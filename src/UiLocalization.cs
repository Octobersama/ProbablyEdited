using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProbablyEdited
{
    internal sealed class UiLocalization : IDisposable
    {
        private const string PreferenceKey = ModEntry.ModId + ".ui_language";
        private const string SectionId = "interface";
        private const string OptionId = "ui_language";
        private readonly List<string> optionCodes = new List<string>();
        private readonly Func<string, string> activeLookup;
        private UiLocaleCatalog catalog;
        private string activeLanguage;

        public UiLocalization()
        {
            activeLookup = LookupActiveText;
        }

        public void RegisterOptions()
        {
            ModHelper.AddGameOptionSection(ModEntry.ModId, SectionId,
                () => "Probably Edited · " + GetOptionText("options.section"));
            ModHelper.AddGameOptionDropdown(ModEntry.ModId, SectionId, OptionId,
                () => GetOptionText("options.ui_language"), GetLanguageOptions,
                GetSelectedIndex, SetSelectedIndex,
                tooltip: () => GetOptionText("options.ui_language_hint"));
        }

        public void Attach(LoadedMod mod)
        {
            catalog = new UiLocaleCatalog(mod.Localization);
            ActivateForEditor();
        }

        public void ActivateForEditor()
        {
            if (catalog == null)
                throw new InvalidOperationException("The mod's localization assets are not ready.");
            activeLanguage = catalog.ResolveLanguage(ReadPreference(), GetGameLanguage());
            UiText.Configure(activeLookup, UiLocaleCatalog.GetCulture(activeLanguage));
        }

        public string GetOptionText(string key)
        {
            if (catalog == null)
                return ModHelper.GetLocalized(ModEntry.ModId, key);
            string language = catalog.ResolveLanguage(ReadPreference(), GetGameLanguage());
            return catalog.Get(language, key);
        }

        public string FormatOptionText(string key, params object[] arguments)
        {
            string language = catalog == null ? GetGameLanguage()
                : catalog.ResolveLanguage(ReadPreference(), GetGameLanguage());
            return string.Format(UiLocaleCatalog.GetCulture(language), GetOptionText(key), arguments);
        }

        private string LookupActiveText(string key)
        {
            return catalog.Get(activeLanguage, key);
        }

        private string[] GetLanguageOptions()
        {
            optionCodes.Clear();
            optionCodes.Add(UiLocaleCatalog.FollowGame);
            var captions = new List<string> { GetOptionText("options.follow_game") };
            if (catalog != null)
            {
                foreach (UiLanguageOption language in catalog.GetLanguages())
                {
                    optionCodes.Add(language.Code);
                    captions.Add(language.NativeName);
                }
            }

            string preference = ReadPreference();
            if (preference != UiLocaleCatalog.FollowGame && (catalog == null || catalog.FindLanguage(preference) == null))
            {
                // Preserve an unavailable language preference for a later resource update.
                optionCodes.Add(preference);
                captions.Add(GetOptionText("options.unavailable_language") + " [" + preference + "]");
            }
            return captions.ToArray();
        }

        private int GetSelectedIndex()
        {
            if (optionCodes.Count == 0) GetLanguageOptions();
            string preference = ReadPreference();
            string language = preference == UiLocaleCatalog.FollowGame ? preference
                : catalog?.FindLanguage(preference) ?? preference;
            for (int i = 0; i < optionCodes.Count; i++)
            {
                if (string.Equals(optionCodes[i], language, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return 0;
        }

        private void SetSelectedIndex(int index)
        {
            if (index < 0 || index >= optionCodes.Count) return;
            string language = optionCodes[index];
            if (string.Equals(ReadPreference(), language, StringComparison.Ordinal)) return;
            PlayerPrefs.SetString(PreferenceKey, language);
            PlayerPrefs.Save();
        }

        private static string ReadPreference()
        {
            string preference = PlayerPrefs.GetString(PreferenceKey, UiLocaleCatalog.FollowGame);
            return string.IsNullOrEmpty(preference) ? UiLocaleCatalog.FollowGame : preference;
        }

        private static string GetGameLanguage()
        {
            return ModHelper.GetCurrentLocale()?.Identifier.Code ?? "en";
        }

        public void Dispose()
        {
            ModHelper.RemoveGameOptions(ModEntry.ModId);
            optionCodes.Clear();
            catalog = null;
            activeLanguage = null;
            UiText.Reset();
        }
    }
}
