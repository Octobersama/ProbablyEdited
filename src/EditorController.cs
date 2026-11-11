using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

namespace ProbablyEdited
{
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
        private static readonly Regex Placeholder = new Regex(@"(?<!\{)\{([A-Za-z_][A-Za-z0-9_.]*|\d+)(?=[}:,\[])");
        private static readonly Regex Tag = new Regex(@"</?([A-Za-z][A-Za-z0-9-]*)(?:=[^>]*)?>");

        public EditorState State => workspace.State;
        public IReadOnlyList<PresetInfo> Presets => workspace.Presets;
        public int SelectedPresetSlot => workspace.SelectedPresetSlot;
        public int ActivePresetSlot => workspace.ActivePresetSlot;
        public bool HasUnsavedChanges => workspace.HasUnsavedChanges;
        public bool CanCreatePreset => workspace.CanCreatePreset;
        public bool CanDeleteSelectedPreset => workspace.CanDeleteSelectedPreset;
        public bool IsLoading { get; private set; }
        public string Status { get; private set; } = "正在准备本地化数据库…";
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
                if (workspace.RequiresWorkspaceMigration)
                    PersistLegacyMigration();
            }
            catch (Exception error)
            {
                storageReadable = false;
                activeSnapshotAvailable = false;
                Report("现有预设工作区无法读取；为保护数据，已停止自动应用和保存。请修复 " + WorkspaceFile + "，或在新工作区不存在时恢复 " + LegacyActiveFile + "。", error);
            }

            ModHook.OnModAssetsLoaded += AssetsLoaded;
            LocalizationSettings.SelectedLocaleChanged += LocaleChanged;
            IsLoading = true;
            ModHelper.StartCoroutine(ModEntry.ModId, Guard(InitializeRoutine()));
        }

        private void PersistLegacyMigration()
        {
            string migratedWorkspace = workspace.SerializeWorkspace();
            try
            {
                ModHelper.WriteModDataFileText(ModEntry.ModId, PreviousWorkspaceFile, migratedWorkspace);
                ModHelper.WriteModDataFileText(ModEntry.ModId, WorkspaceFile, migratedWorkspace);
                workspace.MarkWorkspacePersisted();
            }
            catch (Exception error)
            {
                try
                {
                    ModHelper.DeleteModDataFile(ModEntry.ModId, WorkspaceFile);
                }
                catch (Exception rollback)
                {
                    storageReadable = false;
                    log.Error("清理未完成的工作区迁移文件失败：" + rollback);
                }

                if (storageReadable)
                    Status = "旧 active.json 已读取到预设01，但新工作区尚未写入；旧文件仍保留，可稍后重试保存。";
                log.Error("迁移旧 active.json 失败：" + error);
            }
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

        public void SelectPreset(int slot)
        {
            workspace.SelectPreset(slot);
            if (loadedLocales.Contains(SelectedLocale))
                State.SetCatalog(catalog.Entries(SelectedLocale));
            else
                Enqueue(SelectedLocale);

            Status = "已选择“" + GetPresetLabel(slot) + "”；切换预设只改变编辑目标，不会立即应用。";
            StartPending();
        }

        public int CreatePreset()
        {
            int slot = workspace.CreatePreset();
            if (loadedLocales.Contains(SelectedLocale))
                State.SetCatalog(catalog.Entries(SelectedLocale));
            else
                Enqueue(SelectedLocale);

            Status = "已新建“" + GetPresetLabel(slot) + "”；它从当前游戏与官方模组文本基线开始，不包含其他预设的修改。";
            StartPending();
            return slot;
        }
        public void DeletePreset(int slot)
        {
            bool selectionChanged = workspace.SelectedPresetSlot == slot;
            string label = GetPresetLabel(slot);
            workspace.DeletePreset(slot);
            if (selectionChanged)
            {
                if (loadedLocales.Contains(SelectedLocale))
                    State.SetCatalog(catalog.Entries(SelectedLocale));
                else
                    Enqueue(SelectedLocale);
            }

            Status = "已移除“" + label + "”；点击“保存并应用”后删除本地记录。";
            StartPending();
        }
        public string GetPresetName(int slot)
        {
            return workspace.GetPresetNameForSlot(slot);
        }

        public string GetPresetLabel(int slot)
        {
            string name = GetPresetName(slot);
            return StringComparer.Ordinal.Equals(name, PresetWorkspace.GetPresetName(slot))
                ? name
                : slot.ToString("00", System.Globalization.CultureInfo.InvariantCulture) + " · " + name;
        }

        public void RenamePreset(int slot, string name)
        {
            workspace.RenamePreset(slot, name);
            Status = "已将“" + GetPresetLabel(slot) + "”重命名；保存并应用后写入工作区。";
            Changed?.Invoke();
        }

        public PresetStatistics GetPresetStatistics(int slot)
        {
            return workspace.GetPresetStatistics(slot);
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

                Status = "正在读取 " + code + " 的全部本地化表与已启用模组文本…";
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
                    Status = "现有预设工作区损坏，未覆盖它；请先修复 " + WorkspaceFile + "。";
                else if (workspace.RequiresWorkspaceMigration)
                    Status = "旧 active.json 已读取到预设01，但新工作区迁移未完成；旧文件仍保留，请重试保存。";
                else
                    Status = "已读取 " + State.Entries.Count + " 条文本；当前预设覆盖 " + State.OverrideCount + " 条。自建或硬编码文本不在这些数据库中。";
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
                        Report("预加载所需语言失败；本次保存未开始，草稿仍保留：" + error.Message, error);
                    }
                    else
                    {
                        Report("读取文本失败：" + error.Message, error);
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
                Status = "为保护已有数据，不能覆盖损坏的预设工作区；请先修复 " + WorkspaceFile + "。";
                Changed?.Invoke();
                return;
            }
            if (!runtimeConsistent)
            {
                Status = "运行时文本状态无法确认；请重启游戏后再保存并应用。";
                Changed?.Invoke();
                return;
            }
            if (pendingApply != null)
            {
                Status = "正在准备保存并应用，请等待当前操作完成。";
                Changed?.Invoke();
                return;
            }

            int targetSlot = workspace.SelectedPresetSlot;
            var locales = new HashSet<string>(StringComparer.Ordinal) { SelectedLocale };
            foreach (string locale in workspace.GetPresetLocales(targetSlot))
                locales.Add(locale);
            foreach (string locale in workspace.GetActiveLocales())
                locales.Add(locale);

            var apply = new PendingApply(
                targetSlot,
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

            Status = "正在加载所选预设与当前已应用预设涉及的全部语言，完成后再保存并应用…";
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

            if (workspace.SelectedPresetSlot != apply.TargetSlot ||
                !StringComparer.Ordinal.Equals(workspace.SerializeForSaveAndApply(), apply.WorkspaceJson) ||
                !AreEqual(workspace.ActiveOverrides, apply.PreviousOverrides))
            {
                pendingApply = null;
                Status = "预加载期间预设草稿发生变化；没有保存或应用，请提交后重新保存。";
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
                string message = "保存并应用未完成，草稿仍保留：" + error.Message;
                if (diskRollbackFailed)
                    message += " 磁盘工作区恢复失败，请从 presets.previous.json 恢复；后续保存已停用。";
                else if (!storageReadable)
                    message += " 工作区无法安全读取，后续保存已停用。";
                if (runtimeRollbackFailed)
                    message += " 运行时回滚失败，请重启游戏后再操作。";
                Report(message, error);
                return;
            }

            workspace.MarkSavedAndApplied();
            pendingApply = null;
            Status = "已保存并应用“" + GetPresetLabel(workspace.ActivePresetSlot) + "”：覆盖 " + workspace.State.OverrideCount + " 条。缓存文字可能需重开对应界面；未安装模组的条目会保留。";
            log.Log("SAVED preset=" + workspace.ActivePresetSlot + " overrides=" + workspace.State.OverrideCount + " applied=" + applied + " locale=" + SelectedLocale);
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
            Status = workspace.HasUnsavedChanges ? "有未保存修改；点击“保存并应用”后生效。" : "当前修改已保存。";
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

        public PresetImportPreview PreviewImport(string relativeFile, int targetSlot)
        {
            if (IsPrivateFile(relativeFile))
                throw new InvalidOperationException("请选择分享文件，不要选择编辑器内部存储。");
            if (!ModHelper.ModDataFileExists(ModEntry.ModId, relativeFile))
                throw new InvalidOperationException("文件已不存在，请刷新文件列表。");

            return workspace.PreviewImport(
                ModHelper.ReadModDataFileText(ModEntry.ModId, relativeFile),
                targetSlot,
                GameVersion);
        }

        public void ConfirmImport(PresetImportPreview preview, bool overwriteConflicts)
        {
            workspace.ConfirmImport(preview, overwriteConflicts);
            if (loadedLocales.Contains(SelectedLocale))
                State.SetCatalog(catalog.Entries(SelectedLocale));
            else
                Enqueue(SelectedLocale);

            Status = "修改文件已合并到“" + GetPresetLabel(workspace.SelectedPresetSlot) + "”草稿；检查后点击“保存并应用”。";
            StartPending();
        }

        public void Export(string fileName, int presetSlot)
        {
            string name = (fileName ?? string.Empty).Trim();
            if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 5);
            if (name.Length == 0 || name.Length > 80 || name == "." || name == ".." || name.IndexOfAny(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }) >= 0)
                throw new InvalidOperationException("文件名须为 1–80 个字符，不能包含路径或 Windows 文件名保留字符。");
            foreach (char character in name)
            {
                if (char.IsControl(character))
                    throw new InvalidOperationException("文件名不能包含控制字符。");
            }

            string relative = "exports/" + name + ".json";
            if (ModHelper.ModDataFileExists(ModEntry.ModId, relative))
                throw new InvalidOperationException("同名分享文件已存在，请换一个文件名以保留原文件。");

            string json = workspace.ExportPresetJson(presetSlot, GameVersion);
            ModHelper.WriteModDataFileText(ModEntry.ModId, relative, json);
            PresetStatistics stats = workspace.GetPresetStatistics(presetSlot);
            Status = "已导出“" + GetPresetLabel(presetSlot) + "”：修改 " + stats.OverrideCount + " 条、语言 " + stats.LanguageCount + " 种、来源 " + stats.SourceCount + " 个；" + DataFolder + "/" + relative;
            log.Log("EXPORTED preset=" + presetSlot + " file=" + relative + " overrides=" + stats.OverrideCount);
            Changed?.Invoke();
        }

        public IReadOnlyList<string> ValidateEdit(TextEntry entry, string text)
        {
            var warnings = new List<string>();
            if (text == null) { warnings.Add("替换文本不能为 null；空字符串可以用于清空文本。"); return warnings; }
            if (!Tokens(entry.OriginalText, Placeholder).SetEquals(Tokens(text, Placeholder)))
                warnings.Add("占位变量发生变化；这可能导致缺少游戏数值、名字或格式化错误。");
            if (!Tokens(entry.OriginalText, Tag).SetEquals(Tokens(text, Tag)))
                warnings.Add("富文本标签发生变化；请确认标签仍成对、拼写正确。");
            int depth = 0;
            bool bad = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (i + 1 < text.Length && (c == '{' || c == '}') && text[i + 1] == c) { i++; continue; }
                if (c == '{') depth++;
                if (c == '}' && --depth < 0) { bad = true; break; }
            }
            if (depth != 0 || bad) warnings.Add("花括号似乎不匹配；游戏格式化时可能无法显示此文本。");
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
                foreach (string locale in workspace.GetPresetLocales(preset.Slot))
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

        private void Report(string message, Exception error)
        {
            Status = message;
            log.Error(message + "\n" + error);
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
            public int TargetSlot { get; }
            public string WorkspaceJson { get; }
            public Dictionary<TextId, string> SelectedOverrides { get; }
            public Dictionary<TextId, string> PreviousOverrides { get; }
            public HashSet<string> Locales { get; }

            public PendingApply(
                int targetSlot,
                string workspaceJson,
                Dictionary<TextId, string> selectedOverrides,
                Dictionary<TextId, string> previousOverrides,
                HashSet<string> locales)
            {
                TargetSlot = targetSlot;
                WorkspaceJson = workspaceJson;
                SelectedOverrides = selectedOverrides;
                PreviousOverrides = previousOverrides;
                Locales = locales;
            }
        }
    }
}
