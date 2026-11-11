using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ProbablyEdited
{
    public sealed class PresetInfo
    {
        public int Slot { get; }
        public string Name { get; }
        public bool IsDirty { get; }
        public bool IsActive { get; }

        internal PresetInfo(int slot, string name, bool isDirty, bool isActive)
        {
            Slot = slot;
            Name = name;
            IsDirty = isDirty;
            IsActive = isActive;
        }
    }

    public sealed class PresetStatistics
    {
        public int OverrideCount { get; }
        public int LanguageCount { get; }
        public int SourceCount { get; }

        internal PresetStatistics(int overrideCount, int languageCount, int sourceCount)
        {
            OverrideCount = overrideCount;
            LanguageCount = languageCount;
            SourceCount = sourceCount;
        }
    }

    public sealed class PresetImportPreview
    {
        private readonly ReadOnlyCollection<string> _warnings;

        public int TargetSlot { get; }
        public string PresetName { get; }
        public bool CreatesPreset { get; }
        public PresetStatistics Incoming { get; }
        public PresetStatistics Existing { get; }
        public int ConflictCount { get; }
        public int UnavailableCount { get; }
        public IReadOnlyList<string> Warnings => _warnings;

        internal object WorkspaceMarker { get; }
        internal int SelectedSlotMarker { get; }
        internal EditorState SelectedState { get; }
        internal ImportPlan CatalogPlan { get; }
        internal EditorState TargetState { get; }
        internal ImportPlan TargetPlan { get; }

        internal PresetImportPreview(
            int targetSlot,
            bool createsPreset,
            string presetName,
            PresetStatistics incoming,
            PresetStatistics existing,
            int conflictCount,
            int unavailableCount,
            IList<string> warnings,
            object workspaceMarker,
            int selectedSlotMarker,
            EditorState selectedState,
            ImportPlan catalogPlan,
            EditorState targetState,
            ImportPlan targetPlan)
        {
            TargetSlot = targetSlot;
            CreatesPreset = createsPreset;
            Incoming = incoming;
            Existing = existing;
            ConflictCount = conflictCount;
            UnavailableCount = unavailableCount;
            PresetName = presetName;
            _warnings = new List<string>(warnings).AsReadOnly();
            WorkspaceMarker = workspaceMarker;
            SelectedSlotMarker = selectedSlotMarker;
            SelectedState = selectedState;
            CatalogPlan = catalogPlan;
            TargetState = targetState;
            TargetPlan = targetPlan;
        }
    }

    public sealed class PresetWorkspace
    {
        public const int MaxSlots = 36;
        private const string WorkspaceSchema = "probably-edited-workspace";

        private const int WorkspaceSchemaVersion = 2;
        private const int LegacyWorkspaceSchemaVersion = 1;
        private static readonly string[] WorkspaceProperties = { "schema", "schemaVersion", "activeSlot", "presets" };
        private static readonly string[] PresetPropertiesV1 = { "slot", "overrides" };
        private static readonly string[] PresetPropertiesV2 = { "slot", "name", "overrides" };
        private static readonly string[] OverrideProperties = { "sourceId", "locale", "tableId", "key", "text" };
        private static readonly PresetStatistics EmptyStatistics = new PresetStatistics(0, 0, 0);

        private readonly object _marker = new object();
        private readonly SortedDictionary<int, PresetSlot> _slots = new SortedDictionary<int, PresetSlot>();
        private readonly HashSet<int> _removedSavedSlots = new HashSet<int>();
        private IReadOnlyDictionary<TextId, string> _activeOverrides;
        private int _activeSlot = 1;
        private int _selectedSlot = 1;

        public int ActivePresetSlot => _activeSlot;
        public int SelectedPresetSlot => _selectedSlot;
        public EditorState State => _slots[_selectedSlot].State;
        public IReadOnlyDictionary<TextId, string> ActiveOverrides => _activeOverrides;
        public bool CanCreatePreset => _slots.Count < MaxSlots;
        public bool CanDeleteSelectedPreset => _slots.Count > 1 && _selectedSlot != _activeSlot;
        public bool RequiresWorkspaceMigration { get; private set; }

        public bool HasUnsavedChanges
        {
            get
            {
                if (RequiresWorkspaceMigration)
                    return true;

                if (_removedSavedSlots.Count > 0)
                    return true;

                foreach (PresetSlot preset in _slots.Values)
                {
                    if (preset.IsNew || preset.State.IsDirty || !StringComparer.Ordinal.Equals(preset.Name, preset.SavedName))
                        return true;
                }

                return false;
            }
        }

        public IReadOnlyList<PresetInfo> Presets
        {
            get
            {
                var result = new PresetInfo[_slots.Count];
                int index = 0;
                foreach (KeyValuePair<int, PresetSlot> pair in _slots)
                {
                    PresetSlot preset = pair.Value;
                    result[index++] = new PresetInfo(
                        pair.Key,
                        preset.Name,
                        preset.IsNew || preset.State.IsDirty || !StringComparer.Ordinal.Equals(preset.Name, preset.SavedName),
                        pair.Key == _activeSlot);
                }
                return Array.AsReadOnly(result);
            }
        }

        public PresetWorkspace()
        {
            _slots.Add(1, new PresetSlot(new EditorState(), GetPresetName(1), false));
            _activeOverrides = ReadOnlySnapshot(_slots[1].State.Overrides);
        }
        public static PresetWorkspace Load(string workspaceJson, string legacyActiveJson)
        {
            var workspace = new PresetWorkspace();
            if (workspaceJson != null)
            {
                workspace.LoadWorkspace(workspaceJson);
                return workspace;
            }

            if (legacyActiveJson != null)
            {
                if (string.IsNullOrWhiteSpace(legacyActiveJson))
                    throw new FormatException("active.json 已存在但内容为空；请从 previous.json 恢复，而不是覆盖它。");

                var legacyState = new EditorState();
                legacyState.LoadSaved(legacyActiveJson);
                workspace._slots[1] = new PresetSlot(legacyState, GetPresetName(1), false);
                workspace._activeOverrides = ReadOnlySnapshot(legacyState.Overrides);
                workspace.RequiresWorkspaceMigration = true;
            }

            return workspace;
        }

        public void SelectPreset(int slot)
        {
            ValidateSlot(slot);
            if (!_slots.ContainsKey(slot))
                throw new InvalidOperationException($"预设{slot:00}尚不存在。");

            _selectedSlot = slot;
        }
        public string GetPresetNameForSlot(int slot)
        {
            ValidateOccupiedSlot(slot);
            return _slots[slot].Name;
        }

        public void RenamePreset(int slot, string name)
        {
            ValidateOccupiedSlot(slot);
            if (name == null)
                throw new ArgumentNullException(nameof(name), "预设名称不能为空。");

            string normalized = name.Trim();
            if (normalized.Length == 0)
                throw new ArgumentException("预设名称不能为空。", nameof(name));

            _slots[slot].Name = normalized;
        }

        public int CreatePreset()
        {
            if (!CanCreatePreset)
                throw new InvalidOperationException($"最多只能创建 {MaxSlots} 个预设。");

            int slot = 1;
            while (_slots.ContainsKey(slot))
                slot++;

            var state = new EditorState();
            state.SetCatalog(State.Entries);
            _slots.Add(slot, new PresetSlot(state, GetPresetName(slot), true));
            _selectedSlot = slot;
            return slot;
        }
        public void DeletePreset(int slot)
        {
            ValidateOccupiedSlot(slot);
            if (_slots.Count == 1)
            {
                throw new InvalidOperationException(
                    "至少保留一个预设。当前应用的预设不能删除；请先保存并应用另一个预设后再删除。");
            }

            if (slot == _activeSlot)
                throw new InvalidOperationException("当前应用的预设不能删除；请先保存并应用另一个预设后再删除。");

            PresetSlot preset = _slots[slot];
            if (!preset.IsNew)
                _removedSavedSlots.Add(slot);

            _slots.Remove(slot);
            if (_selectedSlot == slot)
                _selectedSlot = _activeSlot;
        }


        public PresetStatistics GetPresetStatistics(int slot)
        {
            ValidateSlot(slot);
            return _slots.TryGetValue(slot, out PresetSlot preset)
                ? GetStatistics(preset.State.Overrides)
                : EmptyStatistics;
        }

        public IReadOnlyList<string> GetPresetLocales(int slot)
        {
            ValidateSlot(slot);
            if (!_slots.TryGetValue(slot, out PresetSlot preset))
                return Array.AsReadOnly(Array.Empty<string>());

            return GetLocales(preset.State.Overrides);
        }

        public IReadOnlyList<string> GetActiveLocales()
        {
            return GetLocales(_activeOverrides);
        }

        public string SerializeWorkspace()
        {
            return SerializeWorkspace(_activeSlot);
        }

        public string SerializeForSaveAndApply()
        {
            return SerializeWorkspace(_selectedSlot);
        }

        public void MarkWorkspacePersisted()
        {
            RequiresWorkspaceMigration = false;
        }

        public void MarkSavedAndApplied()
        {
            _activeSlot = _selectedSlot;
            foreach (PresetSlot preset in _slots.Values)
            {
                preset.State.MarkSaved();
                preset.SavedName = preset.Name;
                preset.IsNew = false;
            }

            _activeOverrides = ReadOnlySnapshot(State.Overrides);
            _removedSavedSlots.Clear();
            RequiresWorkspaceMigration = false;
        }

        public string ExportPresetJson(int slot, string gameVersion)
        {
            ValidateOccupiedSlot(slot);
            return _slots[slot].State.ExportPresetJson(gameVersion, _slots[slot].Name);
        }

        public PresetImportPreview PreviewImport(string json, int targetSlot, string gameVersion)
        {
            ValidateSlot(targetSlot);

            EditorState.PatchDocument document = EditorState.ParseSharedPatch(json);
            EditorState selectedState = State;
            ImportPlan catalogPlan = selectedState.PreviewImport(document, gameVersion);
            bool createsPreset = !_slots.TryGetValue(targetSlot, out PresetSlot existingPreset);

            if (createsPreset && !CanCreatePreset)
                throw new InvalidOperationException($"最多只能导入到 {MaxSlots} 个预设槽位。");

            EditorState targetState;
            if (createsPreset)
            {
                targetState = new EditorState();
                targetState.SetCatalog(selectedState.Entries);
            }
            else
            {
                targetState = existingPreset.State;
            }

            ImportPlan targetPlan = ReferenceEquals(targetState, selectedState)
                ? catalogPlan
                : targetState.PreviewImport(document, gameVersion);
            PresetStatistics incoming = GetStatistics(document.Overrides);
            PresetStatistics existing = createsPreset ? EmptyStatistics : GetStatistics(targetState.Overrides);
            string presetName = createsPreset
                ? (document.PresetName == null ? GetPresetName(targetSlot) : document.PresetName.Trim())
                : existingPreset.Name;
            var warnings = new List<string>();

            if (gameVersion == null)
                warnings.Add("当前游戏版本未知，无法检查补丁版本兼容性。");
            else if (!StringComparer.Ordinal.Equals(document.GameVersion, gameVersion))
                warnings.Add($"补丁版本 {document.GameVersion} 与当前游戏版本 {gameVersion} 不同；请核对后再应用。");

            string knownLocale = selectedState.CatalogLocale;
            if (knownLocale == null)
                warnings.Add("尚无已加载的语言目录；不可用项计为 0，未判断任何键是否缺失。");
            else
                warnings.Add($"不可用项仅按当前已加载的 {knownLocale} 语言目录统计；未加载语言不计入。");

            if (catalogPlan.UnavailableCount > 0)
                warnings.Add($"当前已加载语言目录中有 {catalogPlan.UnavailableCount} 项暂不可用；这些覆盖仍会保留，不会按缺失项删除。");
            if (targetPlan.ConflictCount > 0)
                warnings.Add($"有 {targetPlan.ConflictCount} 项与目标预设不同；不选择覆盖冲突时将保留目标值。");

            return new PresetImportPreview(
                targetSlot,
                createsPreset,
                presetName,
                incoming,
                existing,
                targetPlan.ConflictCount,
                catalogPlan.UnavailableCount,
                warnings,
                _marker,
                _selectedSlot,
                selectedState,
                catalogPlan,
                targetState,
                targetPlan);
        }

        public void ConfirmImport(PresetImportPreview preview, bool overwriteConflicts)
        {
            if (preview == null)
                throw new ArgumentNullException(nameof(preview), "导入预览不能为空；请先预览文件。");
            if (!ReferenceEquals(preview.WorkspaceMarker, _marker))
                throw new InvalidOperationException("此导入预览不属于当前工作区，请重新预览文件。");
            if (_selectedSlot != preview.SelectedSlotMarker || !ReferenceEquals(State, preview.SelectedState))
                throw new InvalidOperationException("预览后当前预设已变化，请重新预览文件。");

            preview.SelectedState.ValidateImportPlan(preview.CatalogPlan);
            if (preview.CreatesPreset)
            {
                if (_slots.ContainsKey(preview.TargetSlot) || !CanCreatePreset)
                    throw new InvalidOperationException("预览后目标槽位已变化或容量已满，请重新预览文件。");
            }
            else if (!_slots.TryGetValue(preview.TargetSlot, out PresetSlot currentPreset) ||
                     !ReferenceEquals(currentPreset.State, preview.TargetState))
            {
                throw new InvalidOperationException("预览后目标预设已变化，请重新预览文件。");
            }

            preview.TargetState.ValidateImportPlan(preview.TargetPlan);
            preview.TargetState.ApplyImport(preview.TargetPlan, overwriteConflicts);
            if (preview.CreatesPreset)
                _slots.Add(preview.TargetSlot, new PresetSlot(preview.TargetState, preview.PresetName, true));

            _selectedSlot = preview.TargetSlot;
        }

        public static string GetPresetName(int slot)
        {
            ValidateSlot(slot);
            return "预设" + slot.ToString("00", CultureInfo.InvariantCulture);
        }

        private void LoadWorkspace(string json)
        {
            WorkspaceDocument document = ParseWorkspace(json);
            var loadedSlots = new SortedDictionary<int, PresetSlot>();
            foreach (KeyValuePair<int, WorkspacePreset> pair in document.Presets)
            {
                var state = new EditorState();
                state.LoadOverrides(pair.Value.Overrides);
                loadedSlots.Add(pair.Key, new PresetSlot(state, pair.Value.Name, false));
            }

            _slots.Clear();
            foreach (KeyValuePair<int, PresetSlot> pair in loadedSlots)
                _slots.Add(pair.Key, pair.Value);

            _activeSlot = document.ActiveSlot;
            _selectedSlot = document.ActiveSlot;
            _activeOverrides = ReadOnlySnapshot(_slots[_activeSlot].State.Overrides);
            RequiresWorkspaceMigration = false;
        }

        private string SerializeWorkspace(int activeSlot)
        {
            ValidateOccupiedSlot(activeSlot);
            var presets = new JArray();
            foreach (KeyValuePair<int, PresetSlot> pair in _slots)
            {
                presets.Add(new JObject(
                    new JProperty("slot", pair.Key),
                    new JProperty("name", pair.Value.Name),
                    new JProperty("overrides", SerializeOverrides(pair.Value.State.Overrides))));
            }

            var root = new JObject(
                new JProperty("schema", WorkspaceSchema),
                new JProperty("schemaVersion", WorkspaceSchemaVersion),
                new JProperty("activeSlot", activeSlot),
                new JProperty("presets", presets));
            return root.ToString(Formatting.Indented);
        }

        private static JArray SerializeOverrides(IReadOnlyDictionary<TextId, string> overrides)
        {
            var ordered = new List<KeyValuePair<TextId, string>>(overrides);
            ordered.Sort(CompareOverrides);
            var result = new JArray();

            foreach (KeyValuePair<TextId, string> pair in ordered)
            {
                result.Add(new JObject(
                    new JProperty("sourceId", pair.Key.SourceId),
                    new JProperty("locale", pair.Key.Locale),
                    new JProperty("tableId", pair.Key.TableId),
                    new JProperty("key", pair.Key.Key),
                    new JProperty("text", pair.Value)));
            }

            return result;
        }

        private static WorkspaceDocument ParseWorkspace(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new FormatException("presets.json 已存在但内容为空；请从 presets.previous.json 恢复，而不是覆盖它。");

            JToken token = ReadJson(json, "工作区");
            if (!(token is JObject root))
                throw new FormatException("工作区根节点必须是 JSON 对象。");

            RequireExactProperties(root, WorkspaceProperties, "工作区");
            string schema = RequireString(root, "schema", "工作区", false);
            if (!StringComparer.Ordinal.Equals(schema, WorkspaceSchema))
                throw new NotSupportedException($"不支持工作区格式“{schema}”。");

            int version = RequireInt(root["schemaVersion"], "工作区字段 schemaVersion");
            if (version != LegacyWorkspaceSchemaVersion && version != WorkspaceSchemaVersion)
                throw new NotSupportedException($"不支持工作区版本 {version}；当前支持 schemaVersion {LegacyWorkspaceSchemaVersion} 和 {WorkspaceSchemaVersion}。");

            int activeSlot = RequireInt(root["activeSlot"], "工作区字段 activeSlot");
            ValidateSlot(activeSlot);
            if (!(root["presets"] is JArray presetArray))
                throw new FormatException("工作区字段 presets 必须是数组。");
            if (presetArray.Count == 0 || presetArray.Count > MaxSlots)
                throw new FormatException($"工作区必须包含 1–{MaxSlots} 个预设。");

            var presets = new SortedDictionary<int, WorkspacePreset>();
            int index = 0;
            foreach (JToken item in presetArray)
            {
                index++;
                if (!(item is JObject presetObject))
                    throw new FormatException($"工作区 presets 第 {index} 项必须是 JSON 对象。");

                RequireExactProperties(presetObject,
                    version == LegacyWorkspaceSchemaVersion ? PresetPropertiesV1 : PresetPropertiesV2,
                    $"工作区 presets 第 {index} 项");
                int slot = RequireInt(presetObject["slot"], $"工作区 presets 第 {index} 项 slot");
                ValidateSlot(slot);
                if (presets.ContainsKey(slot))
                    throw new FormatException($"工作区预设槽位 {slot} 重复。");

                string name = version == LegacyWorkspaceSchemaVersion
                    ? GetPresetName(slot)
                    : RequireString(presetObject, "name", $"工作区 presets 第 {index} 项", false);
                Dictionary<TextId, string> overrides = ParseOverrides(
                    presetObject["overrides"], $"工作区 presets 第 {index} 项 overrides");
                presets.Add(slot, new WorkspacePreset(name, overrides));
            }

            if (!presets.ContainsKey(activeSlot))
                throw new FormatException("工作区 activeSlot 必须指向一个已存在的预设。");

            return new WorkspaceDocument(activeSlot, presets);
        }

        private static Dictionary<TextId, string> ParseOverrides(JToken token, string context)
        {
            if (!(token is JArray items))
                throw new FormatException($"{context} 必须是数组。");

            var overrides = new Dictionary<TextId, string>();
            int index = 0;
            foreach (JToken item in items)
            {
                index++;
                if (!(item is JObject value))
                    throw new FormatException($"{context} 第 {index} 项必须是 JSON 对象。");

                RequireExactProperties(value, OverrideProperties, $"{context} 第 {index} 项");
                string sourceId = RequireString(value, "sourceId", $"{context} 第 {index} 项", false);
                string locale = RequireString(value, "locale", $"{context} 第 {index} 项", false);
                string tableId = RequireString(value, "tableId", $"{context} 第 {index} 项", false);
                string key = RequireString(value, "key", $"{context} 第 {index} 项", false);
                string text = RequireString(value, "text", $"{context} 第 {index} 项", true);
                var id = new TextId(sourceId, locale, tableId, key);
                if (overrides.ContainsKey(id))
                    throw new FormatException($"{context} 第 {index} 项的文本标识重复。");

                overrides.Add(id, text);
            }

            return overrides;
        }

        private static JToken ReadJson(string json, string context)
        {
            try
            {
                using (var input = new StringReader(json))
                using (var reader = new JsonTextReader(input))
                {
                    reader.DateParseHandling = DateParseHandling.None;
                    reader.SupportMultipleContent = true;
                    if (!reader.Read())
                        throw new FormatException(context + " JSON 内容为空。");
                    if (reader.TokenType == JsonToken.Comment)
                        throw new FormatException(context + "必须是标准 JSON，不能包含注释。");

                    JToken token = JToken.ReadFrom(reader, new JsonLoadSettings
                    {
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                        CommentHandling = CommentHandling.Load
                    });
                    if (reader.Read())
                        throw new FormatException(context + " JSON 后存在多余内容。");
                    if (ContainsComment(token))
                        throw new FormatException(context + "必须是标准 JSON，不能包含注释。");
                    return token;
                }
            }
            catch (JsonException exception)
            {
                throw new FormatException(context + " JSON 格式无效；请检查语法或重复字段。详情：" + exception.Message, exception);
            }
        }

        private static void RequireExactProperties(JObject value, string[] expected, string context)
        {
            foreach (JProperty property in value.Properties())
            {
                bool found = false;
                for (int index = 0; index < expected.Length; index++)
                {
                    if (StringComparer.Ordinal.Equals(property.Name, expected[index]))
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                    throw new FormatException($"{context}包含不支持的字段“{property.Name}”。");
            }

            for (int index = 0; index < expected.Length; index++)
            {
                if (value.Property(expected[index]) == null)
                    throw new FormatException($"{context}缺少必需字段“{expected[index]}”。");
            }
        }

        private static string RequireString(JObject value, string propertyName, string context, bool allowEmpty)
        {
            JToken token = value[propertyName];
            if (token == null || token.Type != JTokenType.String)
                throw new FormatException($"{context}字段“{propertyName}”必须是 JSON 字符串，不能为 null。");

            string text = token.Value<string>();
            if (text == null || (!allowEmpty && string.IsNullOrWhiteSpace(text)))
                throw new FormatException($"{context}字段“{propertyName}”不能为空。");

            return text;
        }

        private static int RequireInt(JToken token, string context)
        {
            if (token == null || token.Type != JTokenType.Integer)
                throw new FormatException(context + "必须是整数。");

            try
            {
                return checked(Convert.ToInt32(((JValue)token).Value, CultureInfo.InvariantCulture));
            }
            catch (Exception exception) when (exception is FormatException || exception is OverflowException || exception is InvalidCastException)
            {
                throw new FormatException(context + "超出有效整数范围。", exception);
            }
        }

        private static bool ContainsComment(JToken token)
        {
            if (token.Type == JTokenType.Comment)
                return true;

            foreach (JToken child in token.Children())
            {
                if (ContainsComment(child))
                    return true;
            }

            return false;
        }

        private static PresetStatistics GetStatistics(IReadOnlyDictionary<TextId, string> overrides)
        {
            if (overrides.Count == 0)
                return EmptyStatistics;

            var locales = new HashSet<string>(StringComparer.Ordinal);
            var sources = new HashSet<string>(StringComparer.Ordinal);
            foreach (TextId id in overrides.Keys)
            {
                locales.Add(id.Locale);
                sources.Add(id.SourceId);
            }

            return new PresetStatistics(overrides.Count, locales.Count, sources.Count);
        }

        private static IReadOnlyList<string> GetLocales(IReadOnlyDictionary<TextId, string> overrides)
        {
            if (overrides.Count == 0)
                return Array.AsReadOnly(Array.Empty<string>());

            var locales = new SortedSet<string>(StringComparer.Ordinal);
            foreach (TextId id in overrides.Keys)
                locales.Add(id.Locale);

            var result = new string[locales.Count];
            locales.CopyTo(result);
            return Array.AsReadOnly(result);
        }

        private static IReadOnlyDictionary<TextId, string> ReadOnlySnapshot(IReadOnlyDictionary<TextId, string> source)
        {
            var copy = new Dictionary<TextId, string>();
            foreach (KeyValuePair<TextId, string> pair in source)
                copy.Add(pair.Key, pair.Value);
            return new ReadOnlyDictionary<TextId, string>(copy);
        }

        private static int CompareOverrides(
            KeyValuePair<TextId, string> left,
            KeyValuePair<TextId, string> right)
        {
            int order = StringComparer.Ordinal.Compare(left.Key.SourceId, right.Key.SourceId);
            if (order != 0)
                return order;

            order = StringComparer.Ordinal.Compare(left.Key.Locale, right.Key.Locale);
            if (order != 0)
                return order;

            order = StringComparer.Ordinal.Compare(left.Key.TableId, right.Key.TableId);
            return order != 0 ? order : StringComparer.Ordinal.Compare(left.Key.Key, right.Key.Key);
        }

        private static void ValidateSlot(int slot)
        {
            if (slot < 1 || slot > MaxSlots)
                throw new ArgumentOutOfRangeException(nameof(slot), slot, $"预设槽位必须在 1–{MaxSlots} 之间。");
        }

        private void ValidateOccupiedSlot(int slot)
        {
            ValidateSlot(slot);
            if (!_slots.ContainsKey(slot))
                throw new InvalidOperationException($"预设{slot:00}尚不存在。");
        }

        private sealed class PresetSlot
        {
            public EditorState State { get; }
            public string Name { get; set; }
            public string SavedName { get; set; }
            public bool IsNew { get; set; }

            public PresetSlot(EditorState state, string name, bool isNew)
            {
                State = state;
                Name = name;
                SavedName = name;
                IsNew = isNew;
            }
        }

        private sealed class WorkspacePreset
        {
            public string Name { get; }
            public Dictionary<TextId, string> Overrides { get; }

            public WorkspacePreset(string name, Dictionary<TextId, string> overrides)
            {
                Name = name;
                Overrides = overrides;
            }
        }

        private sealed class WorkspaceDocument
        {
            public int ActiveSlot { get; }
            public SortedDictionary<int, WorkspacePreset> Presets { get; }

            public WorkspaceDocument(int activeSlot, SortedDictionary<int, WorkspacePreset> presets)
            {
                ActiveSlot = activeSlot;
                Presets = presets;
            }
        }
    }
}
