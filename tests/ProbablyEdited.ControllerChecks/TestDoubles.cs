using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ProbablyEdited;

namespace UnityEngine
{
}

namespace UnityEngine.Localization
{
    public sealed class Locale
    {
        public LocaleIdentifier Identifier { get; set; } = new LocaleIdentifier();
    }

    public sealed class LocaleIdentifier
    {
        public string Code { get; set; } = "en";
    }
}

namespace UnityEngine.Localization.Settings
{
    using UnityEngine.Localization;

    public static class LocalizationSettings
    {
        public static event Action<Locale> SelectedLocaleChanged;
    }
}

namespace ProbablyEdited
{
    public sealed class ModLog
    {
        public void Log(string message) { }
        public void Error(string message) { }
    }

    public static class ModEntry
    {
        public const string ModId = "probably_edited_test";
    }

    public static class PlayerStore
    {
        public const string VERSION = "100-Rev13";
    }

    public sealed class LoadedMod
    {
    }

    public static class ModHook
    {
        public static event Action<LoadedMod> OnModAssetsLoaded;
    }

    public static class ModHelper
    {
        private static string _dataRoot;
        private static int _successfulWritesBeforeFailure;

        public static string FailWritePath { get; private set; }
        public static bool FailAfterPartialWrite { get; private set; }

        public static void Configure(string dataRoot)
        {
            _dataRoot = Path.GetFullPath(dataRoot);
            Directory.CreateDirectory(_dataRoot);
            FailWritePath = null;
            FailAfterPartialWrite = false;
            _successfulWritesBeforeFailure = 0;
        }

        public static void FailNextWrite(string relativePath, bool afterPartialWrite)
        {
            FailWriteAfterSuccessfulMatches(relativePath, 0, afterPartialWrite);
        }

        public static void FailWriteAfterSuccessfulMatches(string relativePath, int successfulMatches, bool afterPartialWrite)
        {
            if (successfulMatches < 0)
                throw new ArgumentOutOfRangeException(nameof(successfulMatches));

            FailWritePath = relativePath.Replace('\\', '/');
            FailAfterPartialWrite = afterPartialWrite;
            _successfulWritesBeforeFailure = successfulMatches;
        }

        public static string GetModDataFolder(string modId)
        {
            return _dataRoot;
        }

        public static bool ModDataFileExists(string modId, string relativePath)
        {
            return File.Exists(Resolve(relativePath));
        }

        public static string ReadModDataFileText(string modId, string relativePath)
        {
            return File.ReadAllText(Resolve(relativePath));
        }

        public static void WriteModDataFileText(string modId, string relativePath, string text)
        {
            string path = Resolve(relativePath);
            string normalized = Path.GetRelativePath(_dataRoot, path).Replace('\\', '/');
            bool matchesFailure = StringComparer.OrdinalIgnoreCase.Equals(normalized, FailWritePath);
            if (matchesFailure && _successfulWritesBeforeFailure > 0)
            {
                _successfulWritesBeforeFailure--;
            }
            else if (matchesFailure)
            {
                bool writePartial = FailAfterPartialWrite;
                FailWritePath = null;
                FailAfterPartialWrite = false;
                _successfulWritesBeforeFailure = 0;
                if (writePartial)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllText(path, "partial write", Encoding.UTF8);
                }

                throw new IOException("Injected storage write failure.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text, Encoding.UTF8);
        }

        public static void DeleteModDataFile(string modId, string relativePath)
        {
            File.Delete(Resolve(relativePath));
        }

        public static List<string> ListModDataFiles(string modId)
        {
            var files = new List<string>();
            if (!Directory.Exists(_dataRoot))
                return files;

            foreach (string path in Directory.GetFiles(_dataRoot, "*", SearchOption.AllDirectories))
                files.Add(Path.GetRelativePath(_dataRoot, path).Replace('\\', '/'));
            return files;
        }

        public static void StartCoroutine(string modId, IEnumerator routine)
        {
            try
            {
                while (routine.MoveNext()) { }
            }
            finally
            {
                (routine as IDisposable)?.Dispose();
            }
        }

        public static void StopAllCoroutines(string modId) { }

        private static string Resolve(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath) || Path.IsPathRooted(relativePath))
                throw new ArgumentException("A relative data-file path is required.", nameof(relativePath));

            string root = Path.GetFullPath(_dataRoot);
            string candidate = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            string prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The data-file path must stay inside the test store.", nameof(relativePath));

            return candidate;
        }
    }

    public sealed class GameTextCatalog : IDisposable
    {
        private static readonly IReadOnlyList<string> LocaleCodes = Array.AsReadOnly(new[] { "en", "fr", "ko" });
        private static readonly IReadOnlyList<string> LocaleNames = Array.AsReadOnly(new[] { "English", "Français", "한국어" });
        private static TextEntry[] _catalogEntries = Array.Empty<TextEntry>();
        private static string _currentLocale = "en";
        private readonly HashSet<string> _loadedLocales = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<TextId, string> _appliedValues = new Dictionary<TextId, string>();
        private readonly List<string> _loadCalls = new List<string>();
        private readonly List<IReadOnlyDictionary<TextId, string>> _applySnapshots = new List<IReadOnlyDictionary<TextId, string>>();
        private readonly List<IReadOnlyList<string>> _loadedLocalesAtApply = new List<IReadOnlyList<string>>();

        public static GameTextCatalog Latest { get; private set; }
        public static void SetEntry(TextEntry entry) { SetEntries(new[] { entry }, "en"); }

        public static void SetEntries(IEnumerable<TextEntry> entries, string currentLocale = "en")
        {
            var values = new List<TextEntry>();
            foreach (TextEntry entry in entries)
                values.Add(entry);
            _catalogEntries = values.ToArray();
            _currentLocale = currentLocale ?? "en";
        }

        public IReadOnlyList<string> Codes => LocaleCodes;
        public IReadOnlyList<string> Names => LocaleNames;
        public string CurrentLocale => _currentLocale;
        public string Coverage(string code) { return "测试目录覆盖摘要"; }
        public IReadOnlyDictionary<TextId, string> AppliedValues => _appliedValues;
        public IReadOnlyList<string> LoadCalls => _loadCalls.AsReadOnly();
        public IReadOnlyList<IReadOnlyDictionary<TextId, string>> ApplySnapshots => _applySnapshots.AsReadOnly();
        public IReadOnlyList<IReadOnlyList<string>> LoadedLocalesAtApply => _loadedLocalesAtApply.AsReadOnly();
        public IReadOnlyCollection<string> LoadedLocales => Array.AsReadOnly(new List<string>(_loadedLocales).ToArray());
        public bool FailNextApplyAfterPartial { get; set; }

        public GameTextCatalog()
        {
            Latest = this;
        }

        public IEnumerator Initialize()
        {
            yield break;
        }

        public IEnumerator Load(string code)
        {
            _loadCalls.Add(code);
            _loadedLocales.Add(code);
            yield break;
        }

        public IReadOnlyList<TextEntry> Entries(string code)
        {
            if (!_loadedLocales.Contains(code))
                return Array.Empty<TextEntry>();

            var entries = new List<TextEntry>();
            foreach (TextEntry entry in _catalogEntries)
                if (StringComparer.Ordinal.Equals(code, entry.Id.Locale))
                    entries.Add(entry);
            return entries.AsReadOnly();
        }

        public int Apply(IReadOnlyDictionary<TextId, string> overrides)
        {
            _applySnapshots.Add(Snapshot(overrides));
            _loadedLocalesAtApply.Add(Array.AsReadOnly(new List<string>(_loadedLocales).ToArray()));

            if (FailNextApplyAfterPartial && !MatchesLoadedBindings(overrides))
            {
                FailNextApplyAfterPartial = false;
                ApplyOneLoadedOverride(overrides);
                throw new InvalidOperationException("Injected runtime apply failure.");
            }

            int applied = 0;
            foreach (TextEntry entry in _catalogEntries)
            {
                if (!_loadedLocales.Contains(entry.Id.Locale))
                    continue;

                if (overrides.TryGetValue(entry.Id, out string text))
                {
                    _appliedValues[entry.Id] = text;
                    applied++;
                }
                else
                {
                    _appliedValues.Remove(entry.Id);
                }
            }

            return applied;
        }

        private bool MatchesLoadedBindings(IReadOnlyDictionary<TextId, string> overrides)
        {
            foreach (TextEntry entry in _catalogEntries)
            {
                if (!_loadedLocales.Contains(entry.Id.Locale))
                    continue;

                bool expected = overrides.TryGetValue(entry.Id, out string value);
                bool actual = _appliedValues.TryGetValue(entry.Id, out string applied);
                if (expected != actual || (expected && !StringComparer.Ordinal.Equals(value, applied)))
                    return false;
            }

            return true;
        }

        private void ApplyOneLoadedOverride(IReadOnlyDictionary<TextId, string> overrides)
        {
            foreach (KeyValuePair<TextId, string> pair in overrides)
            {
                if (_loadedLocales.Contains(pair.Key.Locale) && Array.Exists(_catalogEntries, entry => entry.Id == pair.Key))
                {
                    _appliedValues[pair.Key] = pair.Value;
                    return;
                }
            }
        }

        private static IReadOnlyDictionary<TextId, string> Snapshot(IReadOnlyDictionary<TextId, string> source)
        {
            var copy = new Dictionary<TextId, string>();
            foreach (KeyValuePair<TextId, string> pair in source)
                copy.Add(pair.Key, pair.Value);
            return new System.Collections.ObjectModel.ReadOnlyDictionary<TextId, string>(copy);
        }

        public static void RefreshVisibleText() { }
        public void RefreshLocales() { }
        public static string Canonical(string code) { return code ?? "en"; }
        public void Dispose() { }
    }
}
