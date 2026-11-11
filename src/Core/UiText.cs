using System;
using System.Globalization;

namespace ProbablyEdited
{
    internal static class UiText
    {
        private static Func<string, string> lookup;
        private static CultureInfo culture = CultureInfo.InvariantCulture;

        public static void Configure(Func<string, string> source, CultureInfo displayCulture)
        {
            lookup = source ?? throw new ArgumentNullException(nameof(source));
            culture = displayCulture ?? CultureInfo.InvariantCulture;
        }

        public static string Get(string key)
        {
            if (lookup == null)
                throw new InvalidOperationException("The mod UI localization source is not initialized.");
            return lookup(key) ?? key;
        }

        public static string Format(string key, params object[] arguments)
        {
            return string.Format(culture, Get(key), arguments);
        }

        public static void Reset()
        {
            lookup = null;
            culture = CultureInfo.InvariantCulture;
        }
    }
}
