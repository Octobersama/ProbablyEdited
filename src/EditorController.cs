using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

namespace ProbablyEdited
{
    public enum EditorStatusKind
    {
        CatalogReady,
        PresetSelected,
        Message
    }

    public sealed class EditorController : IDisposable
    {
        private const string WorkspaceFile = "presets.json";
        private const string PreviousWorkspaceFile = "presets.previous.json";
        private const string LegacyActiveFile = "active.json";
        private const string LegacyPreviousFile = "previous.json";
        private readonly ModLog log;
        private readonly GameTextCatalog catalog = new GameTextCatalog();
        private readonly Queue<string> pending = new Queue<string>();
        private readonly HashSet<string> queued = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> loadedLocales = new HashSet<string>(StringComparer.Ordinal);
        private PresetWorkspace workspace = new PresetWorkspace();
        private PendingApply pendingApply;
        private bool initialized;
        private bool disposed;
        private bool storageReadable = true;
        private bool runtimeConsistent = true;
        private bool activeSnapshotAvailable = true;
        private string statusKey = "status.preparing";
        private object[] statusArguments = Array.Empty<object>();
        private string statusSuffixKey;
        private string statusSecondarySuffixKey;
        private static readonly Regex Placeholder = new Regex(@"(?<!\{)\{([A-Za-z_][A-Za-z0-9_.]*|\d+)(?=[}:,\[])");
        private static readonly Regex Tag = new Regex(@"</?([A-Za-z][A-Za-z0-9-]*)(?:=[^>]*)?>");

        public EditorState State => workspace.State;
        public IReadOnlyList<PresetInfo> Presets => workspace.Presets;
        public string SelectedPresetId => workspace.SelectedPresetId;
        public string ActivePresetId => workspace.ActivePresetId;
        public bool HasUnsavedChanges => workspace.HasUnsavedChanges;
        public bool CanCreatePreset => workspace.CanCreatePreset;
        public bool CanDeleteSelectedPreset => workspace.CanDeleteSelectedPreset;
        public bool IsLoading { get; private set; }
        public string Status
        {
            get
            {
                string message = statusArguments.Length == 0
                    ? UiText.Get(statusKey)
                    : UiText.Format(statusKey, statusArguments);
                if (statusSuffixKey != null) message += UiText.Get(statusSuffixKey);
                if (statusSecondarySuffixKey != null) message += UiText.Get(statusSecondarySuffixKey);
                return message;
            }
        }
        public EditorStatusKind StatusKind { get; private set; } = EditorStatusKind.Message;
        public IReadOnlyList<string> LocaleCodes => catalog.Codes;
        public IReadOnlyList<string> LocaleNames => catalog.Names;
        public string SelectedLocale { get; private set; } = "en";
        public string DataFolder => ModHelper.GetModDataFolder(ModEntry.ModId);
        public string GameVersion => PlayerStore.VERSION;
        public string CoverageSummary => catalog.Coverage(SelectedLocale);
        public event Action Changed;

        public EditorController(ModLog log)
        {
            this.log = log;
        }

        public void Initialize()
        {
            try
            {
                string workspaceJson = ReadOptionalFile(WorkspaceFile);
                string legacyJson = workspaceJson == null ? ReadOptionalFile(LegacyActiveFile) : null;
                workspace = PresetWorkspace.Load(workspaceJson, legacyJson);
            }
            catch (Exception error)
            {
                storageReadable = false;
                activeSnapshotAvailable = false;
                SetStatus("status.workspace_read_failed", EditorStatusKind.Message, WorkspaceFile, LegacyActiveFile);
                Report(error);
            }

            ModHook.OnModAssetsLoaded += AssetsLoaded;
            LocalizationSettings.SelectedLocaleChanged += LocaleChanged;
            IsLoading = true;
            ModHelper.StartCoroutine(ModEntry.ModId, Guard(InitializeRoutine()));
        }


        private IEnumerator InitializeRoutine()
        {
            var initialization = catalog.Initialize();
            while (initialization.MoveNext())
                yield return initialization.Current;

            initialized = true;
            SelectedLocale = GameTextCatalog.Canonical(catalog.CurrentLocale);
            Enqueue(SelectedLocale);
            foreach (string locale in workspace.GetActiveLocales())
                Enqueue(locale);

            var work = ProcessQueue();
            while (work.MoveNext())
                yield return work.Current;
        }

        public void LoadLocale(string code)
        {
            if (!initialized || disposed || string.IsNullOrWhiteSpace(code))
                return;

            SelectedLocale = GameTextCatalog.Canonical(code);
            if (loadedLocales.Contains(SelectedLocale))
                State.SetCatalog(catalog.Entries(SelectedLocale));
            else
                Enqueue(SelectedLocale);

            StartPending();
        }

        public void SelectPreset(string presetId)
        {
            workspace.SelectPreset(presetId);
            if (loadedLocales.Contains(SelectedLocale))
                State.SetCatalog(catalog.Entries(SelectedLocale));
            else
                Enqueue(SelectedLocale);

            SetStatus("status.preset_selected", EditorStatusKind.PresetSelected, GetPresetLabel(presetId));
            StartPending();
        }

        public string CreatePreset()
        {
            string presetId = workspace.CreatePreset();
            if (loadedLocales.Contains(SelectedLocale))
                State.SetCatalog(catalog.Entries(SelectedLocale));
            else
                Enqueue(SelectedLocale);

            SetStatus("status.preset_created", EditorStatusKind.Message, GetPresetLabel(presetId));
            StartPending();
            return presetId;
        }

        public void DeletePreset(string presetId)
        {
            bool selectionChanged = StringComparer.Ordinal.Equals(workspace.SelectedPresetId, presetId);
            string label = GetPresetLabel(presetId);
            workspace.DeletePreset(presetId);
            if (selectionChanged)
            {
                if (loadedLocales.Contains(SelectedLocale))
                    State.SetCatalog(catalog.Entries(SelectedLocale));
                else
                    Enqueue(SelectedLocale);
            }

            SetStatus("status.preset_removed", EditorStatusKind.Message, label);
            StartPending();
        }

        public string GetPresetName(string presetId)
        {
            return workspace.GetPresetName(presetId);
        }

        public string GetPresetLabel(string presetId)
        {
            return GetPresetName(presetId);
        }

        public void RenamePreset(string presetId, string name)
        {
            workspace.RenamePreset(presetId, name);
            SetStatus("status.preset_renamed", EditorStatusKind.Message, GetPresetLabel(presetId));
            Changed?.Invoke();
        }

        public PresetStatistics GetPresetStatistics(string presetId)
        {
            return workspace.GetPresetStatistics(presetId);
        }

        private void Enqueue(string code, bool reload = false)
        {
            if (string.IsNullOrWhiteSpace(code))
                return;
            if (reload)
                loadedLocales.Remove(code);
            else if (loadedLocales.Contains(code))
                return;
            if (queued.Add(code))
                pending.Enqueue(code);
        }

        private void StartPending()
        {
            if (!initialized || disposed)
                return;
            if (pending.Count > 0 && !IsLoading)
            {
                IsLoading = true;
                ModHelper.StartCoroutine(ModEntry.ModId, Guard(ProcessQueue()));
            }
            Changed?.Invoke();
        }

        private IEnumerator ProcessQueue()
        {
            while (pending.Count > 0)
            {
                string code = pending.Dequeue();
                queued.Remove(code);
                if (loadedLocales.Contains(code))
                    continue;

                SetStatus("status.loading_language", EditorStatusKind.Message, code);
                Changed?.Invoke();
                var load = catalog.Load(code);
                while (load.MoveNext())
                    yield return load.Current;

                if (!queued.Contains(code))
                    loadedLocales.Add(code);
                if (activeSnapshotAvailable)
                {
                    try
                    {
                        catalog.Apply(workspace.ActiveOverrides);
                        GameTextCatalog.RefreshVisibleText();
                    }
                    catch
                    {
                        runtimeConsistent = false;
                        throw;
                    }
                }

                if (StringComparer.Ordinal.Equals(code, SelectedLocale))
                    State.SetCatalog(catalog.Entries(code));
            }

            IsLoading = false;
            GameTextCatalog.RefreshVisibleText();
            if (pendingApply != null)
            {
                CompletePendingApply();
            }
            else
            {
                if (!storageReadable)
                    SetStatus("status.workspace_corrupt", EditorStatusKind.Message, WorkspaceFile);
                else if (workspace.RequiresWorkspaceMigration)
                    SetStatus("status.migration", EditorStatusKind.Message);
                else
                    SetStatus("status.catalog_loaded", EditorStatusKind.CatalogReady, State.Entries.Count, State.OverrideCount);
            }

            log.Log("CATALOG locale=" + SelectedLocale + " entries=" + State.Entries.Count + " overrides=" + State.OverrideCount);
            Changed?.Invoke();
        }

        private IEnumerator Guard(IEnumerator routine)
        {
            while (!disposed)
            {
                bool next;
                object current;
                try
                {
                    next = routine.MoveNext();
                    current = next ? routine.Current : null;
                }
                catch (Exception error)
                {
                    IsLoading = false;
                    if (pendingApply != null)
                    {
                        pendingApply = null;
                        SetStatus("status.preload_failed", EditorStatusKind.Message, error.Message);
                        Report(error);
                    }
                    else
                    {
                        SetStatus("status.read_failed", EditorStatusKind.Message, error.Message);
                        Report(error);
                    }

                    yield break;
                }

                if (!next)
                    break;
                yield return current;
            }

            (routine as IDisposable)?.Dispose();
        }

        public void SaveAndApply()
        {
            if (!storageReadable)
            {
                SetStatus("status.protect_workspace", EditorStatusKind.Message, WorkspaceFile);
                Changed?.Invoke();
                return;
            }
            if (!runtimeConsistent)
            {
                SetStatus("status.runtime_state_unknown", EditorStatusKind.Message);
                Changed?.Invoke();
                return;
            }
            if (pendingApply != null)
            {
                SetStatus("status.apply_pending", EditorStatusKind.Message);
                Changed?.Invoke();
                return;
            }

            string targetPresetId = workspace.SelectedPresetId;
            var locales = new HashSet<string>(StringComparer.Ordinal) { SelectedLocale };
            foreach (string locale in workspace.GetPresetLocales(targetPresetId))
                locales.Add(locale);
            foreach (string locale in workspace.GetActiveLocales())
                locales.Add(locale);

            var apply = new PendingApply(
                targetPresetId,
                workspace.SerializeForSaveAndApply(),
                Snapshot(State.Overrides),
                Snapshot(workspace.ActiveOverrides),
                locales);
            pendingApply = apply;

            foreach (string locale in locales)
                Enqueue(locale);

            if (pending.Count == 0 && !IsLoading)
            {
                CompletePendingApply();
                return;
            }

            SetStatus("status.loading_apply_languages", EditorStatusKind.Message);
            StartPending();
        }

        private void CompletePendingApply()
        {
            PendingApply apply = pendingApply;
            if (apply == null)
                return;

            foreach (string locale in apply.Locales)
            {
                if (!loadedLocales.Contains(locale))
                {
                    Enqueue(locale);
                    IsLoading = true;
                    ModHelper.StartCoroutine(ModEntry.ModId, Guard(ProcessQueue()));
                    return;
                }
            }

            if (!StringComparer.Ordinal.Equals(workspace.SelectedPresetId, apply.TargetPresetId) ||
                !StringComparer.Ordinal.Equals(workspace.SerializeForSaveAndApply(), apply.WorkspaceJson) ||
                !AreEqual(workspace.ActiveOverrides, apply.PreviousOverrides))
            {
                pendingApply = null;
                SetStatus("status.draft_changed_during_preload", EditorStatusKind.Message);
                Changed?.Invoke();
                return;
            }

            string previousWorkspace = null;
            bool wroteWorkspace = false;
            bool runtimeTouched = false;
            int applied = 0;
            try
            {
                try
                {
                    previousWorkspace = ReadOptionalFile(WorkspaceFile);
                    if (previousWorkspace != null)
                        PresetWorkspace.Load(previousWorkspace, null);
                }
                catch
                {
                    storageReadable = false;
                    throw;
                }

                if (previousWorkspace != null)
                    ModHelper.WriteModDataFileText(ModEntry.ModId, PreviousWorkspaceFile, previousWorkspace);

                wroteWorkspace = true;
                ModHelper.WriteModDataFileText(ModEntry.ModId, WorkspaceFile, apply.WorkspaceJson);

                runtimeTouched = true;
                catalog.Apply(apply.PreviousOverrides);
                GameTextCatalog.RefreshVisibleText();
                applied = catalog.Apply(apply.SelectedOverrides);
                GameTextCatalog.RefreshVisibleText();
            }
            catch (Exception error)
            {
                bool diskRollbackFailed = wroteWorkspace && !RollBackWorkspace(previousWorkspace);
                bool runtimeRollbackFailed = false;
                if (runtimeTouched)
                {
                    try
                    {
                        catalog.Apply(apply.PreviousOverrides);
                        GameTextCatalog.RefreshVisibleText();
                    }
                    catch (Exception rollback)
                    {
                        runtimeConsistent = false;
                        runtimeRollbackFailed = true;
                        log.Error("恢复运行时文本失败：" + rollback);
                    }
                }

                pendingApply = null;
                SetStatus("status.apply_failed", EditorStatusKind.Message, error.Message);
                if (diskRollbackFailed)
                    AppendStatus("status.disk_rollback_failed");
                else if (!storageReadable)
                    AppendStatus("status.storage_read_disabled");
                if (runtimeRollbackFailed)
                    AppendStatus("status.runtime_rollback_failed");
                Report(error);
                return;
            }

            workspace.MarkSavedAndApplied();
            pendingApply = null;
            SetStatus("status.saved_and_applied", EditorStatusKind.Message,
                GetPresetLabel(workspace.ActivePresetId), workspace.State.OverrideCount);
            log.Log("SAVED preset=" + workspace.ActivePresetId + " overrides=" + workspace.State.OverrideCount + " applied=" + applied + " locale=" + SelectedLocale);
            Changed?.Invoke();
        }

        private bool RollBackWorkspace(string previousWorkspace)
        {
            try
            {
                if (previousWorkspace == null)
                    ModHelper.DeleteModDataFile(ModEntry.ModId, WorkspaceFile);
                else
                    ModHelper.WriteModDataFileText(ModEntry.ModId, WorkspaceFile, previousWorkspace);
                return true;
            }
            catch (Exception rollback)
            {
                storageReadable = false;
                log.Error("恢复磁盘预设工作区失败：" + rollback);
                return false;
            }
        }

        public void RestoreEntry(TextEntry entry)
        {
            State.Restore(entry);
            NotifyEdited();
        }

        public void NotifyEdited()
        {
            SetStatus(workspace.HasUnsavedChanges ? "status.unsaved" : "status.saved", EditorStatusKind.Message);
            Changed?.Invoke();
        }

        public IReadOnlyList<string> GetImportFiles()
        {
            var files = new List<string>();
            string root = DataFolder.Replace('\\', '/').TrimEnd('/') + "/";
            foreach (string file in ModHelper.ListModDataFiles(ModEntry.ModId))
            {
                string relative = file.Replace('\\', '/');
                if (relative.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    relative = relative.Substring(root.Length);
                if (!relative.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || IsPrivateFile(relative))
                    continue;
                files.Add(relative);
            }

            files.Sort(StringComparer.Ordinal);
            return files;
        }

        public PresetImportPreview PreviewImport(string relativeFile, string targetPresetId)
        {
            if (IsPrivateFile(relativeFile))
                throw new InvalidOperationException(UiText.Get("status.import_file_private"));
            if (!ModHelper.ModDataFileExists(ModEntry.ModId, relativeFile))
                throw new InvalidOperationException(UiText.Get("status.import_file_missing"));

            return workspace.PreviewImport(
                ModHelper.ReadModDataFileText(ModEntry.ModId, relativeFile),
                targetPresetId,
                GameVersion);
        }

        public void ConfirmImport(PresetImportPreview preview, bool overwriteConflicts)
        {
            workspace.ConfirmImport(preview, overwriteConflicts);
            if (loadedLocales.Contains(SelectedLocale))
                State.SetCatalog(catalog.Entries(SelectedLocale));
            else
                Enqueue(SelectedLocale);

            SetStatus("status.import_merged", EditorStatusKind.Message, GetPresetLabel(workspace.SelectedPresetId));
            StartPending();
        }

        public void Export(string fileName, string presetId)
        {
            string name = (fileName ?? string.Empty).Trim();
            if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 5);
            if (name.Length == 0 || name.Length > 80 || name == "." || name == ".." || name.IndexOfAny(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }) >= 0)
                throw new InvalidOperationException(UiText.Get("status.export_filename_invalid"));
            foreach (char character in name)
            {
                if (char.IsControl(character))
                    throw new InvalidOperationException(UiText.Get("status.export_control_characters"));
            }

            string relative = "exports/" + name + ".json";
            if (ModHelper.ModDataFileExists(ModEntry.ModId, relative))
                throw new InvalidOperationException(UiText.Get("status.export_file_exists"));

            string json = workspace.ExportPresetJson(presetId, GameVersion);
            ModHelper.WriteModDataFileText(ModEntry.ModId, relative, json);
            PresetStatistics stats = workspace.GetPresetStatistics(presetId);
            SetStatus("status.exported", EditorStatusKind.Message,
                GetPresetLabel(presetId), stats.OverrideCount, stats.LanguageCount, stats.SourceCount, DataFolder + "/" + relative);
            log.Log("EXPORTED preset=" + presetId + " file=" + relative + " overrides=" + stats.OverrideCount);
            Changed?.Invoke();
        }

        public IReadOnlyList<string> ValidateEdit(TextEntry entry, string text)
        {
            var warnings = new List<string>();
            if (text == null) { warnings.Add(UiText.Get("status.validation.text_null")); return warnings; }
            if (!Tokens(entry.OriginalText, Placeholder).SetEquals(Tokens(text, Placeholder)))
                warnings.Add(UiText.Get("status.validation.placeholders"));
            if (!Tokens(entry.OriginalText, Tag).SetEquals(Tokens(text, Tag)))
                warnings.Add(UiText.Get("status.validation.rich_text"));
            int depth = 0;
            bool bad = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (i + 1 < text.Length && (c == '{' || c == '}') && text[i + 1] == c) { i++; continue; }
                if (c == '{') depth++;
                if (c == '}' && --depth < 0) { bad = true; break; }
            }
            if (depth != 0 || bad) warnings.Add(UiText.Get("status.validation.braces"));
            return warnings;
        }

        private static HashSet<string> Tokens(string text, Regex pattern)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match match in pattern.Matches(text ?? string.Empty))
                result.Add(match.Groups[1].Value);
            return result;
        }

        private static Dictionary<TextId, string> Snapshot(IReadOnlyDictionary<TextId, string> source)
        {
            var copy = new Dictionary<TextId, string>();
            foreach (KeyValuePair<TextId, string> pair in source)
                copy.Add(pair.Key, pair.Value);
            return copy;
        }

        private static bool AreEqual(
            IReadOnlyDictionary<TextId, string> left,
            IReadOnlyDictionary<TextId, string> right)
        {
            if (left.Count != right.Count)
                return false;

            foreach (KeyValuePair<TextId, string> pair in left)
            {
                if (!right.TryGetValue(pair.Key, out string value) ||
                    !StringComparer.Ordinal.Equals(pair.Value, value))
                    return false;
            }

            return true;
        }

        private static bool IsPrivateFile(string path)
        {
            string normalized = (path ?? string.Empty).Replace('\\', '/').TrimStart('/');
            return StringComparer.OrdinalIgnoreCase.Equals(normalized, WorkspaceFile) ||
                   StringComparer.OrdinalIgnoreCase.Equals(normalized, PreviousWorkspaceFile) ||
                   StringComparer.OrdinalIgnoreCase.Equals(normalized, LegacyActiveFile) ||
                   StringComparer.OrdinalIgnoreCase.Equals(normalized, LegacyPreviousFile);
        }

        private static string ReadOptionalFile(string relativePath)
        {
            if (!ModHelper.ModDataFileExists(ModEntry.ModId, relativePath))
                return null;
            return ModHelper.ReadModDataFileText(ModEntry.ModId, relativePath);
        }

        private void AssetsLoaded(LoadedMod mod)
        {
            if (!initialized || disposed)
                return;

            catalog.RefreshLocales();
            Enqueue(SelectedLocale, reload: true);
            foreach (string locale in workspace.GetActiveLocales())
                Enqueue(locale, reload: true);
            foreach (PresetInfo preset in workspace.Presets)
            {
                foreach (string locale in workspace.GetPresetLocales(preset.Id))
                    Enqueue(locale, reload: true);
            }
            StartPending();
        }

        private void LocaleChanged(Locale locale)
        {
            if (!initialized || disposed)
                return;
            LoadLocale(GameTextCatalog.Canonical(locale.Identifier.Code));
        }

        private void SetStatus(string key, EditorStatusKind kind, params object[] arguments)
        {
            statusKey = key;
            statusArguments = arguments ?? Array.Empty<object>();
            statusSuffixKey = null;
            statusSecondarySuffixKey = null;
            StatusKind = kind;
        }

        private void AppendStatus(string key)
        {
            if (statusSuffixKey == null)
                statusSuffixKey = key;
            else
                statusSecondarySuffixKey = key;
        }

        private void Report(Exception error)
        {
            log.Error(Status + "\n" + error);
            Changed?.Invoke();
        }

        public void Dispose()
        {
            disposed = true;
            ModHook.OnModAssetsLoaded -= AssetsLoaded;
            LocalizationSettings.SelectedLocaleChanged -= LocaleChanged;
            ModHelper.StopAllCoroutines(ModEntry.ModId);
            catalog.Dispose();
        }

        private sealed class PendingApply
        {
            public string TargetPresetId { get; }
            public string WorkspaceJson { get; }
            public Dictionary<TextId, string> SelectedOverrides { get; }
            public Dictionary<TextId, string> PreviousOverrides { get; }
            public HashSet<string> Locales { get; }

            public PendingApply(
                string targetPresetId,
                string workspaceJson,
                Dictionary<TextId, string> selectedOverrides,
                Dictionary<TextId, string> previousOverrides,
                HashSet<string> locales)
            {
                TargetPresetId = targetPresetId;
                WorkspaceJson = workspaceJson;
                SelectedOverrides = selectedOverrides;
                PreviousOverrides = previousOverrides;
                Locales = locales;
            }
        }
    }
}
