using System;
using System.Collections.Generic;

namespace ProbablyEdited
{
    /// <summary>
    /// Maps region-tagged resources to the locale keys used by the game APIs.
    /// Both keys retain the same loaded dictionary; this is an ongoing API mapping,
    /// not a legacy-file compatibility shim.
    /// </summary>
    internal static class ModLocalizationAliases
    {
        private static readonly (string ResourceCode, string NativeCode)[] RegionToNativeMappings =
        {
            ("en-US", "en"),
            ("zh-CN", "zh"),
            ("de-DE", "de"),
            ("es-ES", "es"),
            ("fr-FR", "fr"),
            ("ja-JP", "ja"),
            ("pl-PL", "pl"),
            ("ru-RU", "ru")
        };

        public static void Register(IDictionary<string, Dictionary<string, string>> localization)
        {
            if (localization == null) throw new ArgumentNullException(nameof(localization));

            foreach (var mapping in RegionToNativeMappings)
            {
                if (localization.TryGetValue(mapping.ResourceCode, out Dictionary<string, string> dictionary)
                    && dictionary != null)
                    localization[mapping.NativeCode] = dictionary;
            }
        }
    }
}
