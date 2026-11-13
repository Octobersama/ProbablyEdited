using System;
using System.Collections.Generic;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.ResourceManagement.AsyncOperations;

internal sealed class LoadedMod
{
    public bool IsEnabled { get; set; } = true;
    public object Instance { get; set; } = new object();
    public ModManifest Manifest { get; set; }
    public string LoadError { get; set; }
    public Dictionary<string, Dictionary<string, string>> Localization { get; set; }
}

internal sealed class ModManifest
{
    public string ID { get; set; }
    public string Name { get; set; }
    public string ModVersion { get; set; }
}

internal static class ModLoader
{
    public static readonly List<LoadedMod> Mods = new List<LoadedMod>();
}

internal static class PlayerStore
{
    public const string VERSION = "catalog-check";
}

namespace ProbablyEdited
{
    internal static class NativeGameLanguageChoices
    {
        private static ValueTuple<string, string>[] choices = { ("en", "English") };

        public static ValueTuple<string, string>[] GetChoices() => choices;

        public static void SetChoices(params ValueTuple<string, string>[] values)
        {
            choices = values;
        }
    }

}

namespace UnityEngine
{
    public static class Resources
    {
        public static T[] FindObjectsOfTypeAll<T>() => Array.Empty<T>();
    }

    public sealed class GameObject
    {
        public Scene scene { get; } = new Scene();
    }

    public struct Scene
    {
        public bool IsValid() => true;
    }
}

namespace UnityEngine.Localization
{
    public sealed class Locale
    {
        public Locale(string code)
        {
            Identifier = new LocaleIdentifier(code);
        }

        public LocaleIdentifier Identifier { get; }
    }

    public readonly struct LocaleIdentifier
    {
        public LocaleIdentifier(string code)
        {
            Code = code;
        }

        public string Code { get; }
    }
}

namespace UnityEngine.Localization.Components
{
    public sealed class LocalizeStringEvent
    {
        public bool isActiveAndEnabled { get; set; }
        public UnityEngine.GameObject gameObject { get; } = new UnityEngine.GameObject();
        public void RefreshString() { }
    }
}

namespace UnityEngine.Localization.Tables
{
    public sealed class SharedTableEntry
    {
        public SharedTableEntry(long id, string key)
        {
            Id = id;
            Key = key;
        }

        public long Id { get; }
        public string Key { get; }
    }

    public sealed class SharedTableData
    {
        public Guid TableCollectionNameGuid { get; set; }
        public List<SharedTableEntry> Entries { get; } = new List<SharedTableEntry>();
    }

    public sealed class StringTableEntry
    {
        public string Value { get; set; }
        public bool IsSmart { get; set; }
    }

    public sealed class StringTable
    {
        private readonly Dictionary<long, StringTableEntry> entries = new Dictionary<long, StringTableEntry>();

        public StringTable(Guid tableId, string tableCollectionName)
        {
            SharedData = new SharedTableData { TableCollectionNameGuid = tableId };
            TableCollectionName = tableCollectionName;
        }

        public SharedTableData SharedData { get; }
        public string TableCollectionName { get; }

        public StringTableEntry GetEntry(long id) => entries.TryGetValue(id, out var value) ? value : null;

        public StringTableEntry AddEntry(long id, string text)
        {
            var entry = new StringTableEntry { Value = text };
            entries[id] = entry;
            return entry;
        }

        public void RemoveEntry(long id) => entries.Remove(id);
    }
}

namespace UnityEngine.ResourceManagement.AsyncOperations
{
    public enum AsyncOperationStatus
    {
        None,
        Succeeded,
        Failed
    }

    public class AsyncOperationHandle
    {
        public AsyncOperationStatus Status { get; set; }
        public Exception OperationException { get; set; }
        public bool IsValid() => !Released;
        internal bool Released { get; private set; }
        internal void Release() => Released = true;
    }

    public sealed class AsyncOperationHandle<T> : AsyncOperationHandle
    {
        public T Result { get; set; }
    }
}

namespace UnityEngine.AddressableAssets
{
    using UnityEngine.ResourceManagement.AsyncOperations;

    public sealed class ResourceManager
    {
        public void Acquire(AsyncOperationHandle handle) { }
    }

    public static class Addressables
    {
        public static ResourceManager ResourceManager { get; } = new ResourceManager();

        public static void Release(AsyncOperationHandle handle) => handle.Release();
    }
}

namespace UnityEngine.Localization.Settings
{
    public sealed class AvailableLocales
    {
        public List<Locale> Locales { get; } = new List<Locale>();
    }

    public sealed class StringDatabase
    {
        private readonly Dictionary<string, IList<StringTable>> tables = new Dictionary<string, IList<StringTable>>(StringComparer.Ordinal);

        public AsyncOperationHandle<IList<StringTable>> GetAllTables(Locale locale)
        {
            tables.TryGetValue(locale.Identifier.Code, out var result);
            return new AsyncOperationHandle<IList<StringTable>>
            {
                Status = AsyncOperationStatus.Succeeded,
                Result = result ?? new List<StringTable>()
            };
        }

        public void SetTables(Locale locale, IList<StringTable> value) => tables[locale.Identifier.Code] = value;
        public void Reset() => tables.Clear();
    }

    public static class LocalizationSettings
    {
        public static AvailableLocales AvailableLocales { get; } = new AvailableLocales();
        public static StringDatabase StringDatabase { get; } = new StringDatabase();
        public static Locale SelectedLocale { get; set; }
        public static AsyncOperationHandle InitializationOperation =>
            new AsyncOperationHandle { Status = AsyncOperationStatus.Succeeded };

        public static void Reset()
        {
            AvailableLocales.Locales.Clear();
            StringDatabase.Reset();
            SelectedLocale = null;
        }
    }
}