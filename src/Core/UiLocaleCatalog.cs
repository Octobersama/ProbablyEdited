using System;
using System.Collections.Generic;
using System.Globalization;

namespace ProbablyEdited
{
    internal readonly struct UiLanguageOption
    {
        public UiLanguageOption(string code, string nativeName)
        {
            Code = code;
            NativeName = nativeName;
        }

        public string Code { get; }
        public string NativeName { get; }
    }

    /// <summary>Resolves UI languages from the live dictionaries loaded by the game.</summary>
    internal sealed class UiLocaleCatalog
    {
        public const string FollowGame = "auto";
        private readonly IDictionary<string, Dictionary<string, string>> tables;
        private readonly List<string> nativeLanguageCodes = new List<string>();
        private readonly HashSet<string> supportedLanguageCodes = new HashSet<string>(StringComparer.Ordinal);

        public UiLocaleCatalog(IDictionary<string, Dictionary<string, string>> tables, IEnumerable<string> nativeLanguageCodes)
        {
            this.tables = tables ?? throw new ArgumentNullException(nameof(tables));
            if (!tables.TryGetValue("en", out Dictionary<string, string> english) || english == null)
                throw new ArgumentException("The mod's English localization dictionary is missing.", nameof(tables));
            SetNativeLanguages(nativeLanguageCodes);
        }

        public void SetNativeLanguages(IEnumerable<string> languageCodes)
        {
            nativeLanguageCodes.Clear();
            supportedLanguageCodes.Clear();
            if (languageCodes != null)
            {
                foreach (string code in languageCodes)
                {
                    if (string.IsNullOrWhiteSpace(code)) continue;
                    string canonical = Canonicalize(code);
                    if (canonical == FollowGame || !supportedLanguageCodes.Add(canonical)) continue;
                    nativeLanguageCodes.Add(code);
                }
            }

            if (supportedLanguageCodes.Add(Canonicalize("zh-TW")))
                nativeLanguageCodes.Add("zh-TW");
        }

        public string ResolveLanguage(string preference, string gameLanguage)
        {
            string requested = string.IsNullOrEmpty(preference) || preference == FollowGame
                ? gameLanguage : preference;
            string language = FindLanguage(requested);
            if (language != null) return language;
            return FindLanguage(gameLanguage) ?? "en";
        }

        public string FindLanguage(string requested)
        {
            if (string.IsNullOrEmpty(requested)) return null;
            if (tables.TryGetValue(requested, out Dictionary<string, string> exact) && exact != null
                && supportedLanguageCodes.Contains(Canonicalize(requested)))
                return requested;

            string canonical = Canonicalize(requested);
            if (supportedLanguageCodes.Contains(canonical))
            {
                foreach (var table in tables)
                {
                    if (table.Value != null && Canonicalize(table.Key) == canonical)
                        return table.Key;
                }
            }

            int separator = canonical.IndexOf('-');
            if (separator > 0)
            {
                string parent = canonical.Substring(0, separator);
                if (supportedLanguageCodes.Contains(parent) || supportedLanguageCodes.Contains(canonical))
                {
                    foreach (var table in tables)
                    {
                        if (table.Value != null && Canonicalize(table.Key) == parent)
                            return table.Key;
                    }
                }
            }
            return null;
        }

        public string Get(string language, string key)
        {
            if (language != null && tables.TryGetValue(language, out Dictionary<string, string> selected)
                && selected != null && selected.TryGetValue(key, out string value))
                return value;
            return tables["en"].TryGetValue(key, out string fallback) ? fallback : key;
        }

        public List<UiLanguageOption> GetLanguages()
        {
            var result = new List<UiLanguageOption>(nativeLanguageCodes.Count);
            foreach (string code in nativeLanguageCodes)
            {
                string language = FindLanguage(code);
                if (language == null) continue;
                Dictionary<string, string> table = tables[language];
                table.TryGetValue("meta.native_name", out string nativeName);
                result.Add(new UiLanguageOption(language,
                    string.IsNullOrWhiteSpace(nativeName) ? language : nativeName));
            }
            result.Sort(CompareLanguages);
            return result;
        }

        public static CultureInfo GetCulture(string language)
        {
            try { return CultureInfo.GetCultureInfo(Canonicalize(language)); }
            catch (CultureNotFoundException) { return CultureInfo.InvariantCulture; }
        }

        private static int CompareLanguages(UiLanguageOption first, UiLanguageOption second)
        {
            int order = SortOrder(first.Code).CompareTo(SortOrder(second.Code));
            return order != 0 ? order : StringComparer.Ordinal.Compare(first.Code, second.Code);
        }

        private static int SortOrder(string code)
        {
            switch (Canonicalize(code))
            {
                case "zh": return 0;
                case "zh-tw": return 1;
                case "en": return 2;
                default: return 3;
            }
        }

        private static string Canonicalize(string code)
        {
            string normalized = (code ?? string.Empty).Replace('_', '-').ToLowerInvariant();
            if (normalized == "ptbr") return "pt-br";
            if (normalized == "zh-cn" || normalized.StartsWith("zh-hans", StringComparison.Ordinal)) return "zh";
            if (normalized == "zh-hk" || normalized.StartsWith("zh-hant", StringComparison.Ordinal)) return "zh-tw";
            return normalized;
        }
    }
}
