using System;
using System.Globalization;

namespace ProbablyEdited
{
    public static class CatalogPresentation
    {
        public static string NativeLanguageName(string code, string authoredName)
        {
            switch (NormalizeLocaleForLookup(code))
            {
                case "en": return "English";
                case "zh": return "简体中文";
                case "fr": return "Français";
                case "de": return "Deutsch";
                case "ru": return "Русский";
                case "ja": return "日本語";
                case "es": return "Español";
                case "ptbr": return "Português (Brasil)";
                case "ko": return "한국어";
                case "it": return "Italiano";
                case "pl": return "Polski";
            }

            string cultureName = GetNativeCultureName(code);
            if (!string.IsNullOrWhiteSpace(cultureName))
                return cultureName;

            if (!string.IsNullOrWhiteSpace(authoredName))
                return authoredName.Trim();

            return string.IsNullOrWhiteSpace(code) ? UiText.Get("catalog.locale.unknown") : code;
        }

        public static string SourceName(TextEntry entry)
        {
            if (entry == null)
                return string.Empty;

            return StringComparer.Ordinal.Equals(entry.Id.SourceId, "game")
                ? UiText.Get("catalog.source.game_tables")
                : entry.SourceName;
        }

        public static string CategoryLabel(string category)
        {
            if (string.IsNullOrWhiteSpace(category))
                return UiText.Get("editor.detail.uncategorized");

            switch (category)
            {
                case "ClientName": return UiText.Get("category.client_name.label");
                case "Dialogue": return UiText.Get("category.dialogue.label");
                case "PhoneDialogue": return UiText.Get("category.phone_dialogue.label");
                case "Item": return UiText.Get("category.item.label");
                case "Interactable": return UiText.Get("category.interactable.label");
                case "Hint": return UiText.Get("category.hint.label");
                case "MainMenu": return UiText.Get("category.main_menu.label");
                case "UI": return UiText.Get("category.ui.label");
                case "Mechanic": return UiText.Get("category.mechanic.label");
                case "NetworkUpgrade": return UiText.Get("category.network_upgrade.label");
                case "NewGame": return UiText.Get("category.new_game.label");
                case "PerkTable": return UiText.Get("category.perk.label");
                case "RepPerkTable": return UiText.Get("category.rep_perk.label");
                case "Tutorial": return UiText.Get("category.tutorial.label");
                case "mod-localization": return UiText.Get("category.mod_localization.label");
                default:
                    return UiText.Format("category.unknown.label", UnknownTableName(category));
            }
        }

        public static string CategoryDescription(string category)
        {
            if (string.IsNullOrWhiteSpace(category))
                return UiText.Get("category.all.description");

            switch (category)
            {
                case "ClientName": return UiText.Get("category.client_name.description");
                case "Dialogue": return UiText.Get("category.dialogue.description");
                case "PhoneDialogue": return UiText.Get("category.phone_dialogue.description");
                case "Item": return UiText.Get("category.item.description");
                case "Interactable": return UiText.Get("category.interactable.description");
                case "Hint": return UiText.Get("category.hint.description");
                case "MainMenu": return UiText.Get("category.main_menu.description");
                case "UI": return UiText.Get("category.ui.description");
                case "Mechanic": return UiText.Get("category.mechanic.description");
                case "NetworkUpgrade": return UiText.Get("category.network_upgrade.description");
                case "NewGame": return UiText.Get("category.new_game.description");
                case "PerkTable": return UiText.Get("category.perk.description");
                case "RepPerkTable": return UiText.Get("category.rep_perk.description");
                case "Tutorial": return UiText.Get("category.tutorial.description");
                case "mod-localization": return UiText.Get("category.mod_localization.description");
                default:
                    return UiText.Format("category.unknown.description", UnknownTableName(category));
            }
        }

        public static bool CategoryContains(string category, string query)
        {
            if (string.IsNullOrWhiteSpace(category))
                return ContainsIgnoreCase(UiText.Get("editor.detail.uncategorized"), query);
            if (IsKnownCategory(category))
                return ContainsIgnoreCase(CategoryLabel(category), query);

            return ContainsTemplateParts(UiText.Get("category.unknown.label"), query)
                || ContainsIgnoreCase(category, query);
        }

        public static bool EntryDescriptionContains(TextEntry entry, string query)
        {
            if (entry == null)
                return false;

            if (StringComparer.Ordinal.Equals(entry.Id.SourceId, "game"))
            {
                return CategoryDescriptionContains(entry.Category, query)
                    || entry.IsFallback && ContainsIgnoreCase(UiText.Get("catalog.detail.game_fallback"), query)
                    || entry.IsSmart && ContainsIgnoreCase(UiText.Get("catalog.detail.game_smart"), query)
                    || ContainsIgnoreCase(UiText.Get("catalog.detail.game_source"), query);
            }

            if (StringComparer.Ordinal.Equals(entry.Id.TableId, "Localization"))
            {
                return ContainsIgnoreCase(UiText.Get("catalog.detail.mod_usage"), query)
                    || entry.IsFallback && ContainsIgnoreCase(UiText.Get("catalog.detail.mod_fallback"), query);
            }

            return ContainsIgnoreCase(entry.Description, query);
        }

        private static bool CategoryDescriptionContains(string category, string query)
        {
            if (string.IsNullOrWhiteSpace(category))
                return ContainsIgnoreCase(UiText.Get("category.all.description"), query);
            if (IsKnownCategory(category))
                return ContainsIgnoreCase(CategoryDescription(category), query);

            return ContainsTemplateParts(UiText.Get("category.unknown.description"), query)
                || ContainsIgnoreCase(category, query);
        }

        private static bool IsKnownCategory(string category)
        {
            switch (category)
            {
                case "ClientName":
                case "Dialogue":
                case "PhoneDialogue":
                case "Item":
                case "Interactable":
                case "Hint":
                case "MainMenu":
                case "UI":
                case "Mechanic":
                case "NetworkUpgrade":
                case "NewGame":
                case "PerkTable":
                case "RepPerkTable":
                case "Tutorial":
                case "mod-localization":
                    return true;
                default:
                    return false;
            }
        }

        private static bool ContainsTemplateParts(string template, string query)
        {
            int placeholder = template.IndexOf("{0}", StringComparison.Ordinal);
            if (placeholder < 0)
                return ContainsIgnoreCase(template, query);

            return template.IndexOf(query, 0, placeholder, StringComparison.OrdinalIgnoreCase) >= 0
                || template.IndexOf(query, placeholder + 3, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ContainsIgnoreCase(string text, string query)
        {
            return !string.IsNullOrEmpty(text)
                && text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static string EntryDescription(TextEntry entry)
        {
            if (entry == null)
                return string.Empty;

            if (StringComparer.Ordinal.Equals(entry.Id.SourceId, "game"))
            {
                string description = UiText.Format("catalog.detail.game_base",
                    entry.TableName, entry.KeyName, entry.Id.Key, CategoryDescription(entry.Category));
                if (entry.IsFallback)
                    description += "\n" + UiText.Get("catalog.detail.game_fallback");
                if (entry.IsSmart)
                    description += "\n" + UiText.Get("catalog.detail.game_smart");
                return description + "\n" + UiText.Get("catalog.detail.game_source");
            }

            if (StringComparer.Ordinal.Equals(entry.Id.TableId, "Localization"))
            {
                string description = UiText.Format("catalog.detail.mod_base",
                    SourceName(entry), entry.Id.SourceId, entry.SourceVersion, entry.KeyName);
                description += "\n" + UiText.Format("catalog.detail.mod_usage");
                if (entry.IsFallback)
                    description += "\n" + UiText.Get("catalog.detail.mod_fallback");
                return description;
            }

            return entry.Description ?? string.Empty;
        }

        private static string UnknownTableName(string category)
        {
            return category != null && category.StartsWith("table:", StringComparison.Ordinal)
                ? category.Substring("table:".Length)
                : category;
        }

        private static string NormalizeLocaleForLookup(string code)
        {
            return string.IsNullOrWhiteSpace(code)
                ? string.Empty
                : code.Trim().Replace("-", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
        }

        private static string GetNativeCultureName(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return null;

            try
            {
                string cultureCode = code.Trim().Replace('_', '-');
                return CultureInfo.GetCultureInfo(cultureCode).NativeName;
            }
            catch (CultureNotFoundException)
            {
                return null;
            }
        }
    }
}
