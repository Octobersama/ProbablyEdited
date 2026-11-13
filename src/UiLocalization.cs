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
        private readonly List<string> nativeLanguageCodes = new List<string>();
        private readonly Func<string, string> activeLookup;
        private UiLocaleCatalog catalog;
        private string activeLanguage;
        private bool nativeLanguagesLoaded;

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
            ModLocalizationAliases.Register(mod.Localization);
            nativeLanguageCodes.Clear();
            nativeLanguagesLoaded = false;
            optionCodes.Clear();
            catalog = new UiLocaleCatalog(mod.Localization, Array.Empty<string>());
            ActivateForEditor();
        }

        public void ActivateForEditor()
        {
            if (catalog == null)
                throw new InvalidOperationException("The mod's localization assets are not ready.");
            RefreshNativeLanguages();
            activeLanguage = catalog.ResolveLanguage(ReadPreference(), GetGameLanguage());
            UiText.Configure(activeLookup, UiLocaleCatalog.GetCulture(activeLanguage));
        }

        public string GetOptionText(string key)
        {
            RefreshNativeLanguages();
            if (catalog == null)
                return ModHelper.GetLocalized(ModEntry.ModId, key);
            string language = catalog.ResolveLanguage(ReadPreference(), GetGameLanguage());
            return catalog.Get(language, key);
        }

        public string FormatOptionText(string key, params object[] arguments)
        {
            RefreshNativeLanguages();
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
            return captions.ToArray();
        }

        private int GetSelectedIndex()
        {
            if (optionCodes.Count == 0) GetLanguageOptions();
            string preference = ReadPreference();
            string language = preference == UiLocaleCatalog.FollowGame
                ? UiLocaleCatalog.FollowGame
                : catalog?.FindLanguage(preference) ?? UiLocaleCatalog.FollowGame;
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

        private void RefreshNativeLanguages()
        {
            if (catalog == null || nativeLanguagesLoaded) return;
            ValueTuple<string, string>[] choices = NativeGameLanguageChoices.GetChoices();
            if (choices == null || choices.Length == 0) return;

            nativeLanguageCodes.Clear();
            foreach (ValueTuple<string, string> choice in choices)
            {
                if (!string.IsNullOrWhiteSpace(choice.Item1))
                    nativeLanguageCodes.Add(choice.Item1);
            }
            if (nativeLanguageCodes.Count == 0) return;

            catalog.SetNativeLanguages(nativeLanguageCodes);
            nativeLanguagesLoaded = true;
        }

        private static string GetGameLanguage()
        {
            return ModHelper.GetCurrentLocale()?.Identifier.Code ?? "en";
        }

        public void Dispose()
        {
            ModHelper.RemoveGameOptions(ModEntry.ModId);
            optionCodes.Clear();
            nativeLanguageCodes.Clear();
            nativeLanguagesLoaded = false;
            catalog = null;
            activeLanguage = null;
            UiText.Reset();
        }
    }
}
