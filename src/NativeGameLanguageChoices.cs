using System;
using System.Reflection;
using HarmonyLib;

namespace ProbablyEdited
{
    internal static class NativeGameLanguageChoices
    {
        private static readonly FieldInfo LocaleDropdown = AccessTools.Field(typeof(MainMenuUIController), "LOCALE_DROPDOWN");

        public static ValueTuple<string, string>[] GetChoices()
        {
            return LocaleDropdown?.GetValue(null) as ValueTuple<string, string>[];
        }
    }
}
