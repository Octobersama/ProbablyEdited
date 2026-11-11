using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Localization;
using UnityEngine.Localization.Components;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace ProbablyEdited
{
    internal sealed class GameTextCatalog : IDisposable
    {
        private static readonly FieldInfo NativeLocaleChoices = AccessTools.Field(typeof(MainMenuUIController), "LOCALE_DROPDOWN");

        private readonly Dictionary<string, Locale> locales = new Dictionary<string, Locale>(StringComparer.Ordinal);
        private readonly Dictionary<string, IList<StringTable>> tables = new Dictionary<string, IList<StringTable>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<TextEntry>> catalogs = new Dictionary<string, List<TextEntry>>(StringComparer.Ordinal);
        private readonly Dictionary<TextId, Binding> bindings = new Dictionary<TextId, Binding>();
        private readonly List<AsyncOperationHandle> operations = new List<AsyncOperationHandle>();
        private readonly List<string> codes = new List<string>();
        private readonly List<string> names = new List<string>();

        public IReadOnlyList<string> Codes => codes;
        public IReadOnlyList<string> Names => names;
        public string CurrentLocale => Canonical(LocalizationSettings.SelectedLocale?.Identifier.Code ?? "en");

        public IEnumerator Initialize()
        {
            var initialization = LocalizationSettings.InitializationOperation;
            yield return initialization;
            if (initialization.Status != AsyncOperationStatus.Succeeded)
                throw new InvalidOperationException(UiText.Get("catalog.error.localization_init"));
            RefreshLocales();
        }

        public void RefreshLocales()
        {
            var choices = NativeLocaleChoices?.GetValue(null) as ValueTuple<string, string>[];
            if (choices == null || choices.Length == 0)
                throw new InvalidOperationException(UiText.Get("catalog.error.native_locale_choices"));

            codes.Clear();
            names.Clear();
            locales.Clear();
            foreach (var locale in LocalizationSettings.AvailableLocales.Locales)
                locales[Canonical(locale.Identifier.Code)] = locale;

            // Keep every loaded locale available for existing overrides, but mirror the game's picker.
            foreach (var choice in choices)
            {
                string code = Canonical(choice.Item1);
                codes.Add(code);
                names.Add(CatalogPresentation.NativeLanguageName(code, choice.Item2));
            }
        }

        public IEnumerator Load(string code)
        {
            code = Canonical(code);
            if (locales.TryGetValue(code, out var locale) && !tables.ContainsKey(code))
            {
                var request = LocalizationSettings.StringDatabase.GetAllTables(locale);
                // Localization releases GetAllTables on the next frame; retain our cached result until disposal.
                Addressables.ResourceManager.Acquire(request);
                operations.Add(request);
                yield return request;
                if (request.Status != AsyncOperationStatus.Succeeded)
                    throw new InvalidOperationException(UiText.Format("catalog.error.load_locale", code, request.OperationException?.Message));
                tables[code] = request.Result;
            }
            if (code != "en" && locales.TryGetValue("en", out var englishLocale) && !tables.ContainsKey("en"))
            {
                var request = LocalizationSettings.StringDatabase.GetAllTables(englishLocale);
                Addressables.ResourceManager.Acquire(request);
                operations.Add(request);
                yield return request;
                if (request.Status != AsyncOperationStatus.Succeeded)
                    throw new InvalidOperationException(UiText.Format("catalog.error.load_english", request.OperationException?.Message));
                tables["en"] = request.Result;
            }

            var result = new List<TextEntry>();
            if (tables.TryGetValue(code, out var localizedTables))
            {
                foreach (var table in localizedTables)
                {
                    if (table == null || table.SharedData == null) continue;
                    string tableId = table.SharedData.TableCollectionNameGuid.ToString("N");
                    StringTable fallback = FindEnglishTable(tableId);
                    foreach (var key in table.SharedData.Entries)
                    {
                        var id = new TextId("game", code, tableId, key.Id.ToString(CultureInfo.InvariantCulture));
                        if (!bindings.TryGetValue(id, out var binding))
                        {
                            var value = table.GetEntry(key.Id);
                            var fallbackValue = fallback?.GetEntry(key.Id);
                            bool isFallback = value == null || string.IsNullOrEmpty(value.Value);
                            string fallbackText = Baseline(new TextId("game", "en", tableId, id.Key), fallbackValue?.Value ?? key.Key);
                            string original = isFallback ? fallbackText : value.Value;
                            bool smart = isFallback ? fallbackValue?.IsSmart ?? value?.IsSmart ?? false : value.IsSmart;
                            var entry = new TextEntry(id, string.Empty, PlayerStore.VERSION, table.TableCollectionName,
                                key.Key, Category(table.TableCollectionName), original ?? "", isFallback, smart, string.Empty);
                            binding = new Binding(entry, table, key.Id, value?.Value, value != null, value?.IsSmart ?? false, smart);
                            bindings.Add(id, binding);
                        }
                        result.Add(binding.Entry);
                    }
                }
            }

            foreach (var mod in ModLoader.Mods)
            {
                if (!IsActive(mod) || mod.Localization == null) continue;
                string targetCode = FindModLocale(mod, code) ?? code;
                mod.Localization.TryGetValue(targetCode, out var target);
                string englishCode = FindModLocale(mod, "en");
                Dictionary<string, string> english = null;
                if (englishCode != null) mod.Localization.TryGetValue(englishCode, out english);
                var keys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var language in mod.Localization.Values)
                    if (language != null) keys.UnionWith(language.Keys);
                foreach (string key in keys)
                {
                    var id = new TextId(mod.Manifest.ID, code, "Localization", key);
                    if (!bindings.TryGetValue(id, out var binding) || !ReferenceEquals(binding.Mod, mod))
                    {
                        string raw = null;
                        bool exists = target != null && target.TryGetValue(key, out raw);
                        bool fallback = !exists;
                        string original = raw;
                        if (!exists)
                        {
                            string fallbackText = english != null && english.TryGetValue(key, out var value) ? value : key;
                            original = Baseline(new TextId(mod.Manifest.ID, "en", "Localization", key), fallbackText);
                        }
                        var entry = new TextEntry(id, mod.Manifest.Name ?? mod.Manifest.ID, mod.Manifest.ModVersion ?? "",
                            "Localization", key, "mod-localization", original ?? "", fallback, false, string.Empty);
                        binding = new Binding(entry, mod, targetCode, raw, exists);
                        bindings[id] = binding;
                    }
                    result.Add(binding.Entry);
                }
            }
            result.Sort((left, right) =>
            {
                int order = StringComparer.Ordinal.Compare(left.Category, right.Category);
                if (order == 0) order = StringComparer.Ordinal.Compare(left.SourceName, right.SourceName);
                if (order == 0) order = StringComparer.Ordinal.Compare(left.TableName, right.TableName);
                return order == 0 ? StringComparer.Ordinal.Compare(left.KeyName, right.KeyName) : order;
            });
            catalogs[code] = result;
        }

        public IReadOnlyList<TextEntry> Entries(string code) => catalogs.TryGetValue(code, out var entries) ? entries : Array.Empty<TextEntry>();

        public string Coverage(string code)
        {
            var summary = new System.Text.StringBuilder();
            int tableCount = tables.TryGetValue(code, out var loaded) ? loaded.Count : 0;
            summary.Append(UiText.Format("catalog.coverage.header", code, tableCount, Entries(code).Count));
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var entry in Entries(code))
                if (entry.Id.TableId == "Localization")
                    counts[entry.Id.SourceId] = counts.TryGetValue(entry.Id.SourceId, out int count) ? count + 1 : 1;
            foreach (var mod in ModLoader.Mods)
            {
                if (!IsActive(mod)) continue;
                int count = counts.TryGetValue(mod.Manifest.ID, out int found) ? found : 0;
                summary.Append(UiText.Format("catalog.coverage.mod_line", mod.Manifest.Name ?? mod.Manifest.ID, count));
                if (count == 0) summary.Append(UiText.Get("catalog.coverage.no_entries"));
                summary.Append('\n');
            }
            summary.Append(UiText.Get("catalog.coverage.scope"));
            return summary.ToString();
        }

        public int Apply(IReadOnlyDictionary<TextId, string> overrides)
        {
            int applied = 0;
            foreach (var binding in bindings.Values)
            {
                if (binding.Mod != null && !IsActive(binding.Mod)) continue;
                if (overrides.TryGetValue(binding.Entry.Id, out string text))
                {
                    binding.Apply(text);
                    applied++;
                }
                else binding.Restore();
            }
            return applied;
        }

        public static void RefreshVisibleText()
        {
            foreach (var localized in Resources.FindObjectsOfTypeAll<LocalizeStringEvent>())
                if (localized != null && localized.isActiveAndEnabled && localized.gameObject.scene.IsValid())
                    localized.RefreshString();
        }

        // ModHelper.GetLocalized uses the exact Locale.Identifier.Code dictionary key.
        public static string Canonical(string code) => code ?? "en";

        public void Dispose()
        {
            foreach (var binding in bindings.Values) binding.Restore();
            foreach (var operation in operations)
                if (operation.IsValid()) Addressables.Release(operation);
            operations.Clear();
            bindings.Clear();
        }

        private StringTable FindEnglishTable(string tableId)
        {
            if (!tables.TryGetValue("en", out var english)) return null;
            foreach (var table in english)
                if (table != null && table.SharedData.TableCollectionNameGuid.ToString("N") == tableId) return table;
            return null;
        }

        private string Baseline(TextId id, string current) =>
            bindings.TryGetValue(id, out var binding) ? binding.Entry.OriginalText : current;

        private static bool IsActive(LoadedMod mod) => mod != null && mod.IsEnabled && mod.Instance != null && mod.Manifest != null && string.IsNullOrEmpty(mod.LoadError);

        private static string FindModLocale(LoadedMod mod, string code)
        {
            foreach (string candidate in mod.Localization.Keys)
                if (Canonical(candidate) == code) return candidate;
            return null;
        }

        private static string Category(string table)
        {
            switch (table)
            {
                case "ClientName": return "ClientName";
                case "Dialogue": return "Dialogue";
                case "PhoneDialogue": return "PhoneDialogue";
                case "Item": return "Item";
                case "Interactable": return "Interactable";
                case "Hint": return "Hint";
                case "MainMenu": return "MainMenu";
                case "UI": return "UI";
                case "Mechanic": return "Mechanic";
                case "NetworkUpgrade": return "NetworkUpgrade";
                case "NewGame": return "NewGame";
                case "PerkTable": return "PerkTable";
                case "RepPerkTable": return "RepPerkTable";
                case "Tutorial": return "Tutorial";
                default: return "table:" + table;
            }
        }


        private sealed class Binding
        {
            public readonly TextEntry Entry;
            public readonly LoadedMod Mod;
            private readonly StringTable table;
            private readonly long keyId;
            private readonly string locale;
            private readonly string original;
            private readonly bool existed;
            private readonly bool smart;
            private readonly bool originalSmart;
            private bool applied;
            private bool createdLocale;

            public Binding(TextEntry entry, StringTable table, long keyId, string original, bool existed, bool originalSmart, bool smart)
            {
                Entry = entry;
                this.table = table;
                this.keyId = keyId;
                this.original = original;
                this.existed = existed;
                this.smart = smart;
                this.originalSmart = originalSmart;
            }

            public Binding(TextEntry entry, LoadedMod mod, string locale, string original, bool existed)
            {
                Entry = entry;
                Mod = mod;
                this.locale = locale;
                this.original = original;
                this.existed = existed;
            }

            public void Apply(string text)
            {
                if (Mod == null)
                {
                    if (table == null) throw new InvalidOperationException(UiText.Get("catalog.error.table_unloaded"));
                    var value = table.GetEntry(keyId) ?? table.AddEntry(keyId, text);
                    value.Value = text;
                    value.IsSmart = smart;
                }
                else
                {
                    if (!Mod.Localization.TryGetValue(locale, out var dictionary))
                    {
                        dictionary = new Dictionary<string, string>(StringComparer.Ordinal);
                        Mod.Localization[locale] = dictionary;
                        createdLocale = true;
                    }
                    dictionary[Entry.Id.Key] = text;
                }
                applied = true;
            }

            public void Restore()
            {
                if (!applied) return;
                if (Mod == null && table == null) { applied = false; return; }
                if (table != null)
                {
                    if (existed)
                    {
                        var value = table.GetEntry(keyId) ?? table.AddEntry(keyId, original);
                        value.Value = original;
                        value.IsSmart = originalSmart;
                    }
                    else table.RemoveEntry(keyId);
                }
                else if (Mod.Localization.TryGetValue(locale, out var dictionary))
                {
                    if (existed) dictionary[Entry.Id.Key] = original;
                    else dictionary.Remove(Entry.Id.Key);
                    if (createdLocale && dictionary.Count == 0) Mod.Localization.Remove(locale);
                }
                applied = false;
                createdLocale = false;
            }
        }
    }
}
