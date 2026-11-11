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
                throw new ArgumentNullException(nameof(entries), UiText.Get("core.catalog.null"));

            var copiedEntries = new TextEntry[entries.Count];
            var catalogIds = new HashSet<TextId>();
            string locale = null;

            for (int index = 0; index < entries.Count; index++)
            {
                TextEntry entry = entries[index];
                if (entry == null)
                    throw new ArgumentException(UiText.Format("core.catalog.null_entry", index + 1), nameof(entries));
                if (!entry.Id.IsValid)
                    throw new ArgumentException(UiText.Format("core.catalog.invalid_entry_id", index + 1), nameof(entries));
                if (locale == null)
                    locale = entry.Id.Locale;
                else if (!StringComparer.Ordinal.Equals(locale, entry.Id.Locale))
                    throw new ArgumentException(UiText.Get("core.catalog.locale_mismatch"), nameof(entries));
                if (!catalogIds.Add(entry.Id))
                    throw new ArgumentException(UiText.Format("core.catalog.duplicate_entry", index + 1), nameof(entries));

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
                throw new ArgumentNullException(nameof(text), UiText.Get("core.edit.text_null"));

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
                    throw new FormatException(UiText.Get("core.workspace.invalid_text_id"));
                if (pair.Value == null)
                    throw new FormatException(UiText.Get("core.workspace.text_null"));
                if (loadedOverrides.ContainsKey(pair.Key))
                    throw new FormatException(UiText.Get("core.workspace.duplicate_text_id"));
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
                throw new ArgumentException(UiText.Get("core.export.name_empty"), nameof(presetName));

            return ExportJson(gameVersion, PresetSchemaVersion, presetName);
        }

        private string ExportJson(string gameVersion, int schemaVersion, string presetName)
        {
            if (gameVersion == null)
                throw new ArgumentException(UiText.Get("core.export.game_version_null"), nameof(gameVersion));

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
                warnings.Add(UiText.Get("import.warning.game_unknown"));
            else if (!StringComparer.Ordinal.Equals(document.GameVersion, gameVersion))
                warnings.Add(UiText.Format("import.warning.game_mismatch", document.GameVersion, gameVersion));

            if (unavailableCount > 0)
                warnings.Add(UiText.Format("import.warning.unavailable_count", unavailableCount));
            if (conflictCount > 0)
                warnings.Add(UiText.Format("import.warning.conflict", conflictCount));

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
                throw new ArgumentNullException(nameof(plan), UiText.Get("core.import.preview_null"));
            if (!ReferenceEquals(plan.StateMarker, _stateMarker))
                throw new InvalidOperationException(UiText.Get("core.import.preview_wrong_state"));
            if (plan.StateRevision != _stateRevision)
                throw new InvalidOperationException(UiText.Get("core.import.preview_changed"));
        }


        public void MarkSaved()
        {
            _savedOverrides = new Dictionary<TextId, string>(_overrides);
        }

        private static PatchDocument ParseDocument(string json)
        {
            if (json == null)
                throw new FormatException(UiText.Get("core.patch.empty"));

            JToken token;
            try
            {
                using (var input = new StringReader(json))
                using (var reader = new JsonTextReader(input))
                {
                    reader.DateParseHandling = DateParseHandling.None;
                    reader.SupportMultipleContent = true;

                    if (!reader.Read())
                        throw new FormatException(UiText.Get("core.patch.empty"));
                    if (reader.TokenType == JsonToken.Comment)
                        throw new FormatException(UiText.Get("core.patch.standard_json"));

                    token = JToken.ReadFrom(reader, new JsonLoadSettings
                    {
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                        CommentHandling = CommentHandling.Load
                    });

                    if (reader.Read())
                        throw new FormatException(UiText.Get("core.patch.trailing_content"));
                }
            }
            catch (JsonException exception)
            {
                throw new FormatException(UiText.Format("core.patch.invalid_json", exception.Message), exception);
            }

            if (ContainsComment(token))
                throw new FormatException(UiText.Get("core.patch.standard_json"));
            if (!(token is JObject root))
                throw new FormatException(UiText.Get("core.patch.root_object"));

            string schema = RequireString(root, "schema", UiText.Get("core.context.patch"), false);
            if (!StringComparer.Ordinal.Equals(schema, SchemaName))
                throw new NotSupportedException(UiText.Format("core.patch.unsupported_schema", schema, SchemaName));

            JToken versionToken = root["schemaVersion"];
            if (versionToken == null || versionToken.Type != JTokenType.Integer)
                throw new FormatException(UiText.Get("core.patch.version_integer"));
            string version = Convert.ToString(((JValue)versionToken).Value, CultureInfo.InvariantCulture);
            bool isPresetVersion = StringComparer.Ordinal.Equals(version, PresetSchemaVersion.ToString(CultureInfo.InvariantCulture));
            if (!isPresetVersion && !StringComparer.Ordinal.Equals(version, SchemaVersion.ToString(CultureInfo.InvariantCulture)))
                throw new NotSupportedException(UiText.Format("core.patch.unsupported_version", version, SchemaVersion, PresetSchemaVersion));

            RequireExactProperties(root, isPresetVersion ? PresetDocumentProperties : DocumentProperties, UiText.Get("core.context.patch"));
            string gameVersion = RequireString(root, "gameVersion", UiText.Get("core.context.patch"), true);
            string presetName = isPresetVersion ? RequireString(root, "presetName", UiText.Get("core.context.patch"), false) : null;
            JToken overridesToken = root["overrides"];
            if (overridesToken.Type != JTokenType.Array)
                throw new FormatException(UiText.Get("core.patch.overrides_array"));

            var overrides = new Dictionary<TextId, string>();
            int index = 0;
            foreach (JToken item in (JArray)overridesToken)
            {
                index++;
                if (!(item is JObject overrideObject))
                    throw new FormatException(UiText.Format("core.patch.override_object", index));

                string overrideContext = UiText.Format("core.overrides.item_context", UiText.Get("core.context.patch") + " overrides", index);
                RequireExactProperties(overrideObject, OverrideProperties, overrideContext);
                string sourceId = RequireString(overrideObject, "sourceId", overrideContext, false);
                string locale = RequireString(overrideObject, "locale", overrideContext, false);
                string tableId = RequireString(overrideObject, "tableId", overrideContext, false);
                string key = RequireString(overrideObject, "key", overrideContext, false);
                string text = RequireString(overrideObject, "text", overrideContext, true);
                var id = new TextId(sourceId, locale, tableId, key);

                if (overrides.ContainsKey(id))
                    throw new FormatException(UiText.Format("core.patch.duplicate_override", index));

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
                    throw new FormatException(UiText.Format("core.json.unsupported_field", context, property.Name));
            }

            for (int index = 0; index < expected.Length; index++)
            {
                if (value.Property(expected[index]) == null)
                    throw new FormatException(UiText.Format("core.json.missing_field", context, expected[index]));
            }
        }

        private static string RequireString(JObject value, string propertyName, string context, bool allowEmpty)
        {
            JToken token = value[propertyName];
            if (token == null || token.Type != JTokenType.String)
                throw new FormatException(UiText.Format("core.json.string_type", context, propertyName));

            string text = token.Value<string>();
            if (text == null || (!allowEmpty && string.IsNullOrWhiteSpace(text)))
                throw new FormatException(UiText.Format("core.json.string_empty", context, propertyName));

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
                throw new ArgumentNullException(nameof(entry), UiText.Get("core.entry.null"));
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
