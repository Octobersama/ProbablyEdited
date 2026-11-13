#!/usr/bin/env python3
"""Prepare a local ProbablyEdited release, optionally publishing it to GitHub."""

from __future__ import annotations

import argparse
import base64
import hashlib
import html
import io
import json
import os
import re
import shutil
import stat
import subprocess
import sys
import tarfile
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request
import zipfile
from dataclasses import dataclass
from pathlib import Path, PurePosixPath
from typing import Iterable

PROJECT_NAME = "ProbablyEdited"
PROJECT_FILE = "ProbablyEdited.csproj"
MANIFEST_FILE = "manifest.xml"
CHANGELOG_FILE = "CHANGELOG.md"
WORKFLOW_FILE = ".github/workflows/console-checks.yml"
TOKEN_ENV = "GITHUB_PERSONAL_ACCESS_TOKEN"
API_ROOT = "https://api.github.com"
API_VERSION = "2022-11-28"
CHECK_WAIT_SECONDS = 30 * 60
CHECK_POLL_SECONDS = 15
HTTP_TIMEOUT_SECONDS = 60
VERSION_PATTERN = re.compile(
    r"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)"
    r"(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$"
)
CHANGELOG_HEADING = re.compile(r"(?m)^##[ \t]+(\S+)[ \t]*$")
MANAGED_FILES = (PROJECT_FILE, MANIFEST_FILE, CHANGELOG_FILE)
ROOT = Path(__file__).resolve().parents[1]


class ReleaseError(Exception):
    """An actionable release-tooling failure safe to show in command output."""


@dataclass(frozen=True)
class FileHash:
    path: str
    sha512: str
    md5: str


@dataclass(frozen=True)
class ReleaseArtifacts:
    zip_path: Path
    notes_path: Path
    file_count: int


class NoAuthCrossOriginRedirect(urllib.request.HTTPRedirectHandler):
    """Do not forward credentials when urllib follows a redirect to another origin."""

    def redirect_request(self, request, response, code, message, headers, new_url):
        redirected = super().redirect_request(
            request, response, code, message, headers, new_url
        )
        if redirected is None or _url_origin(request.full_url) == _url_origin(new_url):
            return redirected

        sensitive_headers = {"authorization", "proxy-authorization", "cookie"}
        for header_map in (redirected.headers, redirected.unredirected_hdrs):
            for name in tuple(header_map):
                if name.lower() in sensitive_headers:
                    del header_map[name]
        return redirected


def _url_origin(url: str) -> tuple[str, str, int | None]:
    parsed = urllib.parse.urlsplit(url)
    return parsed.scheme.lower(), (parsed.hostname or "").lower(), parsed.port


class GitHubAPI:
    def __init__(self, token: str, opener=None):
        self.token = token
        self.opener = opener or urllib.request.build_opener(
            NoAuthCrossOriginRedirect()
        )

    def request(
        self,
        method: str,
        path_or_url: str,
        *,
        data: bytes | dict | None = None,
        accept: str = "application/vnd.github+json",
        content_type: str | None = None,
        allow_not_found: bool = False,
    ):
        url = (
            path_or_url
            if path_or_url.startswith("https://")
            else f"{API_ROOT}{path_or_url}"
        )
        headers = {
            "Accept": accept,
            "Authorization": f"Bearer {self.token}",
            "User-Agent": "probably-mod-release-tool",
            "X-GitHub-Api-Version": API_VERSION,
        }
        body = data
        if isinstance(data, dict):
            body = json.dumps(data, ensure_ascii=False).encode("utf-8")
            headers["Content-Type"] = "application/json"
        elif content_type:
            headers["Content-Type"] = content_type

        request = urllib.request.Request(url, data=body, headers=headers, method=method)
        try:
            with self.opener.open(request, timeout=HTTP_TIMEOUT_SECONDS) as response:
                response_body = response.read()
        except urllib.error.HTTPError as error:
            if allow_not_found and error.code == 404:
                return None
            raise ReleaseError(
                f"GitHub {method} request failed with HTTP {error.code}. "
                f"Check repository access and the PAT's Contents: read/write and "
                f"Actions: read permissions."
            ) from None
        except urllib.error.URLError:
            raise ReleaseError(
                f"GitHub {method} request failed because the service could not be reached."
            ) from None
        except OSError:
            raise ReleaseError(
                f"GitHub {method} request failed because the connection could not be completed."
            ) from None

        if accept == "application/octet-stream":
            return response_body
        try:
            return json.loads(response_body.decode("utf-8"))
        except (UnicodeDecodeError, json.JSONDecodeError):
            raise ReleaseError(
                f"GitHub {method} request returned an invalid JSON response."
            ) from None

    def json(self, method: str, path: str, **kwargs):
        return self.request(method, path, **kwargs)


def validate_version(version: str) -> str:
    match = VERSION_PATTERN.fullmatch(version)
    if not match:
        raise ReleaseError(
            "Version must be SemVer X.Y.Z, optionally followed by a prerelease "
            "such as 2.1.0-rc.1; build metadata is not supported."
        )
    prerelease = match.group(4)
    if prerelease:
        for identifier in prerelease.split("."):
            if identifier.isdigit() and len(identifier) > 1 and identifier.startswith("0"):
                raise ReleaseError("Numeric prerelease identifiers cannot have leading zeroes.")
    return version


def _xml_element_match(data: bytes, tag: str, path: Path):
    text = data.decode("utf-8")
    matches = list(re.finditer(fr"(<{re.escape(tag)}>)([^<>]*)(</{re.escape(tag)}>)", text))
    if len(matches) != 1:
        raise ReleaseError(
            f"{path.name} must contain exactly one <{tag}>version</{tag}> element."
        )
    return text, matches[0]


def _element_value(data: bytes, tag: str, path: Path) -> str:
    _, match = _xml_element_match(data, tag, path)
    return match.group(2).strip()


def _replace_element(data: bytes, tag: str, version: str, path: Path) -> bytes:
    text, match = _xml_element_match(data, tag, path)
    current = match.group(2)
    leading = current[: len(current) - len(current.lstrip())]
    trailing = current[len(current.rstrip()) :]
    replacement = match.group(1) + leading + version + trailing + match.group(3)
    return (text[: match.start()] + replacement + text[match.end() :]).encode("utf-8")


def _atomic_write(path: Path, content: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    descriptor, temporary_name = tempfile.mkstemp(
        prefix=f".{path.name}.", suffix=".tmp", dir=path.parent
    )
    temporary_path = Path(temporary_name)
    try:
        with os.fdopen(descriptor, "wb") as temporary:
            temporary.write(content)
        os.replace(temporary_path, path)
    except OSError:
        temporary_path.unlink(missing_ok=True)
        raise


def sync_versions(root: Path, version: str) -> None:
    project_path = root / PROJECT_FILE
    manifest_path = root / MANIFEST_FILE
    project_data = project_path.read_bytes()
    manifest_data = manifest_path.read_bytes()
    new_project = _replace_element(project_data, "Version", version, project_path)
    new_manifest = _replace_element(manifest_data, "ModVersion", version, manifest_path)
    if new_project != project_data:
        _atomic_write(project_path, new_project)
    if new_manifest != manifest_data:
        _atomic_write(manifest_path, new_manifest)


def extract_changelog_section(changelog_path: Path, version: str) -> str:
    text = changelog_path.read_text(encoding="utf-8")
    headings = list(CHANGELOG_HEADING.finditer(text))
    matches = [index for index, heading in enumerate(headings) if heading.group(1) == version]
    if not matches:
        raise ReleaseError(
            f"{changelog_path.name} has no '## {version}' section. Add the manual "
            "release notes before preparing this version."
        )
    if len(matches) != 1:
        raise ReleaseError(f"{changelog_path.name} contains more than one '## {version}' section.")

    index = matches[0]
    end = headings[index + 1].start() if index + 1 < len(headings) else len(text)
    section = text[headings[index].start() : end].strip()
    if not section or section.count("\n") == 0 or not section.splitlines()[1:]:
        raise ReleaseError(f"The '## {version}' section in {changelog_path.name} is empty.")
    has_checksum_table = re.search(
        r"(?im)^[ \t]*\|[^\n]*(?:sha-?512|md5)[^\n]*\|[ \t]*$", section
    )
    has_generated_checksum_heading = re.search(
        r"(?im)^#{1,6}[ \t]+File checksums[ \t]*$", section
    )
    if has_checksum_table or has_generated_checksum_heading:
        raise ReleaseError(
            f"Keep {changelog_path.name} semantic and notes-only; checksum tables are "
            "generated from the completed ZIP."
        )
    return section


def _dist_inventory(dist_root: Path) -> list[tuple[str, Path]]:
    if not dist_root.is_dir():
        raise ReleaseError(f"Build output folder was not created: {dist_root}")
    inventory: list[tuple[str, Path]] = []
    for path in dist_root.rglob("*"):
        if path.is_symlink():
            raise ReleaseError(f"Release packaging does not support symlinks under dist: {path}")
        if path.is_file():
            inventory.append((path.relative_to(dist_root).as_posix(), path))
        elif not path.is_dir():
            raise ReleaseError(f"Release packaging found a non-regular dist entry: {path}")
    inventory.sort(key=lambda entry: entry[0])
    if not inventory:
        raise ReleaseError(f"No files were found under the build output folder: {dist_root}")
    return inventory


def package_dist(dist_root: Path, zip_path: Path) -> list[FileHash]:
    inventory = _dist_inventory(dist_root)
    expected_names = [name for name, _ in inventory]
    zip_path.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(
        zip_path, mode="w", compression=zipfile.ZIP_DEFLATED, compresslevel=9
    ) as archive:
        for name, path in inventory:
            info = zipfile.ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))
            info.create_system = 3
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = (stat.S_IFREG | 0o644) << 16
            archive.writestr(
                info,
                path.read_bytes(),
                compress_type=zipfile.ZIP_DEFLATED,
                compresslevel=9,
            )

    hashes: list[FileHash] = []
    with zipfile.ZipFile(zip_path, mode="r") as archive:
        actual_names = [info.filename for info in archive.infolist()]
        if actual_names != expected_names:
            raise ReleaseError("ZIP contents do not match the current dist inventory.")
        for name in actual_names:
            try:
                archived_bytes = archive.read(name)
            except (OSError, zipfile.BadZipFile, RuntimeError):
                raise ReleaseError(f"Could not read archived file {name!r} for checksum generation.") from None
            hashes.append(
                FileHash(
                    path=name,
                    sha512=hashlib.sha512(archived_bytes).hexdigest(),
                    md5=hashlib.md5(archived_bytes, usedforsecurity=False).hexdigest(),
                )
            )
    return hashes


def _markdown_path(path: str) -> str:
    escaped = html.escape(path, quote=False).replace("|", "&#124;")
    escaped = escaped.replace("\r", "&#13;").replace("\n", "&#10;")
    return f"<code>{escaped}</code>"


def render_release_notes(section: str, file_hashes: Iterable[FileHash]) -> str:
    lines = [
        section.rstrip(),
        "",
        "### File checksums",
        "",
        "These SHA-512 and MD5 values are calculated from the exact bytes of each file "
        "inside the release ZIP. The ZIP archive itself is not hashed.",
        "",
        "| Path inside ZIP | SHA-512 | MD5 |",
        "| --- | --- | --- |",
    ]
    for item in file_hashes:
        lines.append(
            f"| {_markdown_path(item.path)} | `{item.sha512}` | `{item.md5}` |"
        )
    return "\n".join(lines) + "\n"


def _ignore_build_outputs(directory: str, names: list[str]) -> set[str]:
    current = Path(directory)
    ignored = {name for name in names if name in {"__pycache__", "bin", "obj"}}
    if current == ROOT:
        ignored.update(name for name in names if name in {".git", "build", "dist", ".playwright-mcp"})
    return ignored


def copy_project_for_build(root: Path, destination: Path) -> None:
    shutil.copytree(root, destination, ignore=_ignore_build_outputs)


def extract_git_archive(root: Path, commit: str, destination: Path) -> None:
    result = subprocess.run(
        ["git", "archive", "--format=tar", commit],
        cwd=root,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )
    if result.returncode != 0:
        raise ReleaseError(f"Could not read committed candidate {commit} from Git.")
    destination.mkdir(parents=True, exist_ok=True)
    destination_root = destination.resolve()
    try:
        with tarfile.open(fileobj=io.BytesIO(result.stdout), mode="r:") as archive:
            for member in archive:
                member_path = PurePosixPath(member.name)
                if (
                    member_path.is_absolute()
                    or ".." in member_path.parts
                    or "\\" in member.name
                    or not member_path.parts
                ):
                    raise ReleaseError("Committed candidate contains an unsafe archive path.")
                target = destination.joinpath(*member_path.parts)
                if not target.resolve().is_relative_to(destination_root):
                    raise ReleaseError("Committed candidate contains an unsafe archive path.")
                if member.isdir():
                    target.mkdir(parents=True, exist_ok=True)
                    continue
                if not member.isfile():
                    raise ReleaseError(
                        "Committed candidate contains a symlink or unsupported archive entry."
                    )
                source = archive.extractfile(member)
                if source is None:
                    raise ReleaseError("Could not read a file from the committed candidate archive.")
                target.parent.mkdir(parents=True, exist_ok=True)
                with source, target.open("wb") as output:
                    shutil.copyfileobj(source, output)
                os.chmod(target, 0o755 if member.mode & 0o111 else 0o644)
    except (OSError, tarfile.TarError):
        raise ReleaseError("Could not extract the committed candidate for its Release build.") from None


def build_project(source_root: Path, game_directory: Path | None) -> None:
    command = ["dotnet", "build", PROJECT_FILE, "--configuration", "Release"]
    if game_directory is not None:
        command.append(f"-p:GameDirectory={game_directory}")
    print(f"Building {PROJECT_NAME} Release from {source_root}...")
    try:
        result = subprocess.run(command, cwd=source_root, check=False)
    except OSError:
        raise ReleaseError("Could not start dotnet. Install the .NET SDK and retry.") from None
    if result.returncode != 0:
        raise ReleaseError(
            f"Release build failed with dotnet exit code {result.returncode}; "
            "no release package was published."
        )


def write_artifacts(
    repository_root: Path,
    source_root: Path,
    version: str,
    section: str,
) -> ReleaseArtifacts:
    project_path = source_root / PROJECT_FILE
    manifest_path = source_root / MANIFEST_FILE
    if _element_value(project_path.read_bytes(), "Version", project_path) != version:
        raise ReleaseError(f"Committed project Version does not match {version}.")
    if _element_value(manifest_path.read_bytes(), "ModVersion", manifest_path) != version:
        raise ReleaseError(f"Committed manifest ModVersion does not match {version}.")

    output_dir = repository_root / "build" / "releases" / version
    output_dir.mkdir(parents=True, exist_ok=True)
    zip_path = output_dir / f"{PROJECT_NAME}-{version}.zip"
    notes_path = output_dir / "release-notes.md"
    with tempfile.TemporaryDirectory(prefix=".release-", dir=output_dir) as temporary:
        temporary_root = Path(temporary)
        temporary_zip = temporary_root / zip_path.name
        hashes = package_dist(source_root / "dist", temporary_zip)
        notes = render_release_notes(section, hashes).encode("utf-8")
        temporary_notes = temporary_root / notes_path.name
        temporary_notes.write_bytes(notes)
        os.replace(temporary_zip, zip_path)
        os.replace(temporary_notes, notes_path)
    return ReleaseArtifacts(zip_path, notes_path, len(hashes))


def prepare_local(
    root: Path, version: str, game_directory: Path | None
) -> ReleaseArtifacts:
    section = extract_changelog_section(root / CHANGELOG_FILE, version)
    sync_versions(root, version)
    with tempfile.TemporaryDirectory(prefix=f"{PROJECT_NAME}-release-build-") as temporary:
        source_root = Path(temporary) / PROJECT_NAME
        copy_project_for_build(root, source_root)
        build_project(source_root, game_directory)
        artifacts = write_artifacts(root, source_root, version, section)
    return artifacts


def _git(root: Path, *arguments: str, env=None, check: bool = True) -> subprocess.CompletedProcess:
    try:
        result = subprocess.run(
            ["git", *arguments],
            cwd=root,
            env=env,
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            check=False,
        )
    except OSError:
        raise ReleaseError("Could not start Git. Install Git and retry.") from None
    if check and result.returncode != 0:
        operation = arguments[0] if arguments else "command"
        raise ReleaseError(f"Git {operation} failed with exit code {result.returncode}.")
    return result


def _git_output(root: Path, *arguments: str, env=None) -> str:
    return _git(root, *arguments, env=env).stdout.strip()


def parse_github_remote(remote_url: str) -> tuple[str, str, bool]:
    if remote_url.startswith("git@github.com:"):
        path = remote_url.split(":", 1)[1]
        https_transport = False
    else:
        parsed = urllib.parse.urlsplit(remote_url)
        if parsed.hostname != "github.com" or parsed.scheme not in {"https", "ssh"}:
            raise ReleaseError("The origin remote must point to github.com over HTTPS or SSH.")
        if parsed.password or (parsed.scheme == "https" and parsed.username):
            raise ReleaseError("Remove embedded credentials from the origin URL and retry.")
        path = parsed.path.lstrip("/")
        https_transport = parsed.scheme == "https"
    path = path.removesuffix(".git").strip("/")
    parts = path.split("/")
    if len(parts) != 2 or not all(parts):
        raise ReleaseError("Could not determine GitHub owner and repository from origin.")
    return parts[0], parts[1], https_transport


def _git_environment(token: str, use_https: bool) -> dict[str, str]:
    environment = os.environ.copy()
    if not use_https:
        return environment
    try:
        index = int(environment.get("GIT_CONFIG_COUNT", "0"))
    except ValueError:
        raise ReleaseError("GIT_CONFIG_COUNT must be an integer to use the HTTPS PAT safely.") from None
    authorization = base64.b64encode(f"x-access-token:{token}".encode("utf-8")).decode("ascii")
    environment[f"GIT_CONFIG_KEY_{index}"] = "http.https://github.com/.extraheader"
    environment[f"GIT_CONFIG_VALUE_{index}"] = f"AUTHORIZATION: basic {authorization}"
    environment["GIT_CONFIG_COUNT"] = str(index + 1)
    return environment


def _remote_ref(root: Path, ref: str, env: dict[str, str]) -> dict[str, str]:
    output = _git_output(root, "ls-remote", "origin", ref, env=env)
    result: dict[str, str] = {}
    for line in output.splitlines():
        object_id, remote_ref = line.split("\t", 1)
        result[remote_ref] = object_id
    return result


def _require_synced_branch(
    root: Path, remote_ref: str, token: str, use_https: bool
) -> tuple[str, dict[str, str]]:
    env = _git_environment(token, use_https)
    head = _git_output(root, "rev-parse", "HEAD")
    remote_refs = _remote_ref(root, remote_ref, env)
    if remote_refs.get(remote_ref) != head:
        raise ReleaseError(
            "The current branch must be fully synchronized with origin before publishing. "
            "Push or reconcile existing commits first; no unrelated commits will be pushed."
        )
    return head, env


def find_release_by_tag(api: GitHubAPI, owner: str, repo: str, tag: str) -> dict | None:
    releases_path = (
        f"/repos/{urllib.parse.quote(owner, safe='')}/"
        f"{urllib.parse.quote(repo, safe='')}/releases"
    )
    page = 1
    while True:
        query = urllib.parse.urlencode({"per_page": 100, "page": page})
        releases = api.json("GET", f"{releases_path}?{query}")
        if not isinstance(releases, list):
            raise ReleaseError("GitHub returned an invalid releases listing.")
        for item in releases:
            if isinstance(item, dict) and item.get("tag_name") == tag:
                return item
        if len(releases) < 100:
            return None
        page += 1


def commit_release_files(root: Path, version: str) -> bool:
    paths = list(MANAGED_FILES)
    status = _git(
        root,
        "status",
        "--porcelain",
        "--untracked-files=all",
        "--",
        *paths,
    )
    if not status.stdout.strip():
        return False
    staged = _git(root, "add", "--", *paths, check=False)
    if staged.returncode != 0:
        raise ReleaseError("Could not stage the explicitly managed release files.")
    result = _git(
        root,
        "commit",
        "--only",
        "-m",
        f"Release v{version}",
        "--",
        *paths,
        check=False,
    )
    if result.returncode != 0:
        raise ReleaseError(
            "Could not commit the release files. Configure Git author identity or resolve "
            "the reported commit-hook failure, then retry."
        )
    return True


def _push_branch(
    root: Path, branch: str, candidate: str, env: dict[str, str]
) -> None:
    result = _git(
        root,
        "push",
        "origin",
        f"HEAD:refs/heads/{branch}",
        env=env,
        check=False,
    )
    remote_ref = f"refs/heads/{branch}"
    if result.returncode != 0:
        remote_refs = _remote_ref(root, remote_ref, env)
        if remote_refs.get(remote_ref) != candidate:
            raise ReleaseError(
                "Could not push the release commit. It remains local; restore GitHub access, "
                f"push it with 'git push origin HEAD:{remote_ref}', then rerun."
            )
    remote_refs = _remote_ref(root, remote_ref, env)
    if remote_refs.get(remote_ref) != candidate:
        raise ReleaseError("Origin did not confirm the exact release commit after push.")


def _matching_workflow_runs(
    api: GitHubAPI, owner: str, repo: str, candidate: str
) -> list[dict]:
    query = urllib.parse.urlencode({"head_sha": candidate, "per_page": 100})
    response = api.json(
        "GET",
        f"/repos/{urllib.parse.quote(owner, safe='')}/{urllib.parse.quote(repo, safe='')}"
        f"/actions/workflows/console-checks.yml/runs?{query}",
    )
    runs = response.get("workflow_runs") if isinstance(response, dict) else None
    if not isinstance(runs, list):
        raise ReleaseError("GitHub returned an invalid console-check workflow response.")
    suffix = WORKFLOW_FILE
    return [
        run
        for run in runs
        if isinstance(run, dict)
        and str(run.get("head_sha", "")).lower() == candidate.lower()
        and run.get("event") in {"push", "workflow_dispatch"}
        and str(run.get("path", "")).removeprefix("./").endswith(suffix)
    ]


def wait_for_console_checks(
    api: GitHubAPI, owner: str, repo: str, candidate: str
) -> None:
    print(f"Waiting for console-check CI on exact release commit {candidate}...")
    deadline = time.monotonic() + CHECK_WAIT_SECONDS
    while True:
        runs = _matching_workflow_runs(api, owner, repo, candidate)
        if any(run.get("status") == "completed" and run.get("conclusion") == "success" for run in runs):
            print("Console-check CI succeeded for the exact release commit.")
            return

        pending = [run for run in runs if run.get("status") != "completed"]
        completed = [run for run in runs if run.get("status") == "completed"]
        if completed and not pending:
            latest = max(completed, key=lambda run: str(run.get("created_at", "")))
            conclusion = latest.get("conclusion") or "unknown"
            url = latest.get("html_url") or "the Actions workflow page"
            raise ReleaseError(
                f"Console-check CI for {candidate} concluded '{conclusion}' ({url}). "
                "A skipped, cancelled, or failed workflow is not a pass; fix or rerun the "
                "checks for this exact commit before publishing."
            )
        if time.monotonic() >= deadline:
            raise ReleaseError(
                f"No successful console-check workflow appeared for exact commit {candidate}. "
                "The workflow may have been skipped. Open the repository's Actions page, "
                "run Editor console checks on this unchanged branch head, and rerun the "
                "release command; no tag or release was created."
            )
        time.sleep(CHECK_POLL_SECONDS)


def _remote_tag(root: Path, tag: str, env: dict[str, str]) -> dict[str, str]:
    base = f"refs/tags/{tag}"
    return _remote_ref(root, base, env) | _remote_ref(root, f"{base}^{{}}", env)


def _local_tag(root: Path, tag: str) -> tuple[bool, bool, str | None]:
    ref = f"refs/tags/{tag}"
    exists = _git(root, "show-ref", "--verify", "--quiet", ref, check=False)
    if exists.returncode == 1:
        return False, False, None
    if exists.returncode != 0:
        raise ReleaseError("Could not inspect the local release tag.")
    object_type = _git_output(root, "cat-file", "-t", ref)
    commit = _git_output(root, "rev-parse", f"{ref}^{{}}")
    return True, object_type == "tag", commit


def ensure_annotated_tag(
    root: Path,
    tag: str,
    candidate: str,
    env: dict[str, str],
) -> None:
    local_exists, local_annotated, local_commit = _local_tag(root, tag)
    if local_exists and (not local_annotated or local_commit != candidate):
        raise ReleaseError(
            f"Local tag {tag} already exists but is not the expected annotated tag for "
            "this candidate; it will not be replaced."
        )

    remote = _remote_tag(root, tag, env)
    tag_ref = f"refs/tags/{tag}"
    peeled_ref = f"{tag_ref}^{{}}"
    if remote:
        if peeled_ref not in remote or remote[peeled_ref] != candidate:
            raise ReleaseError(
                f"Remote tag {tag} already exists but is not an annotated tag for this "
                "candidate; it will not be replaced."
            )
        return

    if not local_exists:
        result = _git(
            root,
            "tag",
            "-a",
            tag,
            "-m",
            f"{PROJECT_NAME} {tag}",
            candidate,
            check=False,
        )
        if result.returncode != 0:
            raise ReleaseError(f"Could not create annotated tag {tag}.")

    push_result = _git(root, "push", "origin", f"refs/tags/{tag}", env=env, check=False)
    if push_result.returncode != 0:
        remote = _remote_tag(root, tag, env)
        if remote.get(peeled_ref) != candidate:
            raise ReleaseError(
                f"Could not push annotated tag {tag}. The local tag was retained; inspect "
                "origin and rerun only after confirming no conflicting tag exists."
            )
    remote = _remote_tag(root, tag, env)
    if peeled_ref not in remote or remote[peeled_ref] != candidate:
        raise ReleaseError(f"Origin did not confirm annotated tag {tag} at the release commit.")


def _upload_url(template: str, asset_name: str) -> str:
    url = template.split("{", 1)[0]
    parsed = urllib.parse.urlsplit(url)
    if parsed.scheme != "https" or parsed.hostname != "uploads.github.com":
        raise ReleaseError("GitHub returned an unexpected release-asset upload URL.")
    query = urllib.parse.parse_qsl(parsed.query, keep_blank_values=True)
    query.append(("name", asset_name))
    return urllib.parse.urlunsplit(
        (parsed.scheme, parsed.netloc, parsed.path, urllib.parse.urlencode(query), "")
    )


def _verify_uploaded_asset(
    api: GitHubAPI,
    owner: str,
    repo: str,
    asset: dict,
    zip_path: Path,
) -> None:
    if (
        asset.get("name") != zip_path.name
        or asset.get("state") != "uploaded"
        or asset.get("size") != zip_path.stat().st_size
    ):
        raise ReleaseError(
            "GitHub did not confirm a complete uploaded ZIP; the release remains a draft."
        )
    asset_id = asset.get("id")
    if not isinstance(asset_id, int):
        raise ReleaseError("GitHub did not return a valid uploaded asset ID; the release remains a draft.")
    asset_url = (
        f"/repos/{urllib.parse.quote(owner, safe='')}/{urllib.parse.quote(repo, safe='')}"
        f"/releases/assets/{asset_id}"
    )
    downloaded = api.request("GET", asset_url, accept="application/octet-stream")
    if downloaded != zip_path.read_bytes():
        raise ReleaseError(
            "The downloaded GitHub asset bytes differ from the built ZIP; the release remains a draft."
        )


def publish_release(
    root: Path,
    version: str,
    game_directory: Path | None,
    token: str,
) -> tuple[ReleaseArtifacts, str]:
    changelog_section = extract_changelog_section(root / CHANGELOG_FILE, version)
    remote_url = _git_output(root, "remote", "get-url", "origin")
    owner, repo, use_https = parse_github_remote(remote_url)
    branch = _git_output(root, "symbolic-ref", "--quiet", "--short", "HEAD")
    remote_branch = f"refs/heads/{branch}"
    original_head, git_env = _require_synced_branch(
        root, remote_branch, token, use_https
    )
    api = GitHubAPI(token)
    tag = f"v{version}"
    existing_release = find_release_by_tag(api, owner, repo, tag)
    if existing_release is not None:
        state = "draft" if existing_release.get("draft") else "published"
        raise ReleaseError(
            f"GitHub already has a {state} release for {tag}; it will not be changed."
        )

    sync_versions(root, version)
    committed = commit_release_files(root, version)
    candidate = _git_output(root, "rev-parse", "HEAD")
    if not committed and candidate != original_head:
        raise ReleaseError("The release candidate changed unexpectedly before publication.")
    if committed:
        _push_branch(root, branch, candidate, git_env)
    else:
        _require_synced_branch(root, remote_branch, token, use_https)
    wait_for_console_checks(api, owner, repo, candidate)

    with tempfile.TemporaryDirectory(prefix=f"{PROJECT_NAME}-candidate-") as temporary:
        source_root = Path(temporary) / PROJECT_NAME
        extract_git_archive(root, candidate, source_root)
        candidate_section = extract_changelog_section(source_root / CHANGELOG_FILE, version)
        build_project(source_root, game_directory)
        artifacts = write_artifacts(root, source_root, version, candidate_section)

    ensure_annotated_tag(root, tag, candidate, git_env)
    if find_release_by_tag(api, owner, repo, tag) is not None:
        raise ReleaseError(
            f"GitHub created a release for {tag} while this command was running; it was not modified."
        )

    draft = api.json(
        "POST",
        f"/repos/{urllib.parse.quote(owner, safe='')}/{urllib.parse.quote(repo, safe='')}/releases",
        data={
            "tag_name": tag,
            "target_commitish": candidate,
            "name": f"{PROJECT_NAME} {version}",
            "body": artifacts.notes_path.read_text(encoding="utf-8"),
            "draft": True,
            "prerelease": "-" in version,
            "generate_release_notes": False,
        },
    )
    if (
        not isinstance(draft, dict)
        or draft.get("tag_name") != tag
        or draft.get("draft") is not True
        or not isinstance(draft.get("id"), int)
        or not isinstance(draft.get("upload_url"), str)
    ):
        raise ReleaseError(
            "GitHub did not confirm creation of the expected draft release; inspect the tag "
            "and release page before retrying."
        )

    upload_url = _upload_url(draft["upload_url"], artifacts.zip_path.name)
    upload = api.request(
        "POST",
        upload_url,
        data=artifacts.zip_path.read_bytes(),
        accept="application/vnd.github+json",
        content_type="application/zip",
    )
    if not isinstance(upload, dict):
        raise ReleaseError("GitHub returned an invalid asset upload response; the release remains a draft.")
    _verify_uploaded_asset(api, owner, repo, upload, artifacts.zip_path)

    published = api.json(
        "PATCH",
        f"/repos/{urllib.parse.quote(owner, safe='')}/{urllib.parse.quote(repo, safe='')}"
        f"/releases/{draft['id']}",
        data={"draft": False, "prerelease": "-" in version},
    )
    if (
        not isinstance(published, dict)
        or published.get("draft") is not False
        or published.get("prerelease") is not ("-" in version)
    ):
        raise ReleaseError(
            "GitHub did not confirm the final release state. Inspect the release page before retrying."
        )
    return artifacts, str(published.get("html_url") or "")


def _game_directory(value: str | None) -> Path | None:
    if value is None:
        return None
    path = Path(value).expanduser().resolve()
    if not path.is_dir():
        raise ReleaseError(f"GameDirectory is not an existing folder: {path}")
    return path


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description=f"Prepare a local {PROJECT_NAME} release; --publish enables GitHub operations."
    )
    parser.add_argument("--version", required=True, help="SemVer X.Y.Z or X.Y.Z-prerelease")
    parser.add_argument("--game-directory", help="Override the local Probably Stolen install folder")
    parser.add_argument("--publish", action="store_true", help="Commit, push, check CI, and publish to GitHub")
    arguments = parser.parse_args(argv)

    try:
        version = validate_version(arguments.version)
        game_directory = _game_directory(arguments.game_directory)
        if arguments.publish:
            token = os.environ.get(TOKEN_ENV)
            if not token:
                raise ReleaseError(
                    f"Set {TOKEN_ENV} in the environment to use --publish. Local preparation "
                    "does not require a token."
                )
            artifacts, release_url = publish_release(ROOT, version, game_directory, token)
            print(f"Published {PROJECT_NAME} {version}: {release_url}")
            print(f"Package: {artifacts.zip_path}")
            print(f"Notes: {artifacts.notes_path}")
        else:
            artifacts = prepare_local(ROOT, version, game_directory)
            print(f"Prepared {PROJECT_NAME} {version} locally ({artifacts.file_count} files).")
            print(f"Package: {artifacts.zip_path}")
            print(f"Notes: {artifacts.notes_path}")
    except ReleaseError as error:
        print(f"release: error: {error}", file=sys.stderr)
        return 1
    except (OSError, UnicodeError, zipfile.BadZipFile) as error:
        print(f"release: error: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
