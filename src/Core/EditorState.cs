using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ProbablyEdited
{
    public sealed class EditorState
    {
        // Share documents contain only identities and replacements; baseline game text is never exported.
        private const string SchemaName = "probably-edited";
        private const int SchemaVersion = 1;
        private const int PresetSchemaVersion = 2;

        private static readonly string[] DocumentProperties =
        {
            "schema", "schemaVersion", "gameVersion", "overrides"
        };

        private static readonly string[] PresetDocumentProperties =
        {
            "schema", "schemaVersion", "gameVersion", "presetName", "overrides"
        };

        private static readonly string[] OverrideProperties =
        {
            "sourceId", "locale", "tableId", "key", "text"
        };

        private static readonly IReadOnlyList<TextEntry> EmptyEntries =
            Array.AsReadOnly(Array.Empty<TextEntry>());

        private readonly object _stateMarker = new object();
        private HashSet<TextId> _catalogEntryIds = new HashSet<TextId>();
        private readonly Dictionary<TextId, string> _overrides = new Dictionary<TextId, string>();
        private readonly ReadOnlyDictionary<TextId, string> _readOnlyOverrides;
        private IReadOnlyList<TextEntry> _entries = EmptyEntries;
        private Dictionary<TextId, string> _savedOverrides = new Dictionary<TextId, string>();
        private string _catalogLocale;
        private long _stateRevision;

        public IReadOnlyList<TextEntry> Entries => _entries;
        public bool IsDirty => !AreEqual(_overrides, _savedOverrides);
        public int OverrideCount => _overrides.Count;
        public int UnavailableOverrideCount => CountUnavailable(_overrides.Keys);
        public IReadOnlyDictionary<TextId, string> Overrides => _readOnlyOverrides;

        public EditorState()
        {
            _readOnlyOverrides = new ReadOnlyDictionary<TextId, string>(_overrides);
        }

        public void SetCatalog(IReadOnlyList<TextEntry> entries)
        {
            if (entries == null)
                throw new ArgumentNullException(nameof(entries), "目录不能为空。");

            var copiedEntries = new TextEntry[entries.Count];
            var catalogIds = new HashSet<TextId>();
            string locale = null;

            for (int index = 0; index < entries.Count; index++)
            {
                TextEntry entry = entries[index];
                if (entry == null)
                    throw new ArgumentException($"目录第 {index + 1} 项为空，无法载入。", nameof(entries));
                if (!entry.Id.IsValid)
                    throw new ArgumentException($"目录第 {index + 1} 项的标识无效。", nameof(entries));
                if (locale == null)
                    locale = entry.Id.Locale;
                else if (!StringComparer.Ordinal.Equals(locale, entry.Id.Locale))
                    throw new ArgumentException("目录必须只包含当前选中语言的条目。", nameof(entries));
                if (!catalogIds.Add(entry.Id))
                    throw new ArgumentException($"目录中存在重复文本标识：第 {index + 1} 项。", nameof(entries));

                copiedEntries[index] = entry;
            }

            _entries = Array.AsReadOnly(copiedEntries);
            _catalogEntryIds = catalogIds;
            _catalogLocale = locale;
            _stateRevision++;
        }

        public string GetText(TextEntry entry)
        {
            RequireEntry(entry);
            return _overrides.TryGetValue(entry.Id, out string text) ? text : entry.OriginalText;
        }

        public bool IsModified(TextEntry entry)
        {
            RequireEntry(entry);
            return _overrides.ContainsKey(entry.Id);
        }

        public void Stage(TextEntry entry, string text)
        {
            RequireEntry(entry);
            if (text == null)
                throw new ArgumentNullException(nameof(text), "替换文本不能为空引用；如需清空文本，请传入空字符串。");

            if (StringComparer.Ordinal.Equals(text, entry.OriginalText))
            {
                if (_overrides.Remove(entry.Id))
                    _stateRevision++;
                return;
            }

            if (_overrides.TryGetValue(entry.Id, out string current) &&
                StringComparer.Ordinal.Equals(current, text))
                return;

            _overrides[entry.Id] = text;
            _stateRevision++;
        }

        public void Restore(TextEntry entry)
        {
            RequireEntry(entry);
            if (_overrides.Remove(entry.Id))
                _stateRevision++;
        }

        public void LoadSaved(string json)
        {
            Dictionary<TextId, string> loadedOverrides;
            if (string.IsNullOrEmpty(json))
            {
                loadedOverrides = new Dictionary<TextId, string>();
            }
            else
            {
                loadedOverrides = ParseDocument(json).Overrides;
            }

            ReplaceOverrides(loadedOverrides);
            _savedOverrides = new Dictionary<TextId, string>(_overrides);
        }
        internal void LoadOverrides(IReadOnlyDictionary<TextId, string> overrides)
        {
            if (overrides == null)
                throw new ArgumentNullException(nameof(overrides));

            var loadedOverrides = new Dictionary<TextId, string>();
            foreach (KeyValuePair<TextId, string> pair in overrides)
            {
                if (!pair.Key.IsValid)
                    throw new FormatException("工作区包含无效文本标识。");
                if (pair.Value == null)
                    throw new FormatException("工作区替换文本不能为 null。");
                if (loadedOverrides.ContainsKey(pair.Key))
                    throw new FormatException("工作区包含重复文本标识。");
                loadedOverrides.Add(pair.Key, pair.Value);
            }

            ReplaceOverrides(loadedOverrides);
            _savedOverrides = new Dictionary<TextId, string>(_overrides);
        }

        public string ExportJson(string gameVersion)
        {
            return ExportJson(gameVersion, SchemaVersion, null);
        }

        internal string ExportPresetJson(string gameVersion, string presetName)
        {
            if (string.IsNullOrWhiteSpace(presetName))
                throw new ArgumentException("预设名称不能为空。", nameof(presetName));

            return ExportJson(gameVersion, PresetSchemaVersion, presetName);
        }

        private string ExportJson(string gameVersion, int schemaVersion, string presetName)
        {
            if (gameVersion == null)
                throw new ArgumentException("游戏版本不能为空，无法写入补丁兼容性信息。", nameof(gameVersion));

            var orderedOverrides = new List<KeyValuePair<TextId, string>>(_overrides);
            orderedOverrides.Sort(CompareOverrides);

            using (var output = new StringWriter(CultureInfo.InvariantCulture))
            using (var writer = new JsonTextWriter(output))
            {
                writer.Formatting = Formatting.Indented;
                writer.Indentation = 2;
                writer.IndentChar = ' ';
                writer.Culture = CultureInfo.InvariantCulture;
                writer.StringEscapeHandling = StringEscapeHandling.Default;
                writer.CloseOutput = false;

                writer.WriteStartObject();
                writer.WritePropertyName("schema");
                writer.WriteValue(SchemaName);
                writer.WritePropertyName("schemaVersion");
                writer.WriteValue(schemaVersion);
                writer.WritePropertyName("gameVersion");
                writer.WriteValue(gameVersion);
                if (presetName != null)
                {
                    writer.WritePropertyName("presetName");
                    writer.WriteValue(presetName);
                }
                writer.WritePropertyName("overrides");
                writer.WriteStartArray();

                foreach (KeyValuePair<TextId, string> pair in orderedOverrides)
                {
                    writer.WriteStartObject();
                    writer.WritePropertyName("sourceId");
                    writer.WriteValue(pair.Key.SourceId);
                    writer.WritePropertyName("locale");
                    writer.WriteValue(pair.Key.Locale);
                    writer.WritePropertyName("tableId");
                    writer.WriteValue(pair.Key.TableId);
                    writer.WritePropertyName("key");
                    writer.WriteValue(pair.Key.Key);
                    writer.WritePropertyName("text");
                    writer.WriteValue(pair.Value);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
                writer.Flush();
                return output.ToString();
            }
        }


        public ImportPlan PreviewImport(string json, string gameVersion)
        {
            return PreviewImport(ParseDocument(json), gameVersion);
        }

        internal static PatchDocument ParseSharedPatch(string json)
        {
            return ParseDocument(json);
        }

        internal string CatalogLocale => _catalogLocale;

        internal ImportPlan PreviewImport(PatchDocument document, string gameVersion)
        {
            int conflictCount = 0;
            foreach (KeyValuePair<TextId, string> pair in document.Overrides)
            {
                if (_overrides.TryGetValue(pair.Key, out string localText) &&
                    !StringComparer.Ordinal.Equals(localText, pair.Value))
                    conflictCount++;
            }

            int unavailableCount = CountUnavailable(document.Overrides.Keys);
            var warnings = new List<string>();

            if (gameVersion == null)
                warnings.Add("当前游戏版本未知，无法检查补丁版本兼容性。");
            else if (!StringComparer.Ordinal.Equals(document.GameVersion, gameVersion))
                warnings.Add($"补丁版本 {document.GameVersion} 与当前游戏版本 {gameVersion} 不同；请核对后再应用。");

            if (unavailableCount > 0)
                warnings.Add($"当前语言目录中有 {unavailableCount} 项暂不可用；这些覆盖仍会保留，不会按缺失项删除。");
            if (conflictCount > 0)
                warnings.Add($"有 {conflictCount} 项与本地覆盖不同；不选择覆盖冲突时将保留本地值。");

            return new ImportPlan(
                document.Overrides,
                conflictCount,
                unavailableCount,
                warnings,
                _stateMarker,
                _stateRevision);
        }


        public void ApplyImport(ImportPlan plan, bool overwriteConflicts)
        {
            ValidateImportPlan(plan);

            var mergedOverrides = new Dictionary<TextId, string>(_overrides);
            foreach (KeyValuePair<TextId, string> pair in plan.ImportedOverrides)
            {
                if (!overwriteConflicts &&
                    mergedOverrides.TryGetValue(pair.Key, out string localText) &&
                    !StringComparer.Ordinal.Equals(localText, pair.Value))
                    continue;

                mergedOverrides[pair.Key] = pair.Value;
            }

            ReplaceOverrides(mergedOverrides);
        }

        internal void ValidateImportPlan(ImportPlan plan)
        {
            if (plan == null)
                throw new ArgumentNullException(nameof(plan), "导入预览不能为空；请先预览补丁文件。");
            if (!ReferenceEquals(plan.StateMarker, _stateMarker))
                throw new InvalidOperationException("此导入预览不属于当前编辑状态，请重新预览补丁文件。");
            if (plan.StateRevision != _stateRevision)
                throw new InvalidOperationException("预览后目录或本地草稿已变化，请重新预览补丁文件后再导入。");
        }


        public void MarkSaved()
        {
            _savedOverrides = new Dictionary<TextId, string>(_overrides);
        }

        private static PatchDocument ParseDocument(string json)
        {
            if (json == null)
                throw new FormatException("补丁内容为空；请选择有效的 JSON 补丁文件。");

            JToken token;
            try
            {
                using (var input = new StringReader(json))
                using (var reader = new JsonTextReader(input))
                {
                    reader.DateParseHandling = DateParseHandling.None;
                    reader.SupportMultipleContent = true;

                    if (!reader.Read())
                        throw new FormatException("补丁内容为空；请选择有效的 JSON 补丁文件。");
                    if (reader.TokenType == JsonToken.Comment)
                        throw new FormatException("补丁必须是标准 JSON，不能包含注释。");

                    token = JToken.ReadFrom(reader, new JsonLoadSettings
                    {
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                        CommentHandling = CommentHandling.Load
                    });

                    if (reader.Read())
                        throw new FormatException("补丁 JSON 后存在多余内容，请删除额外内容后重试。");
                }
            }
            catch (JsonException exception)
            {
                throw new FormatException("补丁 JSON 格式无效，请检查文件语法或重复字段后重试。详情：" + exception.Message, exception);
            }

            if (ContainsComment(token))
                throw new FormatException("补丁必须是标准 JSON，不能包含注释。");
            if (!(token is JObject root))
                throw new FormatException("补丁根节点必须是 JSON 对象。");

            string schema = RequireString(root, "schema", "补丁", false);
            if (!StringComparer.Ordinal.Equals(schema, SchemaName))
                throw new NotSupportedException($"不支持补丁格式“{schema}”；请使用 {SchemaName} 格式。");

            JToken versionToken = root["schemaVersion"];
            if (versionToken == null || versionToken.Type != JTokenType.Integer)
                throw new FormatException("补丁字段 schemaVersion 必须是整数。");
            string version = Convert.ToString(((JValue)versionToken).Value, CultureInfo.InvariantCulture);
            bool isPresetVersion = StringComparer.Ordinal.Equals(version, PresetSchemaVersion.ToString(CultureInfo.InvariantCulture));
            if (!isPresetVersion && !StringComparer.Ordinal.Equals(version, SchemaVersion.ToString(CultureInfo.InvariantCulture)))
                throw new NotSupportedException($"不支持补丁版本 {version}；当前支持 schemaVersion {SchemaVersion} 和 {PresetSchemaVersion}。");

            RequireExactProperties(root, isPresetVersion ? PresetDocumentProperties : DocumentProperties, "补丁");
            string gameVersion = RequireString(root, "gameVersion", "补丁", true);
            string presetName = isPresetVersion ? RequireString(root, "presetName", "补丁", false) : null;
            JToken overridesToken = root["overrides"];
            if (overridesToken.Type != JTokenType.Array)
                throw new FormatException("补丁字段 overrides 必须是数组。");

            var overrides = new Dictionary<TextId, string>();
            int index = 0;
            foreach (JToken item in (JArray)overridesToken)
            {
                index++;
                if (!(item is JObject overrideObject))
                    throw new FormatException($"补丁 overrides 第 {index} 项必须是 JSON 对象。");

                RequireExactProperties(overrideObject, OverrideProperties, $"补丁 overrides 第 {index} 项");
                string sourceId = RequireString(overrideObject, "sourceId", $"第 {index} 项", false);
                string locale = RequireString(overrideObject, "locale", $"第 {index} 项", false);
                string tableId = RequireString(overrideObject, "tableId", $"第 {index} 项", false);
                string key = RequireString(overrideObject, "key", $"第 {index} 项", false);
                string text = RequireString(overrideObject, "text", $"第 {index} 项", true);
                var id = new TextId(sourceId, locale, tableId, key);

                if (overrides.ContainsKey(id))
                    throw new FormatException($"补丁 overrides 第 {index} 项的文本标识重复；请删除重复项后重试。");

                overrides.Add(id, text);
            }

            return new PatchDocument(gameVersion, presetName, overrides);
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

        private int CountUnavailable(IEnumerable<TextId> ids)
        {
            if (_catalogLocale == null)
                return 0;

            int count = 0;
            foreach (TextId id in ids)
            {
                if (StringComparer.Ordinal.Equals(id.Locale, _catalogLocale) &&
                    !_catalogEntryIds.Contains(id))
                    count++;
            }

            return count;
        }

        private void ReplaceOverrides(Dictionary<TextId, string> replacement)
        {
            if (AreEqual(_overrides, replacement))
                return;

            _overrides.Clear();
            foreach (KeyValuePair<TextId, string> pair in replacement)
                _overrides.Add(pair.Key, pair.Value);
            _stateRevision++;
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

        private static void RequireEntry(TextEntry entry)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry), "文本条目不能为空。");
        }

        internal sealed class PatchDocument
        {
            public string GameVersion { get; }
            public string PresetName { get; }
            public Dictionary<TextId, string> Overrides { get; }

            public PatchDocument(string gameVersion, string presetName, Dictionary<TextId, string> overrides)
            {
                GameVersion = gameVersion;
                PresetName = presetName;
                Overrides = overrides;
            }
        }
    }
}
