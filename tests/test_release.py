from __future__ import annotations

import io
import os
import subprocess
import tempfile
import unittest
from unittest.mock import patch
import urllib.error
import urllib.request
import zipfile
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from scripts import release


class ReleaseToolingTests(unittest.TestCase):
    def test_version_sync_preserves_xml_bytes_outside_version_values(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            project = root / release.PROJECT_FILE
            manifest = root / release.MANIFEST_FILE
            project_before = (
                b"<Project>\r\n  <Version> 1.0.0 </Version>\r\n"
                b"  <Authors>OctoberSama</Authors>\r\n</Project>\r\n"
            )
            manifest_before = (
                b"<?xml version=\"1.0\"?>\r\n<Manifest>\r\n"
                b"  <ModVersion>1.0.0</ModVersion>\r\n</Manifest>\r\n"
            )
            project.write_bytes(project_before)
            manifest.write_bytes(manifest_before)

            release.sync_versions(root, "1.2.3-rc.1")

            self.assertEqual(
                project.read_bytes(),
                project_before.replace(b"1.0.0", b"1.2.3-rc.1"),
            )
            self.assertEqual(
                manifest.read_bytes(),
                manifest_before.replace(b"1.0.0", b"1.2.3-rc.1"),
            )

    def test_version_sync_validates_both_xml_files_before_writing(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            project = root / release.PROJECT_FILE
            manifest = root / release.MANIFEST_FILE
            project_before = b"<Project><Version>1.0.0</Version></Project>"
            project.write_bytes(project_before)
            manifest.write_bytes(b"<Manifest></Manifest>")

            with self.assertRaises(release.ReleaseError):
                release.sync_versions(root, "2.0.0")

            self.assertEqual(project.read_bytes(), project_before)

    def test_changelog_selects_only_the_exact_version_section(self):
        with tempfile.TemporaryDirectory() as temporary:
            changelog = Path(temporary) / "CHANGELOG.md"
            changelog.write_text(
                "# Changelog\n\n## 1.0.0\n\nFirst release.\n\n"
                "## 1.1.0-rc.1\n\nNew font behavior.\n",
                encoding="utf-8",
            )

            section = release.extract_changelog_section(changelog, "1.0.0")

            self.assertEqual(section, "## 1.0.0\n\nFirst release.")
            self.assertNotIn("New font behavior", section)

    def test_changelog_rejects_missing_empty_and_generated_checksum_notes(self):
        with tempfile.TemporaryDirectory() as temporary:
            changelog = Path(temporary) / "CHANGELOG.md"
            changelog.write_text("# Changelog\n\n## 1.0.0\n\n", encoding="utf-8")
            with self.assertRaisesRegex(release.ReleaseError, "empty"):
                release.extract_changelog_section(changelog, "1.0.0")
            with self.assertRaisesRegex(release.ReleaseError, "no '## 2.0.0'"):
                release.extract_changelog_section(changelog, "2.0.0")

            changelog.write_text(
                "## 1.0.0\n\nAdded automatic SHA-512 and MD5 tables for archived files.\n",
                encoding="utf-8",
            )
            section = release.extract_changelog_section(changelog, "1.0.0")
            self.assertIn("SHA-512", section)
            self.assertIn("MD5", section)

            changelog.write_text(
                "## 1.0.0\n\n"
                "| Path inside ZIP | SHA-512 | MD5 |\n"
                "| --- | --- | --- |\n"
                "| `mod.dll` | `abc` | `def` |\n",
                encoding="utf-8",
            )
            with self.assertRaisesRegex(release.ReleaseError, "notes-only"):
                release.extract_changelog_section(changelog, "1.0.0")

    def test_semver_accepts_stable_and_prerelease_and_rejects_invalid_values(self):
        self.assertEqual(release.validate_version("2.4.0"), "2.4.0")
        self.assertEqual(release.validate_version("2.4.0-rc.1"), "2.4.0-rc.1")
        for version in ("2.4", "02.4.0", "2.4.0-rc.01", "2.4.0+build.2"):
            with self.subTest(version=version), self.assertRaises(release.ReleaseError):
                release.validate_version(version)

    def test_zip_includes_all_dist_files_and_hashes_the_archived_bytes(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            dist = root / "dist"
            (dist / "ProbablyEdited" / "Localization").mkdir(parents=True)
            (dist / "ProbablyEdited" / "Localization" / "en.csv").write_bytes(b"abc")
            (dist / "ProbablyEdited" / "mod.dll").write_bytes(b"\x00\xff")
            (dist / "extra.txt").write_bytes(b"outside the project folder")
            archive_path = root / "ProbablyEdited-1.2.3.zip"

            hashes = release.package_dist(dist, archive_path)

            expected_names = [
                "ProbablyEdited/Localization/en.csv",
                "ProbablyEdited/mod.dll",
                "extra.txt",
            ]
            with zipfile.ZipFile(archive_path) as archive:
                self.assertEqual(archive.namelist(), expected_names)
                self.assertEqual(archive.read(expected_names[0]), b"abc")
            first = hashes[0]
            self.assertEqual(first.path, expected_names[0])
            self.assertEqual(
                first.sha512,
                "ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a"
                "2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f",
            )
            self.assertEqual(first.md5, "900150983cd24fb0d6963f7d28e17f72")

    def test_zip_bytes_are_repeatable_for_unchanged_dist_content(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            dist = root / "dist"
            dist.mkdir()
            source = dist / "item.txt"
            source.write_bytes(b"repeatable bytes")
            first_zip = root / "first.zip"
            second_zip = root / "second.zip"

            release.package_dist(dist, first_zip)
            os.utime(source, (1_700_000_000, 1_700_000_000))
            release.package_dist(dist, second_zip)

            self.assertEqual(first_zip.read_bytes(), second_zip.read_bytes())

    def test_release_notes_include_file_hashes_but_no_archive_hash(self):
        hashes = [
            release.FileHash(
                "ProbablyEdited/thing.txt",
                "a" * 128,
                "b" * 32,
            )
        ]
        notes = release.render_release_notes("## 1.2.3\n\nA real release note.", hashes)

        self.assertIn("ProbablyEdited/thing.txt", notes)
        self.assertIn("`" + "a" * 128 + "`", notes)
        self.assertIn("`" + "b" * 32 + "`", notes)
        self.assertIn("The ZIP archive itself is not hashed.", notes)
        self.assertNotIn("ProbablyEdited-1.2.3.zip", notes)

    def test_cross_origin_redirect_removes_authorization_and_cookies(self):
        request = urllib.request.Request(
            "https://api.github.com/repos/example/project/releases/assets/1",
            headers={"Authorization": "Bearer secret", "Cookie": "private=1"},
        )
        redirected = release.NoAuthCrossOriginRedirect().redirect_request(
            request,
            None,
            302,
            "Found",
            {},
            "https://release-assets.githubusercontent.com/file.zip",
        )

        self.assertIsNotNone(redirected)
        headers = {name.lower(): value for name, value in redirected.header_items()}
        self.assertNotIn("authorization", headers)
        self.assertNotIn("cookie", headers)

    def test_github_http_errors_do_not_expose_token_or_response_body(self):
        token = "example-secret-token"

        class FailingOpener:
            def open(self, request, timeout):
                self.request = request
                raise urllib.error.HTTPError(
                    request.full_url,
                    403,
                    "Forbidden",
                    hdrs=None,
                    fp=io.BytesIO(token.encode("utf-8")),
                )

        opener = FailingOpener()
        api = release.GitHubAPI(token, opener=opener)
        with self.assertRaises(release.ReleaseError) as raised:
            api.json("GET", "/repos/example/project/releases")

        self.assertEqual(opener.request.get_header("Authorization"), f"Bearer {token}")
        self.assertNotIn(token, str(raised.exception))

    def test_ci_gate_ignores_pr_runs_and_accepts_dispatch_for_exact_sha(self):
        candidate = "a" * 40
        matching = {
            "head_sha": candidate,
            "path": ".github/workflows/console-checks.yml",
            "event": "push",
            "status": "completed",
            "conclusion": "success",
            "created_at": "2026-11-13T12:00:00Z",
        }

        class StaticAPI:
            def __init__(self, runs):
                self.runs = runs

            def json(self, method, path):
                return {"workflow_runs": self.runs}

        mismatched = StaticAPI(
            [
                {**matching, "head_sha": "b" * 40},
                {**matching, "path": ".github/workflows/other.yml"},
                {**matching, "event": "pull_request"},
                {**matching, "status": "in_progress", "conclusion": None},
            ]
        )
        with patch.object(release, "CHECK_WAIT_SECONDS", 0):
            with self.assertRaisesRegex(release.ReleaseError, "No successful console-check"):
                release.wait_for_console_checks(mismatched, "owner", "repo", candidate)

        release.wait_for_console_checks(
            StaticAPI([{**matching, "event": "workflow_dispatch"}]),
            "owner",
            "repo",
            candidate,
        )
        with self.assertRaisesRegex(release.ReleaseError, "skipped"):
            release.wait_for_console_checks(
                StaticAPI([{**matching, "conclusion": "skipped"}]),
                "owner",
                "repo",
                candidate,
            )

    def test_existing_draft_release_stops_before_release_changes(self):
        class PaginatedAPI:
            def __init__(self):
                self.paths = []

            def json(self, method, path):
                self.paths.append(path)
                if path.endswith("page=1"):
                    return [{"tag_name": f"v0.{index}.0", "draft": False} for index in range(100)]
                if path.endswith("page=2"):
                    return [{"tag_name": "v1.2.3", "draft": True}]
                raise AssertionError(f"Unexpected releases page: {path}")

        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / release.CHANGELOG_FILE).write_text(
                "## 1.2.3\n\nRelease notes.\n", encoding="utf-8"
            )
            api = PaginatedAPI()

            def git_output(root, *arguments, **kwargs):
                if arguments == ("remote", "get-url", "origin"):
                    return "https://github.com/example/project.git"
                if arguments == ("symbolic-ref", "--quiet", "--short", "HEAD"):
                    return "main"
                raise AssertionError(f"Unexpected Git query: {arguments}")

            with patch.object(release, "_git_output", side_effect=git_output), \
                    patch.object(release, "_require_synced_branch", return_value=("a" * 40, {})), \
                    patch.object(release, "GitHubAPI", return_value=api), \
                    patch.object(release, "sync_versions") as sync_versions, \
                    patch.object(release, "commit_release_files") as commit_release_files, \
                    patch.object(release, "_push_branch") as push_branch:
                with self.assertRaisesRegex(release.ReleaseError, "draft release"):
                    release.publish_release(root, "1.2.3", None, "test-token")

            self.assertEqual(len(api.paths), 2)
            self.assertTrue(api.paths[0].endswith("page=1"))
            self.assertTrue(api.paths[1].endswith("page=2"))
            sync_versions.assert_not_called()
            commit_release_files.assert_not_called()
            push_branch.assert_not_called()

    def test_release_commit_does_not_commit_staged_readme_or_unrelated_work(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            subprocess.run(["git", "init", "--quiet"], cwd=root, check=True)
            subprocess.run(["git", "config", "user.name", "Release Test"], cwd=root, check=True)
            subprocess.run(
                ["git", "config", "user.email", "release-test@example.invalid"],
                cwd=root,
                check=True,
            )
            base_files = {
                release.PROJECT_FILE: "<Project><Version>1.0.0</Version></Project>\n",
                release.MANIFEST_FILE: "<Manifest><ModVersion>1.0.0</ModVersion></Manifest>\n",
                release.CHANGELOG_FILE: "# Changelog\n\n## 1.0.0\n\nNotes.\n",
                "README.md": "manual README text\n",
                "unrelated.txt": "unrelated content\n",
            }
            for name, content in base_files.items():
                (root / name).write_text(content, encoding="utf-8")
            subprocess.run(["git", "add", "."], cwd=root, check=True)
            subprocess.run(["git", "commit", "--quiet", "-m", "baseline"], cwd=root, check=True)

            for name in base_files:
                (root / name).write_text(base_files[name] + "changed\n", encoding="utf-8")
            subprocess.run(["git", "add", "."], cwd=root, check=True)

            self.assertTrue(release.commit_release_files(root, "1.1.0"))

            committed = subprocess.run(
                ["git", "show", "--pretty=format:", "--name-only", "HEAD"],
                cwd=root,
                check=True,
                text=True,
                stdout=subprocess.PIPE,
            ).stdout.splitlines()
            self.assertEqual(set(committed), set(release.MANAGED_FILES))
            readme = subprocess.run(
                ["git", "show", "HEAD:README.md"],
                cwd=root,
                check=True,
                text=True,
                stdout=subprocess.PIPE,
            ).stdout
            self.assertEqual(readme, base_files["README.md"])
            staged_unrelated = subprocess.run(
                ["git", "diff", "--cached", "--name-only"],
                cwd=root,
                check=True,
                text=True,
                stdout=subprocess.PIPE,
            ).stdout.splitlines()
            self.assertEqual(set(staged_unrelated), {"README.md", "unrelated.txt"})


if __name__ == "__main__":
    unittest.main()
