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
        public string Id { get; }
        public string Name { get; }
        public bool IsDirty { get; }
        public bool IsActive { get; }

        internal PresetInfo(string id, string name, bool isDirty, bool isActive)
        {
            Id = id;
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

        public string TargetPresetId { get; }
        public string PresetName { get; }
        public bool CreatesPreset { get; }
        public PresetStatistics Incoming { get; }
        public PresetStatistics Existing { get; }
        public int ConflictCount { get; }
        public int UnavailableCount { get; }
        public IReadOnlyList<string> Warnings => _warnings;

        internal object WorkspaceMarker { get; }
        internal string SelectedPresetIdMarker { get; }
        internal EditorState SelectedState { get; }
        internal ImportPlan CatalogPlan { get; }
        internal EditorState TargetState { get; }
        internal ImportPlan TargetPlan { get; }

        internal PresetImportPreview(
            string targetPresetId,
            bool createsPreset,
            string presetName,
            PresetStatistics incoming,
            PresetStatistics existing,
            int conflictCount,
            int unavailableCount,
            IList<string> warnings,
            object workspaceMarker,
            string selectedPresetIdMarker,
            EditorState selectedState,
            ImportPlan catalogPlan,
            EditorState targetState,
            ImportPlan targetPlan)
        {
            TargetPresetId = targetPresetId;
            CreatesPreset = createsPreset;
            PresetName = presetName;
            Incoming = incoming;
            Existing = existing;
            ConflictCount = conflictCount;
            UnavailableCount = unavailableCount;
            _warnings = new ReadOnlyCollection<string>(new List<string>(warnings));
            WorkspaceMarker = workspaceMarker;
            SelectedPresetIdMarker = selectedPresetIdMarker;
            SelectedState = selectedState;
            CatalogPlan = catalogPlan;
            TargetState = targetState;
            TargetPlan = targetPlan;
        }
    }

    public sealed class PresetWorkspace
    {
        public const int MaxPresets = 36;
        private const string WorkspaceSchema = "probably-edited-workspace";
        private const int WorkspaceSchemaVersion = 3;
        private const int LegacyWorkspaceSchemaVersion = 1;
        private const int NamedWorkspaceSchemaVersion = 2;
        private static readonly string[] WorkspacePropertiesV1V2 = { "schema", "schemaVersion", "activeSlot", "presets" };
        private static readonly string[] WorkspacePropertiesV3 = { "schema", "schemaVersion", "activePresetId", "presets" };
        private static readonly string[] PresetPropertiesV1 = { "slot", "overrides" };
        private static readonly string[] PresetPropertiesV2 = { "slot", "name", "overrides" };
        private static readonly string[] PresetPropertiesV3 = { "id", "name", "overrides" };
        private static readonly string[] OverrideProperties = { "sourceId", "locale", "tableId", "key", "text" };
        private static readonly PresetStatistics EmptyStatistics = new PresetStatistics(0, 0, 0);

        private readonly object _marker = new object();
        private readonly List<PresetRecord> _presets = new List<PresetRecord>();
        private readonly Dictionary<string, PresetRecord> _presetsById = new Dictionary<string, PresetRecord>(StringComparer.Ordinal);
        private readonly HashSet<string> _removedSavedPresetIds = new HashSet<string>(StringComparer.Ordinal);
        private IReadOnlyDictionary<TextId, string> _activeOverrides;
        private string _activePresetId;
        private string _selectedPresetId;

        public string ActivePresetId => _activePresetId;
        public string SelectedPresetId => _selectedPresetId;
        public EditorState State => _presetsById[_selectedPresetId].State;
        public IReadOnlyDictionary<TextId, string> ActiveOverrides => _activeOverrides;
        public bool CanCreatePreset => _presets.Count < MaxPresets;
        public bool CanDeleteSelectedPreset => _presets.Count > 1 && !StringComparer.Ordinal.Equals(_selectedPresetId, _activePresetId);
        public bool RequiresWorkspaceMigration { get; private set; }

        public bool HasUnsavedChanges
        {
            get
            {
                if (RequiresWorkspaceMigration || _removedSavedPresetIds.Count > 0)
                    return true;

                foreach (PresetRecord preset in _presets)
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
                var result = new PresetInfo[_presets.Count];
                for (int index = 0; index < _presets.Count; index++)
                {
                    PresetRecord preset = _presets[index];
                    result[index] = new PresetInfo(
                        preset.Id,
                        preset.Name,
                        preset.IsNew || preset.State.IsDirty || !StringComparer.Ordinal.Equals(preset.Name, preset.SavedName),
                        StringComparer.Ordinal.Equals(preset.Id, _activePresetId));
                }
                return Array.AsReadOnly(result);
            }
        }

        public PresetWorkspace()
        {
            PresetRecord initial = new PresetRecord(CreatePresetId(), new EditorState(), GetDefaultPresetName(1), false);
            AddPreset(initial);
            _activePresetId = initial.Id;
            _selectedPresetId = initial.Id;
            _activeOverrides = ReadOnlySnapshot(initial.State.Overrides);
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
                PresetRecord initial = new PresetRecord(workspace._selectedPresetId, legacyState, GetDefaultPresetName(1), false);
                workspace._presets[0] = initial;
                workspace._presetsById[initial.Id] = initial;
                workspace._activeOverrides = ReadOnlySnapshot(legacyState.Overrides);
                workspace.RequiresWorkspaceMigration = true;
            }

            return workspace;
        }

        public void SelectPreset(string presetId)
        {
            GetPreset(presetId);
            _selectedPresetId = presetId;
        }

        public string GetPresetName(string presetId)
        {
            return GetPreset(presetId).Name;
        }

        public void RenamePreset(string presetId, string name)
        {
            PresetRecord preset = GetPreset(presetId);
            if (name == null)
                throw new ArgumentNullException(nameof(name), "预设名称不能为空。");

            string normalized = name.Trim();
            if (normalized.Length == 0)
                throw new ArgumentException("预设名称不能为空。", nameof(name));

            preset.Name = normalized;
        }

        public string CreatePreset()
        {
            if (!CanCreatePreset)
                throw new InvalidOperationException($"最多只能创建 {MaxPresets} 个预设。");

            var state = new EditorState();
            state.SetCatalog(State.Entries);
            string presetId = CreatePresetId();
            var preset = new PresetRecord(presetId, state, GetNextPresetName(), true);
            AddPreset(preset);
            _selectedPresetId = presetId;
            return presetId;
        }

        public void DeletePreset(string presetId)
        {
            PresetRecord preset = GetPreset(presetId);
            if (_presets.Count == 1)
            {
                throw new InvalidOperationException(
                    "至少保留一个预设。当前应用的预设不能删除；请先保存并应用另一个预设后再删除。");
            }

            if (StringComparer.Ordinal.Equals(presetId, _activePresetId))
                throw new InvalidOperationException("当前应用的预设不能删除；请先保存并应用另一个预设后再删除。");

            if (!preset.IsNew)
                _removedSavedPresetIds.Add(presetId);

            _presets.Remove(preset);
            _presetsById.Remove(presetId);
            if (StringComparer.Ordinal.Equals(_selectedPresetId, presetId))
                _selectedPresetId = _activePresetId;
        }

        public PresetStatistics GetPresetStatistics(string presetId)
        {
            return GetStatistics(GetPreset(presetId).State.Overrides);
        }

        public IReadOnlyList<string> GetPresetLocales(string presetId)
        {
            return GetLocales(GetPreset(presetId).State.Overrides);
        }

        public IReadOnlyList<string> GetActiveLocales()
        {
            return GetLocales(_activeOverrides);
        }

        public string SerializeWorkspace()
        {
            return SerializeWorkspace(_activePresetId);
        }

        public string SerializeForSaveAndApply()
        {
            return SerializeWorkspace(_selectedPresetId);
        }

        public void MarkSavedAndApplied()
        {
            _activePresetId = _selectedPresetId;
            foreach (PresetRecord preset in _presets)
            {
                preset.State.MarkSaved();
                preset.SavedName = preset.Name;
                preset.IsNew = false;
            }

            _activeOverrides = ReadOnlySnapshot(State.Overrides);
            _removedSavedPresetIds.Clear();
            RequiresWorkspaceMigration = false;
        }

        public string ExportPresetJson(string presetId, string gameVersion)
        {
            PresetRecord preset = GetPreset(presetId);
            return preset.State.ExportPresetJson(gameVersion, preset.Name);
        }

        public PresetImportPreview PreviewImport(string json, string targetPresetId, string gameVersion)
        {
            bool createsPreset = targetPresetId == null;
            PresetRecord existingPreset = createsPreset ? null : GetPreset(targetPresetId);
            if (createsPreset && !CanCreatePreset)
                throw new InvalidOperationException($"最多只能导入到 {MaxPresets} 个预设。");

            EditorState.PatchDocument document = EditorState.ParseSharedPatch(json);
            EditorState selectedState = State;
            ImportPlan catalogPlan = selectedState.PreviewImport(document, gameVersion);
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
                ? (document.PresetName == null ? GetNextPresetName() : document.PresetName.Trim())
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
                targetPresetId,
                createsPreset,
                presetName,
                incoming,
                existing,
                targetPlan.ConflictCount,
                catalogPlan.UnavailableCount,
                warnings,
                _marker,
                _selectedPresetId,
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
            if (!StringComparer.Ordinal.Equals(_selectedPresetId, preview.SelectedPresetIdMarker) ||
                !ReferenceEquals(State, preview.SelectedState))
            {
                throw new InvalidOperationException("预览后当前预设已变化，请重新预览文件。");
            }

            preview.SelectedState.ValidateImportPlan(preview.CatalogPlan);
            if (preview.CreatesPreset)
            {
                if (preview.TargetPresetId != null || !CanCreatePreset)
                    throw new InvalidOperationException("预览后目标预设已变化或容量已满，请重新预览文件。");
            }
            else if (preview.TargetPresetId == null ||
                     !_presetsById.TryGetValue(preview.TargetPresetId, out PresetRecord currentPreset) ||
                     !ReferenceEquals(currentPreset.State, preview.TargetState))
            {
                throw new InvalidOperationException("预览后目标预设已变化，请重新预览文件。");
            }

            preview.TargetState.ValidateImportPlan(preview.TargetPlan);
            preview.TargetState.ApplyImport(preview.TargetPlan, overwriteConflicts);
            if (preview.CreatesPreset)
            {
                string createdPresetId = CreatePresetId();
                AddPreset(new PresetRecord(createdPresetId, preview.TargetState, preview.PresetName, true));
                _selectedPresetId = createdPresetId;
            }
            else
            {
                _selectedPresetId = preview.TargetPresetId;
            }
        }

        private void LoadWorkspace(string json)
        {
            WorkspaceDocument document = ParseWorkspace(json);
            _presets.Clear();
            _presetsById.Clear();
            foreach (WorkspacePreset workspacePreset in document.Presets)
            {
                var state = new EditorState();
                state.LoadOverrides(workspacePreset.Overrides);
                AddPreset(new PresetRecord(workspacePreset.Id, state, workspacePreset.Name, false));
            }

            _activePresetId = document.ActivePresetId;
            _selectedPresetId = document.ActivePresetId;
            _activeOverrides = ReadOnlySnapshot(_presetsById[_activePresetId].State.Overrides);
            RequiresWorkspaceMigration = document.RequiresMigration;
        }

        private string SerializeWorkspace(string activePresetId)
        {
            GetPreset(activePresetId);
            var presets = new JArray();
            foreach (PresetRecord preset in _presets)
            {
                presets.Add(new JObject(
                    new JProperty("id", preset.Id),
                    new JProperty("name", preset.Name),
                    new JProperty("overrides", SerializeOverrides(preset.State.Overrides))));
            }

            var root = new JObject(
                new JProperty("schema", WorkspaceSchema),
                new JProperty("schemaVersion", WorkspaceSchemaVersion),
                new JProperty("activePresetId", activePresetId),
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

            int version = RequireInt(root["schemaVersion"], "工作区字段 schemaVersion");
            if (version != LegacyWorkspaceSchemaVersion && version != NamedWorkspaceSchemaVersion && version != WorkspaceSchemaVersion)
                throw new NotSupportedException($"不支持工作区版本 {version}；当前支持 schemaVersion 1、2 和 {WorkspaceSchemaVersion}。");

            RequireExactProperties(root, version == WorkspaceSchemaVersion ? WorkspacePropertiesV3 : WorkspacePropertiesV1V2, "工作区");
            string schema = RequireString(root, "schema", "工作区", false);
            if (!StringComparer.Ordinal.Equals(schema, WorkspaceSchema))
                throw new NotSupportedException($"不支持工作区格式“{schema}”。");

            bool legacy = version < WorkspaceSchemaVersion;
            int activeSlot = 0;
            string activePresetId = null;
            if (legacy)
            {
                activeSlot = RequireInt(root["activeSlot"], "工作区字段 activeSlot");
                ValidateLegacySlot(activeSlot);
            }
            else
            {
                activePresetId = RequireString(root, "activePresetId", "工作区", false);
            }

            if (!(root["presets"] is JArray presetArray))
                throw new FormatException("工作区字段 presets 必须是数组。");
            if (presetArray.Count == 0 || presetArray.Count > MaxPresets)
                throw new FormatException($"工作区必须包含 1–{MaxPresets} 个预设。");

            var presets = new List<WorkspacePreset>(presetArray.Count);
            var legacyPresets = legacy ? new SortedDictionary<int, WorkspacePreset>() : null;
            var presetIds = new HashSet<string>(StringComparer.Ordinal);
            int index = 0;
            foreach (JToken item in presetArray)
            {
                index++;
                if (!(item is JObject presetObject))
                    throw new FormatException($"工作区 presets 第 {index} 项必须是 JSON 对象。");

                RequireExactProperties(presetObject,
                    version == LegacyWorkspaceSchemaVersion ? PresetPropertiesV1 :
                    version == NamedWorkspaceSchemaVersion ? PresetPropertiesV2 : PresetPropertiesV3,
                    $"工作区 presets 第 {index} 项");

                string id;
                string name;
                int legacySlot = 0;
                if (legacy)
                {
                    legacySlot = RequireInt(presetObject["slot"], $"工作区 presets 第 {index} 项 slot");
                    ValidateLegacySlot(legacySlot);
                    id = CreatePresetId();
                    name = version == LegacyWorkspaceSchemaVersion
                        ? GetDefaultPresetName(legacySlot)
                        : RequireString(presetObject, "name", $"工作区 presets 第 {index} 项", false);
                }
                else
                {
                    id = RequireString(presetObject, "id", $"工作区 presets 第 {index} 项", false);
                    name = RequireString(presetObject, "name", $"工作区 presets 第 {index} 项", false);
                }

                if (!presetIds.Add(id))
                    throw new FormatException($"工作区预设 ID “{id}”重复。");

                Dictionary<TextId, string> overrides = ParseOverrides(
                    presetObject["overrides"], $"工作区 presets 第 {index} 项 overrides");
                var preset = new WorkspacePreset(id, name, overrides);
                if (legacy)
                {
                    if (legacyPresets.ContainsKey(legacySlot))
                        throw new FormatException($"工作区预设序号 {legacySlot} 重复。");
                    legacyPresets.Add(legacySlot, preset);
                    if (legacySlot == activeSlot)
                        activePresetId = id;
                }
                else
                {
                    presets.Add(preset);
                }
            }

            if (legacy)
            {
                foreach (WorkspacePreset preset in legacyPresets.Values)
                    presets.Add(preset);
            }

            if (activePresetId == null || !presetIds.Contains(activePresetId))
                throw new FormatException(legacy
                    ? "工作区 activeSlot 必须指向一个已存在的预设。"
                    : "工作区 activePresetId 必须指向一个已存在的预设。");

            return new WorkspaceDocument(activePresetId, presets, legacy);
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

        private static string CreatePresetId()
        {
            return Guid.NewGuid().ToString("N");
        }

        private static string GetDefaultPresetName(int number)
        {
            return "预设" + number.ToString("00", CultureInfo.InvariantCulture);
        }

        private string GetNextPresetName()
        {
            int number = _presets.Count + 1;
            while (ContainsPresetName(GetDefaultPresetName(number)))
                number++;
            return GetDefaultPresetName(number);
        }

        private bool ContainsPresetName(string name)
        {
            foreach (PresetRecord preset in _presets)
            {
                if (StringComparer.Ordinal.Equals(preset.Name, name))
                    return true;
            }

            return false;
        }

        private void AddPreset(PresetRecord preset)
        {
            if (_presetsById.ContainsKey(preset.Id))
                throw new InvalidOperationException("预设 ID 冲突；未修改工作区。");

            _presets.Add(preset);
            _presetsById.Add(preset.Id, preset);
        }

        private PresetRecord GetPreset(string presetId)
        {
            if (string.IsNullOrWhiteSpace(presetId))
                throw new ArgumentException("预设 ID 不能为空。", nameof(presetId));
            if (!_presetsById.TryGetValue(presetId, out PresetRecord preset))
                throw new InvalidOperationException("指定的预设不存在。");
            return preset;
        }


        private static void ValidateLegacySlot(int slot)
        {
            if (slot < 1 || slot > MaxPresets)
                throw new FormatException($"旧工作区预设序号必须在 1–{MaxPresets} 之间。");
        }

        private sealed class PresetRecord
        {
            public string Id { get; }
            public EditorState State { get; }
            public string Name { get; set; }
            public string SavedName { get; set; }
            public bool IsNew { get; set; }

            public PresetRecord(string id, EditorState state, string name, bool isNew)
            {
                Id = id;
                State = state;
                Name = name;
                SavedName = name;
                IsNew = isNew;
            }
        }

        private sealed class WorkspacePreset
        {
            public string Id { get; }
            public string Name { get; }
            public Dictionary<TextId, string> Overrides { get; }

            public WorkspacePreset(string id, string name, Dictionary<TextId, string> overrides)
            {
                Id = id;
                Name = name;
                Overrides = overrides;
            }
        }

        private sealed class WorkspaceDocument
        {
            public string ActivePresetId { get; }
            public List<WorkspacePreset> Presets { get; }
            public bool RequiresMigration { get; }

            public WorkspaceDocument(string activePresetId, List<WorkspacePreset> presets, bool requiresMigration)
            {
                ActivePresetId = activePresetId;
                Presets = presets;
                RequiresMigration = requiresMigration;
            }
        }
    }
}
