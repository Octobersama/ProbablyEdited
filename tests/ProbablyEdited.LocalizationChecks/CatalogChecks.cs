using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using ProbablyEdited;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;

internal static class CatalogChecks
{
    private const string ModId = "catalog-check";
    private const string GreetingKey = "greeting";
    private static readonly Guid TableId = Guid.Parse("8f024743-91e5-48c7-8e57-b9551c14e2a7");

    public static void Run(ref int failures)
    {
        UiText.Configure(key => key == "catalog.error.localization_alias_conflict"
            ? "Conflicting aliases for {0}."
            : key, CultureInfo.InvariantCulture);
        try
        {
            Program.Run("regional aliases keep the first baseline across preset switches", LoadsSharedAliasesFromOneBaseline, ref failures);
            Program.Run("shared alias apply and restore work in either load order", RestoresSelectedAliasAfterEarlierAlias, ref failures);
            Program.Run("disposing the catalog restores a shared alias baseline", DisposeRestoresSharedBaseline, ref failures);
            Program.Run("missing keys and newly created locale dictionaries are cleaned up", RestoresMissingKeysAndCreatedLocales, ref failures);
            Program.Run("reloading a created locale preserves its English baselines and overrides", ReloadsCreatedLocaleWithoutLosingBaseline, ref failures);
            Program.Run("disposing after a created-locale reload removes only that locale", DisposeAfterReloadingCreatedLocale, ref failures);
            Program.Run("independent locale dictionaries remain independent", KeepsIndependentLocalesSeparate, ref failures);
            Program.Run("identical alias overrides are accepted and counted per text id", AllowsEqualAliasOverrides, ref failures);
            Program.Run("alias conflicts fail before game or mod text is written", RejectsConflictsBeforeWriting, ref failures);
        }
        finally
        {
            UiText.Reset();
            ModLoader.Mods.Clear();
            LocalizationSettings.Reset();
        }
    }

    private static void LoadsSharedAliasesFromOneBaseline()
    {
        using (Fixture fixture = CreateFixture(Localization(("en-US", GreetingKey, "Original greeting"))))
        {
            Load(fixture.Catalog, "en");
            TextEntry english = FindEntry(fixture.Catalog, "en", GreetingKey);
            Program.Equal("en", english.Id.Locale);
            Program.Equal(true, ReferenceEquals(fixture.Mod.Localization["en"], fixture.Mod.Localization["en-US"]));
            Program.Equal("Original greeting", english.OriginalText);
            Program.Equal(1, fixture.Catalog.Apply(Overrides((english.Id, "Preset A"))));

            Load(fixture.Catalog, "en-US");
            TextEntry regional = FindEntry(fixture.Catalog, "en-US", GreetingKey);
            Program.Equal("en-US", regional.Id.Locale);
            Program.Equal("Original greeting", regional.OriginalText);
            Program.Equal("Preset A", fixture.Mod.Localization["en-US"][GreetingKey]);

            Program.Equal(1, fixture.Catalog.Apply(Overrides((regional.Id, "Preset B"))));
            Program.Equal("Preset B", fixture.Mod.Localization["en"][GreetingKey]);
            Program.Equal(0, fixture.Catalog.Apply(EmptyOverrides()));
            Program.Equal("Original greeting", fixture.Mod.Localization["en-US"][GreetingKey]);
        }
    }

    private static void RestoresSelectedAliasAfterEarlierAlias()
    {
        using (Fixture fixture = CreateFixture(Localization(("en-US", GreetingKey, "Original greeting"))))
        {
            Load(fixture.Catalog, "en");
            Load(fixture.Catalog, "en-US");
            TextEntry english = FindEntry(fixture.Catalog, "en", GreetingKey);
            TextEntry regional = FindEntry(fixture.Catalog, "en-US", GreetingKey);

            fixture.Catalog.Apply(Overrides((regional.Id, "Earlier preset")));
            fixture.Catalog.Apply(Overrides((english.Id, "Selected preset")));

            Program.Equal("Selected preset", fixture.Mod.Localization["en-US"][GreetingKey]);
        }
    }

    private static void DisposeRestoresSharedBaseline()
    {
        Fixture fixture = CreateFixture(Localization(("en-US", GreetingKey, "Original greeting")));
        Load(fixture.Catalog, "en");
        Load(fixture.Catalog, "en-US");
        TextEntry regional = FindEntry(fixture.Catalog, "en-US", GreetingKey);
        fixture.Catalog.Apply(Overrides((regional.Id, "Temporary override")));

        fixture.Catalog.Dispose();

        Program.Equal("Original greeting", fixture.Mod.Localization["en-US"][GreetingKey]);
        fixture.Dispose();
    }

    private static void RestoresMissingKeysAndCreatedLocales()
    {
        Dictionary<string, Dictionary<string, string>> localization = Localization(
            ("en-US", GreetingKey, "English greeting"),
            ("en-US", "farewell", "English farewell"));
        localization["de-DE"] = new Dictionary<string, string>(StringComparer.Ordinal);

        using (Fixture fixture = CreateFixture(localization))
        {
            Load(fixture.Catalog, "de");
            TextEntry germanGreeting = FindEntry(fixture.Catalog, "de", GreetingKey);
            Program.Equal("English greeting", germanGreeting.OriginalText);
            fixture.Catalog.Apply(Overrides((germanGreeting.Id, "Deutscher Gruß")));
            Program.Equal("Deutscher Gruß", fixture.Mod.Localization["de-DE"][GreetingKey]);
            fixture.Catalog.Apply(EmptyOverrides());
            Program.Equal(false, fixture.Mod.Localization["de-DE"].ContainsKey(GreetingKey));

            Load(fixture.Catalog, "it");
            TextEntry italianGreeting = FindEntry(fixture.Catalog, "it", GreetingKey);
            TextEntry italianFarewell = FindEntry(fixture.Catalog, "it", "farewell");
            fixture.Catalog.Apply(Overrides(
                (italianGreeting.Id, "Ciao"),
                (italianFarewell.Id, "Arrivederci")));
            Program.Equal(true, fixture.Mod.Localization.ContainsKey("it"));
            fixture.Catalog.Apply(EmptyOverrides());
            Program.Equal(false, fixture.Mod.Localization.ContainsKey("it"));
        }
    }

    private static void ReloadsCreatedLocaleWithoutLosingBaseline()
    {
        using (Fixture fixture = CreateFixture(Localization(
            ("en-US", GreetingKey, "English greeting"),
            ("en-US", "farewell", "English farewell"))))
        {
            Load(fixture.Catalog, "it");
            TextEntry greeting = FindEntry(fixture.Catalog, "it", GreetingKey);
            TextEntry farewell = FindEntry(fixture.Catalog, "it", "farewell");
            Program.Equal(2, fixture.Catalog.Apply(Overrides(
                (greeting.Id, "First greeting"),
                (farewell.Id, "First farewell"))));

            Load(fixture.Catalog, "it");
            greeting = FindEntry(fixture.Catalog, "it", GreetingKey);
            farewell = FindEntry(fixture.Catalog, "it", "farewell");
            Program.Equal("English greeting", greeting.OriginalText);
            Program.Equal("English farewell", farewell.OriginalText);
            Program.Equal("First greeting", fixture.Mod.Localization["it"][GreetingKey]);
            Program.Equal("First farewell", fixture.Mod.Localization["it"]["farewell"]);

            Program.Equal(2, fixture.Catalog.Apply(Overrides(
                (greeting.Id, "Second greeting"),
                (farewell.Id, "Second farewell"))));
            Program.Equal("Second greeting", fixture.Mod.Localization["it"][GreetingKey]);
            Program.Equal("Second farewell", fixture.Mod.Localization["it"]["farewell"]);
            Program.Equal(0, fixture.Catalog.Apply(EmptyOverrides()));
            Program.Equal(false, fixture.Mod.Localization.ContainsKey("it"));
        }
    }

    private static void DisposeAfterReloadingCreatedLocale()
    {
        using (Fixture fixture = CreateFixture(Localization(
            ("en-US", GreetingKey, "English greeting"),
            ("en-US", "farewell", "English farewell"))))
        {
            Load(fixture.Catalog, "it");
            TextEntry greeting = FindEntry(fixture.Catalog, "it", GreetingKey);
            TextEntry farewell = FindEntry(fixture.Catalog, "it", "farewell");
            fixture.Catalog.Apply(Overrides(
                (greeting.Id, "Temporary greeting"),
                (farewell.Id, "Temporary farewell")));

            Load(fixture.Catalog, "it");
            greeting = FindEntry(fixture.Catalog, "it", GreetingKey);
            Program.Equal("English greeting", greeting.OriginalText);
            Program.Equal("Temporary greeting", fixture.Mod.Localization["it"][GreetingKey]);
            Program.Equal(true, ReferenceEquals(fixture.Mod.Localization["en"], fixture.Mod.Localization["en-US"]));

            fixture.Catalog.Dispose();

            Program.Equal(false, fixture.Mod.Localization.ContainsKey("it"));
            Program.Equal("English greeting", fixture.Mod.Localization["en-US"][GreetingKey]);
            Program.Equal("English farewell", fixture.Mod.Localization["en-US"]["farewell"]);
        }
    }

    private static void KeepsIndependentLocalesSeparate()
    {
        using (Fixture fixture = CreateFixture(Localization(
            ("en-US", GreetingKey, "English baseline"),
            ("de-DE", GreetingKey, "German baseline"))))
        {
            Load(fixture.Catalog, "en");
            Load(fixture.Catalog, "de");
            TextEntry english = FindEntry(fixture.Catalog, "en", GreetingKey);
            TextEntry german = FindEntry(fixture.Catalog, "de", GreetingKey);

            fixture.Catalog.Apply(Overrides(
                (english.Id, "English override"),
                (german.Id, "German override")));
            Program.Equal("English override", fixture.Mod.Localization["en-US"][GreetingKey]);
            Program.Equal("German override", fixture.Mod.Localization["de-DE"][GreetingKey]);

            fixture.Catalog.Apply(Overrides((english.Id, "Selected English override")));
            Program.Equal("Selected English override", fixture.Mod.Localization["en-US"][GreetingKey]);
            Program.Equal("German baseline", fixture.Mod.Localization["de-DE"][GreetingKey]);
        }
    }

    private static void AllowsEqualAliasOverrides()
    {
        using (Fixture fixture = CreateFixture(Localization(("en-US", GreetingKey, "Original greeting"))))
        {
            Load(fixture.Catalog, "en-US");
            Load(fixture.Catalog, "en");
            TextEntry english = FindEntry(fixture.Catalog, "en", GreetingKey);
            TextEntry regional = FindEntry(fixture.Catalog, "en-US", GreetingKey);
            Program.Equal("Original greeting", english.OriginalText);
            Program.Equal("Original greeting", regional.OriginalText);

            Program.Equal(2, fixture.Catalog.Apply(Overrides(
                (english.Id, "Same replacement"),
                (regional.Id, "Same replacement"))));
            Program.Equal("Same replacement", fixture.Mod.Localization["en"][GreetingKey]);
            fixture.Catalog.Apply(EmptyOverrides());
            Program.Equal("Original greeting", fixture.Mod.Localization["en-US"][GreetingKey]);
        }
    }

    private static void RejectsConflictsBeforeWriting()
    {
        using (Fixture fixture = CreateFixture(Localization(("en-US", GreetingKey, "Original greeting"))))
        {
            Load(fixture.Catalog, "en");
            Load(fixture.Catalog, "en-US");
            TextEntry english = FindEntry(fixture.Catalog, "en", GreetingKey);
            TextEntry regional = FindEntry(fixture.Catalog, "en-US", GreetingKey);
            TextEntry game = FindGameEntry(fixture.Catalog, "en");

            Program.Equal(2, fixture.Catalog.Apply(Overrides(
                (english.Id, "Prior mod override"),
                (game.Id, "Prior game override"))));
            InvalidOperationException conflict = null;
            try
            {
                fixture.Catalog.Apply(Overrides(
                    (english.Id, "Conflicting native value"),
                    (regional.Id, "Conflicting regional value"),
                    (game.Id, "Must not be written")));
            }
            catch (InvalidOperationException error)
            {
                conflict = error;
            }

            Program.Equal("Conflicting aliases for greeting.", conflict?.Message);
            Program.Equal("Prior mod override", fixture.Mod.Localization["en-US"][GreetingKey]);
            Program.Equal("Prior game override", fixture.GameTable.GetEntry(1).Value);
        }
    }

    private static Fixture CreateFixture(Dictionary<string, Dictionary<string, string>> localization)
    {
        LocalizationSettings.Reset();
        ModLoader.Mods.Clear();
        NativeGameLanguageChoices.SetChoices(("en", "English"));

        var locale = new Locale("en");
        LocalizationSettings.AvailableLocales.Locales.Add(locale);
        var gameTable = new StringTable(TableId, "TestTable");
        gameTable.SharedData.Entries.Add(new SharedTableEntry(1, "game.key"));
        gameTable.AddEntry(1, "Game baseline");
        LocalizationSettings.StringDatabase.SetTables(locale, new List<StringTable> { gameTable });

        ModLocalizationAliases.Register(localization);
        var mod = new LoadedMod
        {
            Manifest = new ModManifest { ID = ModId, Name = "Catalog Check", ModVersion = "1" },
            Localization = localization
        };
        ModLoader.Mods.Add(mod);

        var catalog = new GameTextCatalog();
        catalog.RefreshLocales();
        return new Fixture(catalog, mod, gameTable);
    }

    private static Dictionary<string, Dictionary<string, string>> Localization(
        params (string Locale, string Key, string Value)[] entries)
    {
        var localization = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (!localization.TryGetValue(entry.Locale, out var dictionary))
            {
                dictionary = new Dictionary<string, string>(StringComparer.Ordinal);
                localization.Add(entry.Locale, dictionary);
            }
            dictionary.Add(entry.Key, entry.Value);
        }
        return localization;
    }

    private static TextEntry FindEntry(GameTextCatalog catalog, string locale, string key)
    {
        foreach (TextEntry entry in catalog.Entries(locale))
            if (entry.Id.SourceId == ModId && entry.Id.Key == key)
                return entry;
        throw new InvalidOperationException("Could not find mod text " + locale + ":" + key);
    }

    private static TextEntry FindGameEntry(GameTextCatalog catalog, string locale)
    {
        foreach (TextEntry entry in catalog.Entries(locale))
            if (entry.Id.SourceId == GameTextCatalog.GameSourceId)
                return entry;
        throw new InvalidOperationException("Could not find a game-table entry for " + locale);
    }

    private static void Load(GameTextCatalog catalog, string locale)
    {
        IEnumerator operation = catalog.Load(locale);
        while (operation.MoveNext()) { }
    }

    private static Dictionary<TextId, string> Overrides(params (TextId Id, string Text)[] entries)
    {
        var result = new Dictionary<TextId, string>();
        foreach (var entry in entries)
            result.Add(entry.Id, entry.Text);
        return result;
    }

    private static Dictionary<TextId, string> EmptyOverrides() => new Dictionary<TextId, string>();

    private sealed class Fixture : IDisposable
    {
        public Fixture(GameTextCatalog catalog, LoadedMod mod, StringTable gameTable)
        {
            Catalog = catalog;
            Mod = mod;
            GameTable = gameTable;
        }

        public GameTextCatalog Catalog { get; }
        public LoadedMod Mod { get; }
        public StringTable GameTable { get; }

        public void Dispose() => Catalog.Dispose();
    }
}