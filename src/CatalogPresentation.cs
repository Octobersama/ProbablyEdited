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

            return string.IsNullOrWhiteSpace(code) ? "未知语言" : code;
        }

        public static string CategoryDescription(string category)
        {
            if (string.IsNullOrWhiteSpace(category))
                return "全部分类：显示当前语言目录中的所有文本表条目。";

            switch (category)
            {
                case "顾客 / 姓名":
                case "ClientName":
                    return "顾客姓名与称呼，来源于 ClientName 字符串表。";
                case "顾客 / 对话":
                case "Dialogue":
                    return "顾客交谈、报价等对白，来源于 Dialogue 字符串表。";
                case "电话 / 对话":
                case "PhoneDialogue":
                    return "电话中的对话文本，来源于 PhoneDialogue 字符串表。";
                case "物品":
                case "Item":
                    return "物品名称、描述等内容，来源于 Item 字符串表。";
                case "场景 / 交互":
                case "Interactable":
                    return "场景对象名称及交互提示，来源于 Interactable 字符串表。";
                case "提示":
                case "Hint":
                    return "游戏中的提示文本，来源于 Hint 字符串表。";
                case "界面 / 主菜单":
                case "MainMenu":
                    return "主菜单的按钮、标签和说明，来源于 MainMenu 字符串表。";
                case "界面 / 游戏界面":
                case "UI":
                    return "游戏界面的标签、按钮和消息，来源于 UI 字符串表。";
                case "经营 / 机制":
                case "Mechanic":
                    return "经营与游戏机制相关文本，来源于 Mechanic 字符串表。";
                case "成长 / 人脉升级":
                case "NetworkUpgrade":
                    return "人脉网络升级相关文本，来源于 NetworkUpgrade 字符串表。";
                case "新游戏":
                case "NewGame":
                    return "新游戏流程相关文本，来源于 NewGame 字符串表。";
                case "成长 / 专长":
                case "PerkTable":
                    return "角色专长相关文本，来源于 PerkTable 字符串表。";
                case "成长 / 声望专长":
                case "RepPerkTable":
                    return "声望专长相关文本，来源于 RepPerkTable 字符串表。";
                case "教程":
                case "Tutorial":
                    return "教程与操作引导文本，来源于 Tutorial 字符串表。";
                case "模组文本":
                    return "已启用模组通过官方 Localization 字典提供的文本条目；来源属于模组文本，而非原始游戏字符串表。";
                default:
                    return "这是原始字符串表中的未识别分类“" + category + "”。当前目录按该表名保留条目；请结合键名和原文判断具体用途。";
            }
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
