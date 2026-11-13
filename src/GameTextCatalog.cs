using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
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
        public const string GameSourceId = "game";

        private readonly Dictionary<string, Locale> locales = new Dictionary<string, Locale>(StringComparer.Ordinal);
        private readonly Dictionary<string, IList<StringTable>> tables = new Dictionary<string, IList<StringTable>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<TextEntry>> catalogs = new Dictionary<string, List<TextEntry>>(StringComparer.Ordinal);
        private readonly Dictionary<TextId, Binding> bindings = new Dictionary<TextId, Binding>();
        private readonly List<AsyncOperationHandle> operations = new List<AsyncOperationHandle>();
        private readonly List<string> codes = new List<string>();
        private readonly List<string> names = new List<string>();
        // Multiple TextIds may resolve to one dictionary entry, so baseline and apply ownership live there.
        private readonly Dictionary<ModDictionaryEntryKey, ModTarget> modTargetsByDictionaryEntry = new Dictionary<ModDictionaryEntryKey, ModTarget>();
        private readonly Dictionary<ModLocaleKey, ModLocaleState> modLocaleStatesByLocale = new Dictionary<ModLocaleKey, ModLocaleState>();
        private readonly List<ModTarget> modTargets = new List<ModTarget>();
        private readonly List<ModLocaleState> modLocaleStates = new List<ModLocaleState>();

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
            var choices = NativeGameLanguageChoices.GetChoices();
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
                        var id = new TextId(GameSourceId, code, tableId, key.Id.ToString(CultureInfo.InvariantCulture));
                        if (!bindings.TryGetValue(id, out var binding))
                        {
                            var value = table.GetEntry(key.Id);
                            var fallbackValue = fallback?.GetEntry(key.Id);
                            bool isFallback = value == null || string.IsNullOrEmpty(value.Value);
                            string fallbackText = Baseline(new TextId(GameSourceId, "en", tableId, id.Key), fallbackValue?.Value ?? key.Key);
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
                    string raw = null;
                    bool exists = target != null && target.TryGetValue(key, out raw);
                    string originalText = raw;
                    if (!exists)
                    {
                        string fallbackText = english != null && english.TryGetValue(key, out var value) ? value : key;
                        originalText = Baseline(new TextId(mod.Manifest.ID, "en", "Localization", key), fallbackText);
                    }

                    bindings.TryGetValue(id, out var existing);
                    ModTarget modTarget = GetModTarget(mod, targetCode, target, key, raw, exists, originalText, existing);
                    Binding binding;
                    if (existing == null || !ReferenceEquals(existing.Mod, mod) || !ReferenceEquals(existing.ModTarget, modTarget))
                    {
                        var entry = new TextEntry(id, mod.Manifest.Name ?? mod.Manifest.ID, mod.Manifest.ModVersion ?? "",
                            "Localization", key, "mod-localization", modTarget.OriginalText, !modTarget.Existed, false, string.Empty);
                        binding = new Binding(entry, modTarget);
                        ModTarget previousTarget = existing?.ModTarget;
                        previousTarget?.Bindings.Remove(existing);
                        if (previousTarget != null && !ReferenceEquals(previousTarget, modTarget)
                            && previousTarget.Bindings.Count == 0)
                            previousTarget.Restore();
                        modTarget.Bindings.Add(binding);
                        bindings[id] = binding;
                    }
                    else binding = existing;
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
            // Resolve late registrations before alias validation; missing locales remain uncreated.
            BindUnboundModTargets();
            int applied = ValidateModOverrides(overrides);
            foreach (var binding in bindings.Values)
            {
                if (binding.Mod != null) continue;
                if (overrides.TryGetValue(binding.Entry.Id, out string text))
                {
                    binding.Apply(text);
                    applied++;
                }
                else binding.Restore();
            }

            foreach (ModTarget target in modTargets)
            {
                bool active = false;
                bool hasOverride = false;
                string text = null;
                foreach (Binding binding in target.Bindings)
                {
                    if (!IsActive(binding.Mod)) continue;
                    active = true;
                    if (overrides.TryGetValue(binding.Entry.Id, out text))
                    {
                        hasOverride = true;
                        break;
                    }
                }
                if (!active) continue;
                if (hasOverride) ApplyModTarget(target, text);
                else target.Restore();
            }
            CleanupCreatedModLocales();
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
            foreach (var binding in bindings.Values)
                if (binding.Mod == null) binding.Restore();
            foreach (ModTarget target in modTargets) target.Restore();
            CleanupCreatedModLocales();
            foreach (var operation in operations)
                if (operation.IsValid()) Addressables.Release(operation);
            operations.Clear();
            bindings.Clear();
            modTargets.Clear();
            modTargetsByDictionaryEntry.Clear();
            modLocaleStatesByLocale.Clear();
            modLocaleStates.Clear();
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
        private ModTarget GetModTarget(
            LoadedMod mod,
            string locale,
            Dictionary<string, string> dictionary,
            string key,
            string original,
            bool existed,
            string originalText,
            Binding existing)
        {
            if (existing != null && ReferenceEquals(existing.Mod, mod)
                && existing.ModTarget != null && existing.ModTarget.Matches(dictionary, locale, key))
                return existing.ModTarget;

            if (dictionary != null)
            {
                var identity = new ModDictionaryEntryKey(dictionary, key);
                if (modTargetsByDictionaryEntry.TryGetValue(identity, out var sharedTarget))
                    return sharedTarget;
            }

            var target = new ModTarget(mod, locale, dictionary, key, original, existed, originalText);
            modTargets.Add(target);
            if (dictionary != null)
                modTargetsByDictionaryEntry.Add(new ModDictionaryEntryKey(dictionary, key), target);
            return target;
        }

        private void ApplyModTarget(ModTarget target, string text)
        {
            var localeKey = new ModLocaleKey(target.Mod, target.Locale);
            modLocaleStatesByLocale.TryGetValue(localeKey, out ModLocaleState localeState);

            Dictionary<string, string> dictionary = target.TargetDictionary;
            bool addToDictionaryIndex = dictionary == null;
            if (addToDictionaryIndex)
            {
                if (!target.Mod.Localization.TryGetValue(target.Locale, out dictionary))
                {
                    dictionary = new Dictionary<string, string>(StringComparer.Ordinal);
                    target.Mod.Localization[target.Locale] = dictionary;
                    if (localeState == null)
                        localeState = GetOrCreateModLocaleState(target.Mod, target.Locale);
                    localeState.CreatedDictionary = dictionary;
                }
                target.BindDictionary(dictionary);
            }
            else if (!target.Mod.Localization.TryGetValue(target.Locale, out _)
                && localeState != null && ReferenceEquals(localeState.CreatedDictionary, dictionary))
            {
                target.Mod.Localization[target.Locale] = dictionary;
            }

            if (localeState != null && ReferenceEquals(localeState.CreatedDictionary, dictionary))
                target.LocaleState = localeState;

            target.Apply(text);
            if (addToDictionaryIndex)
                modTargetsByDictionaryEntry.Add(new ModDictionaryEntryKey(dictionary, target.Key), target);
        }

        private void BindUnboundModTargets()
        {
            for (int i = 0; i < modTargets.Count; i++)
            {
                ModTarget target = modTargets[i];
                if (target.TargetDictionary != null
                    || !target.Mod.Localization.TryGetValue(target.Locale, out var dictionary)
                    || dictionary == null) continue;

                var identity = new ModDictionaryEntryKey(dictionary, target.Key);
                if (modTargetsByDictionaryEntry.TryGetValue(identity, out var sharedTarget))
                {
                    foreach (Binding binding in target.Bindings)
                    {
                        binding.ModTarget = sharedTarget;
                        sharedTarget.Bindings.Add(binding);
                    }
                    target.Bindings.Clear();
                    modTargets.RemoveAt(i--);
                    continue;
                }

                target.BindDictionary(dictionary);
                modTargetsByDictionaryEntry.Add(identity, target);
                if (modLocaleStatesByLocale.TryGetValue(new ModLocaleKey(target.Mod, target.Locale), out var localeState)
                    && ReferenceEquals(localeState.CreatedDictionary, dictionary))
                    target.LocaleState = localeState;
            }
        }

        private ModLocaleState GetOrCreateModLocaleState(LoadedMod mod, string locale)
        {
            var identity = new ModLocaleKey(mod, locale);
            if (modLocaleStatesByLocale.TryGetValue(identity, out var state))
                return state;

            state = new ModLocaleState(mod, locale);
            modLocaleStatesByLocale.Add(identity, state);
            modLocaleStates.Add(state);
            return state;
        }

        private int ValidateModOverrides(IReadOnlyDictionary<TextId, string> overrides)
        {
            int applied = 0;
            foreach (ModTarget target in modTargets)
            {
                bool found = false;
                string firstText = null;
                foreach (Binding binding in target.Bindings)
                {
                    if (!IsActive(binding.Mod) || !overrides.TryGetValue(binding.Entry.Id, out string text)) continue;
                    if (found && !StringComparer.Ordinal.Equals(firstText, text))
                        throw new InvalidOperationException(UiText.Format("catalog.error.localization_alias_conflict", target.Key));
                    found = true;
                    firstText = text;
                    applied++;
                }
            }
            return applied;
        }

        private void CleanupCreatedModLocales()
        {
            foreach (ModLocaleState state in modLocaleStates)
            {
                if (state.CreatedDictionary == null || state.AppliedTargets != 0
                    || !state.Mod.Localization.TryGetValue(state.Locale, out var dictionary)
                    || !ReferenceEquals(dictionary, state.CreatedDictionary)
                    || dictionary.Count != 0) continue;
                state.Mod.Localization.Remove(state.Locale);
            }
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


        private readonly struct ModDictionaryEntryKey : IEquatable<ModDictionaryEntryKey>
        {
            private readonly Dictionary<string, string> dictionary;
            private readonly string key;

            public ModDictionaryEntryKey(Dictionary<string, string> dictionary, string key)
            {
                this.dictionary = dictionary;
                this.key = key;
            }

            public bool Equals(ModDictionaryEntryKey other) =>
                ReferenceEquals(dictionary, other.dictionary) && StringComparer.Ordinal.Equals(key, other.key);

            public override bool Equals(object obj) => obj is ModDictionaryEntryKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked { return RuntimeHelpers.GetHashCode(dictionary) * 397 ^ StringComparer.Ordinal.GetHashCode(key); }
            }
        }

        private readonly struct ModLocaleKey : IEquatable<ModLocaleKey>
        {
            private readonly LoadedMod mod;
            private readonly string locale;

            public ModLocaleKey(LoadedMod mod, string locale)
            {
                this.mod = mod;
                this.locale = locale;
            }

            public bool Equals(ModLocaleKey other) =>
                ReferenceEquals(mod, other.mod) && StringComparer.Ordinal.Equals(locale, other.locale);

            public override bool Equals(object obj) => obj is ModLocaleKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked { return RuntimeHelpers.GetHashCode(mod) * 397 ^ StringComparer.Ordinal.GetHashCode(locale); }
            }
        }

        private sealed class ModLocaleState
        {
            public readonly LoadedMod Mod;
            public readonly string Locale;
            public Dictionary<string, string> CreatedDictionary;
            public int AppliedTargets;

            public ModLocaleState(LoadedMod mod, string locale)
            {
                Mod = mod;
                Locale = locale;
            }
        }

        private sealed class ModTarget
        {
            public readonly LoadedMod Mod;
            public readonly string Locale;
            public Dictionary<string, string> TargetDictionary { get; private set; }
            public readonly string Key;
            public string Original { get; private set; }
            public bool Existed { get; private set; }
            public readonly string OriginalText;
            public ModLocaleState LocaleState { get; set; }
            public readonly List<Binding> Bindings = new List<Binding>();
            private bool applied;

            public ModTarget(
                LoadedMod mod,
                string locale,
                Dictionary<string, string> dictionary,
                string key,
                string original,
                bool existed,
                string originalText)
            {
                Mod = mod;
                Locale = locale;
                TargetDictionary = dictionary;
                Key = key;
                Original = original;
                Existed = existed;
                OriginalText = originalText ?? string.Empty;
            }

            public void BindDictionary(Dictionary<string, string> dictionary)
            {
                TargetDictionary = dictionary;
                Existed = dictionary.TryGetValue(Key, out string original);
                Original = original;
            }

            public bool Matches(Dictionary<string, string> dictionary, string locale, string key)
            {
                if (!StringComparer.Ordinal.Equals(Key, key)) return false;
                if (TargetDictionary != null) return ReferenceEquals(TargetDictionary, dictionary);
                return dictionary == null && StringComparer.Ordinal.Equals(Locale, locale);
            }

            public void Apply(string text)
            {
                TargetDictionary[Key] = text;
                if (!applied && LocaleState != null) LocaleState.AppliedTargets++;
                applied = true;
            }

            public void Restore()
            {
                if (!applied) return;
                if (Existed) TargetDictionary[Key] = Original;
                else TargetDictionary.Remove(Key);
                if (LocaleState != null) LocaleState.AppliedTargets--;
                applied = false;
            }
        }

        private sealed class Binding
        {
            public readonly TextEntry Entry;
            public LoadedMod Mod => ModTarget?.Mod;
            public ModTarget ModTarget { get; set; }
            private readonly StringTable table;
            private readonly long keyId;
            private readonly string original;
            private readonly bool existed;
            private readonly bool smart;
            private readonly bool originalSmart;
            private bool applied;

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

            public Binding(TextEntry entry, ModTarget modTarget)
            {
                Entry = entry;
                ModTarget = modTarget;
            }

            public void Apply(string text)
            {
                if (table == null) throw new InvalidOperationException(UiText.Get("catalog.error.table_unloaded"));
                var value = table.GetEntry(keyId) ?? table.AddEntry(keyId, text);
                value.Value = text;
                value.IsSmart = smart;
                applied = true;
            }

            public void Restore()
            {
                if (!applied) return;
                if (table == null) { applied = false; return; }
                if (existed)
                {
                    var value = table.GetEntry(keyId) ?? table.AddEntry(keyId, original);
                    value.Value = original;
                    value.IsSmart = originalSmart;
                }
                else table.RemoveEntry(keyId);
                applied = false;
            }
        }
    }
}
