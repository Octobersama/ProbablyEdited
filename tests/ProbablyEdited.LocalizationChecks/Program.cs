using System;
using System.Collections.Generic;
using System.Globalization;
using ProbablyEdited;

internal static class Program
{
    private static int Main()
    {
        int failures = 0;
        Run("automatic UI language follows game while explicit choice remains independent", FollowsGameOnlyWhenRequested, ref failures);
        Run("missing translations fall back without replacing intentional empty values", PreservesEmptyValues, ref failures);
        Run("game locale aliases resolve to their existing mod dictionaries", ResolvesAliases, ref failures);
        Run("UI languages are limited to native choices plus Taiwan Traditional", LimitsLanguagesToNativePickerAndTaiwan, ref failures);
        Run("live official-dictionary overrides and restoration are visible", ObservesLiveOverrides, ref failures);
        Run("UI formatting uses its own culture without changing process culture", KeepsFormattingIndependent, ref failures);
        Console.WriteLine(failures == 0 ? "All 6 localization checks passed." : failures + " localization check(s) failed.");
        return failures == 0 ? 0 : 1;
    }

    private static Dictionary<string, Dictionary<string, string>> Tables()
    {
        return new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal)
        {
            ["en"] = new Dictionary<string, string> { ["meta.native_name"] = "English", ["label"] = "English value", ["only_en"] = "Fallback" },
            ["zh"] = new Dictionary<string, string> { ["meta.native_name"] = "简体中文", ["label"] = "简体内容" },
            ["ru"] = new Dictionary<string, string> { ["meta.native_name"] = "Русский", ["label"] = "Содержимое" },
            ["de"] = new Dictionary<string, string> { ["meta.native_name"] = "Deutsch", ["label"] = "Inhalt" },
            ["ja"] = new Dictionary<string, string> { ["meta.native_name"] = "日本語", ["label"] = "内容" },
            ["fr"] = new Dictionary<string, string> { ["meta.native_name"] = "Français", ["label"] = "Contenu" },
            ["es"] = new Dictionary<string, string> { ["meta.native_name"] = "Español", ["label"] = "Contenido" },
            ["pl"] = new Dictionary<string, string> { ["meta.native_name"] = "Polski", ["label"] = "Treść" },
            ["zh-TW"] = new Dictionary<string, string> { ["meta.native_name"] = "正體中文（臺灣）", ["label"] = "臺灣內容" },
            ["ko"] = new Dictionary<string, string> { ["meta.native_name"] = "한국어", ["label"] = "한국어 내용" },
            ["it"] = new Dictionary<string, string> { ["meta.native_name"] = "Italiano", ["label"] = "Contenuto" },
            ["ptBR"] = new Dictionary<string, string> { ["meta.native_name"] = "Português (Brasil)", ["label"] = "Conteúdo" },
            ["x-custom"] = new Dictionary<string, string> { ["meta.native_name"] = "Future UI language", ["label"] = "Custom value" }
        };
    }

    private static string[] NativePickerLanguages()
    {
        return new[] { "en", "zh", "ru", "de", "ja", "fr", "es", "pl" };
    }

    private static UiLocaleCatalog Catalog(Dictionary<string, Dictionary<string, string>> tables = null)
    {
        return new UiLocaleCatalog(tables ?? Tables(), NativePickerLanguages());
    }

    private static void FollowsGameOnlyWhenRequested()
    {
        var catalog = Catalog();
        Equal("zh", catalog.ResolveLanguage(UiLocaleCatalog.FollowGame, "zh"));
        Equal("en", catalog.ResolveLanguage(UiLocaleCatalog.FollowGame, "en"));
        Equal("zh-TW", catalog.ResolveLanguage("zh-TW", "en"));
        Equal("zh-TW", catalog.ResolveLanguage("zh-TW", "zh"));
        Equal("zh", catalog.ResolveLanguage("ko", "zh"));
        Equal("en", catalog.ResolveLanguage("unavailable", "missing"));
    }

    private static void PreservesEmptyValues()
    {
        var tables = Tables();
        tables["zh"]["label"] = string.Empty;
        var catalog = Catalog(tables);
        Equal(string.Empty, catalog.Get("zh", "label"));
        Equal("Fallback", catalog.Get("zh", "only_en"));
        Equal("missing_key", catalog.Get("zh", "missing_key"));
    }

    private static void ResolvesAliases()
    {
        var catalog = Catalog();
        Equal("zh", catalog.ResolveLanguage(UiLocaleCatalog.FollowGame, "zh-Hans-CN"));
        Equal("zh-TW", catalog.ResolveLanguage(UiLocaleCatalog.FollowGame, "zh-Hant"));
        Equal("zh", catalog.ResolveLanguage("pt-BR", "zh"));
        Equal("en", catalog.ResolveLanguage(UiLocaleCatalog.FollowGame, "en-US"));
    }

    private static void LimitsLanguagesToNativePickerAndTaiwan()
    {
        var tables = Tables();
        var catalog = Catalog(tables);
        var languages = catalog.GetLanguages();
        string[] expectedCodes = { "zh", "zh-TW", "en", "de", "es", "fr", "ja", "pl", "ru" };
        Equal(expectedCodes.Length, languages.Count);
        for (int index = 0; index < expectedCodes.Length; index++)
            Equal(expectedCodes[index], languages[index].Code);

        Equal("简体中文", languages.Find(language => language.Code == "zh").NativeName);
        Equal("正體中文（臺灣）", languages.Find(language => language.Code == "zh-TW").NativeName);
        Equal(null, catalog.FindLanguage("ko"));
        Equal(null, catalog.FindLanguage("it"));
        Equal(null, catalog.FindLanguage("ptBR"));
        Equal(null, catalog.FindLanguage("x-custom"));
        Equal("한국어 내용", tables["ko"]["label"]);
        Equal("Custom value", tables["x-custom"]["label"]);
    }

    private static void ObservesLiveOverrides()
    {
        var tables = Tables();
        var catalog = Catalog(tables);
        string original = catalog.Get("zh-TW", "label");
        tables["zh-TW"]["label"] = "自訂文字";
        Equal("自訂文字", catalog.Get("zh-TW", "label"));
        tables["zh-TW"]["label"] = original;
        Equal("臺灣內容", catalog.Get("zh-TW", "label"));
        tables["zh-TW"].Remove("label");
        Equal("English value", catalog.Get("zh-TW", "label"));
    }

    private static void KeepsFormattingIndependent()
    {
        string originalCulture = CultureInfo.CurrentCulture.Name;
        UiText.Configure(_ => "{0:0.0}", CultureInfo.GetCultureInfo("fr"));
        try
        {
            Equal("12,5", UiText.Format("number", 12.5));
            Equal(originalCulture, CultureInfo.CurrentCulture.Name);
        }
        finally { UiText.Reset(); }
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException("Expected: " + expected + "; actual: " + actual);
    }

    private static void Run(string name, Action check, ref int failures)
    {
        try { check(); Console.WriteLine("PASS " + name); }
        catch (Exception error) { failures++; Console.WriteLine("FAIL " + name + ": " + error); }
    }
}
