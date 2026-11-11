using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Localization;
using UnityEngine.Localization.Components;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace ProbablyEdited
{
    internal sealed class GameTextCatalog : IDisposable
    {
        private static readonly FieldInfo NativeLocaleChoices = AccessTools.Field(typeof(MainMenuUIController), "LOCALE_DROPDOWN");

        private readonly Dictionary<string, Locale> locales = new Dictionary<string, Locale>(StringComparer.Ordinal);
        private readonly Dictionary<string, IList<StringTable>> tables = new Dictionary<string, IList<StringTable>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<TextEntry>> catalogs = new Dictionary<string, List<TextEntry>>(StringComparer.Ordinal);
        private readonly Dictionary<TextId, Binding> bindings = new Dictionary<TextId, Binding>();
        private readonly List<AsyncOperationHandle> operations = new List<AsyncOperationHandle>();
        private readonly List<string> codes = new List<string>();
        private readonly List<string> names = new List<string>();

        public IReadOnlyList<string> Codes => codes;
        public IReadOnlyList<string> Names => names;
        public string CurrentLocale => Canonical(LocalizationSettings.SelectedLocale?.Identifier.Code ?? "en");

        public IEnumerator Initialize()
        {
            var initialization = LocalizationSettings.InitializationOperation;
            yield return initialization;
            if (initialization.Status != AsyncOperationStatus.Succeeded)
                throw new InvalidOperationException("游戏本地化初始化失败。请查看游戏日志后重新启动。");
            RefreshLocales();
        }

        public void RefreshLocales()
        {
            var choices = NativeLocaleChoices?.GetValue(null) as ValueTuple<string, string>[];
            if (choices == null || choices.Length == 0)
                throw new InvalidOperationException("无法读取游戏原生语言选择器，请核对游戏版本与模组兼容性。");

            codes.Clear();
            names.Clear();
            locales.Clear();
            foreach (var locale in LocalizationSettings.AvailableLocales.Locales)
                locales[Canonical(locale.Identifier.Code)] = locale;

            // Keep every loaded locale available for existing overrides, but mirror the game's picker.
            foreach (var choice in choices)
            {
                string code = Canonical(choice.Item1);
                codes.Add(code);
                names.Add(CatalogPresentation.NativeLanguageName(code, choice.Item2));
            }
        }

        public IEnumerator Load(string code)
        {
            code = Canonical(code);
            if (locales.TryGetValue(code, out var locale) && !tables.ContainsKey(code))
            {
                var request = LocalizationSettings.StringDatabase.GetAllTables(locale);
                // Localization releases GetAllTables on the next frame; retain our cached result until disposal.
                Addressables.ResourceManager.Acquire(request);
                operations.Add(request);
                yield return request;
                if (request.Status != AsyncOperationStatus.Succeeded)
                    throw new InvalidOperationException("无法读取语言 " + code + " 的文本表：" + request.OperationException?.Message);
                tables[code] = request.Result;
            }
            if (code != "en" && locales.TryGetValue("en", out var englishLocale) && !tables.ContainsKey("en"))
            {
                var request = LocalizationSettings.StringDatabase.GetAllTables(englishLocale);
                Addressables.ResourceManager.Acquire(request);
                operations.Add(request);
                yield return request;
                if (request.Status != AsyncOperationStatus.Succeeded)
                    throw new InvalidOperationException("无法读取英语回退文本表：" + request.OperationException?.Message);
                tables["en"] = request.Result;
            }

            var result = new List<TextEntry>();
            if (tables.TryGetValue(code, out var localizedTables))
            {
                foreach (var table in localizedTables)
                {
                    if (table == null || table.SharedData == null) continue;
                    string tableId = table.SharedData.TableCollectionNameGuid.ToString("N");
                    StringTable fallback = FindEnglishTable(tableId);
                    foreach (var key in table.SharedData.Entries)
                    {
                        var id = new TextId("game", code, tableId, key.Id.ToString(CultureInfo.InvariantCulture));
                        if (!bindings.TryGetValue(id, out var binding))
                        {
                            var value = table.GetEntry(key.Id);
                            var fallbackValue = fallback?.GetEntry(key.Id);
                            bool isFallback = value == null || string.IsNullOrEmpty(value.Value);
                            string fallbackText = Baseline(new TextId("game", "en", tableId, id.Key), fallbackValue?.Value ?? key.Key);
                            string original = isFallback ? fallbackText : value.Value;
                            bool smart = isFallback ? fallbackValue?.IsSmart ?? value?.IsSmart ?? false : value.IsSmart;
                            string description = "字符串表：" + table.TableCollectionName + "\n键：" + key.Key
                                + "\n数值键：" + key.Id + "\n" + CategoryHelp(table.TableCollectionName)
                                + (isFallback ? "\n本语言无翻译，参考显示英语或键名；恢复原文会恢复游戏原有回退。" : "")
                                + (smart ? "\nSmart String：保留变量、条件与复数格式结构。" : "")
                                + "\n来源为游戏本地化数据库；注册进此数据库的第三方表不提供可靠的作者标记。";
                            var entry = new TextEntry(id, "游戏文本表", PlayerStore.VERSION, table.TableCollectionName,
                                key.Key, Category(table.TableCollectionName), original ?? "", isFallback, smart, description);
                            binding = new Binding(entry, table, key.Id, value?.Value, value != null, value?.IsSmart ?? false, smart);
                            bindings.Add(id, binding);
                        }
                        result.Add(binding.Entry);
                    }
                }
            }

            foreach (var mod in ModLoader.Mods)
            {
                if (!IsActive(mod) || mod.Localization == null) continue;
                string targetCode = FindModLocale(mod, code) ?? code;
                mod.Localization.TryGetValue(targetCode, out var target);
                string englishCode = FindModLocale(mod, "en");
                Dictionary<string, string> english = null;
                if (englishCode != null) mod.Localization.TryGetValue(englishCode, out english);
                var keys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var language in mod.Localization.Values)
                    if (language != null) keys.UnionWith(language.Keys);
                foreach (string key in keys)
                {
                    var id = new TextId(mod.Manifest.ID, code, "Localization", key);
                    if (!bindings.TryGetValue(id, out var binding) || !ReferenceEquals(binding.Mod, mod))
                    {
                        string raw = null;
                        bool exists = target != null && target.TryGetValue(key, out raw);
                        bool fallback = !exists;
                        string original = raw;
                        if (!exists)
                        {
                            string fallbackText = english != null && english.TryGetValue(key, out var value) ? value : key;
                            original = Baseline(new TextId(mod.Manifest.ID, "en", "Localization", key), fallbackText);
                        }
                        string description = "模组：" + mod.Manifest.Name + "\nID：" + mod.Manifest.ID
                            + "\n版本：" + mod.Manifest.ModVersion + "\nCSV 键：" + key
                            + "\n具体显示位置由该模组决定；编号参数如 {0} 必须保留。"
                            + (fallback ? "\n目标语言未提供该键，参考显示英语或键名。" : "");
                        var entry = new TextEntry(id, mod.Manifest.Name ?? mod.Manifest.ID, mod.Manifest.ModVersion ?? "",
                            "Localization", key, "模组文本", original ?? "", fallback, false, description);
                        binding = new Binding(entry, mod, targetCode, raw, exists);
                        bindings[id] = binding;
                    }
                    result.Add(binding.Entry);
                }
            }
            result.Sort((left, right) =>
            {
                int order = StringComparer.Ordinal.Compare(left.Category, right.Category);
                if (order == 0) order = StringComparer.Ordinal.Compare(left.SourceName, right.SourceName);
                if (order == 0) order = StringComparer.Ordinal.Compare(left.TableName, right.TableName);
                return order == 0 ? StringComparer.Ordinal.Compare(left.KeyName, right.KeyName) : order;
            });
            catalogs[code] = result;
        }

        public IReadOnlyList<TextEntry> Entries(string code) => catalogs.TryGetValue(code, out var entries) ? entries : Array.Empty<TextEntry>();

        public string Coverage(string code)
        {
            var summary = new System.Text.StringBuilder();
            int tableCount = tables.TryGetValue(code, out var loaded) ? loaded.Count : 0;
            summary.Append("语言：").Append(code).Append("\n数据库字符串表：").Append(tableCount)
                .Append("\n可编辑条目：").Append(Entries(code).Count).Append("\n\n已启用模组的官方本地化条目：\n");
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var entry in Entries(code))
                if (entry.Id.TableId == "Localization")
                    counts[entry.Id.SourceId] = counts.TryGetValue(entry.Id.SourceId, out int count) ? count + 1 : 1;
            foreach (var mod in ModLoader.Mods)
            {
                if (!IsActive(mod)) continue;
                int count = counts.TryGetValue(mod.Manifest.ID, out int found) ? found : 0;
                summary.Append(mod.Manifest.Name ?? mod.Manifest.ID).Append("：").Append(count);
                if (count == 0) summary.Append("（没有可枚举条目）");
                summary.Append('\n');
            }
            summary.Append("\n范围：完整读取当前数据库中的字符串表、已启用模组的官方 CSV 字典及缺失翻译键。")
                .Append("\n代码硬编码、自建文本系统和动态拼接内容不可通用枚举。数据库中第三方注册表的作者来源可能不明；不据表名伪造来源。");
            return summary.ToString();
        }

        public int Apply(IReadOnlyDictionary<TextId, string> overrides)
        {
            int applied = 0;
            foreach (var binding in bindings.Values)
            {
                if (binding.Mod != null && !IsActive(binding.Mod)) continue;
                if (overrides.TryGetValue(binding.Entry.Id, out string text))
                {
                    binding.Apply(text);
                    applied++;
                }
                else binding.Restore();
            }
            return applied;
        }

        public static void RefreshVisibleText()
        {
            foreach (var localized in Resources.FindObjectsOfTypeAll<LocalizeStringEvent>())
                if (localized != null && localized.isActiveAndEnabled && localized.gameObject.scene.IsValid())
                    localized.RefreshString();
        }

        // ModHelper.GetLocalized uses the exact Locale.Identifier.Code dictionary key.
        public static string Canonical(string code) => code ?? "en";

        public void Dispose()
        {
            foreach (var binding in bindings.Values) binding.Restore();
            foreach (var operation in operations)
                if (operation.IsValid()) Addressables.Release(operation);
            operations.Clear();
            bindings.Clear();
        }

        private StringTable FindEnglishTable(string tableId)
        {
            if (!tables.TryGetValue("en", out var english)) return null;
            foreach (var table in english)
                if (table != null && table.SharedData.TableCollectionNameGuid.ToString("N") == tableId) return table;
            return null;
        }

        private string Baseline(TextId id, string current) =>
            bindings.TryGetValue(id, out var binding) ? binding.Entry.OriginalText : current;

        private static bool IsActive(LoadedMod mod) => mod != null && mod.IsEnabled && mod.Instance != null && mod.Manifest != null && string.IsNullOrEmpty(mod.LoadError);

        private static string FindModLocale(LoadedMod mod, string code)
        {
            foreach (string candidate in mod.Localization.Keys)
                if (Canonical(candidate) == code) return candidate;
            return null;
        }

        private static string Category(string table)
        {
            switch (table)
            {
                case "ClientName": return "顾客 / 姓名";
                case "Dialogue": return "顾客 / 对话";
                case "PhoneDialogue": return "电话 / 对话";
                case "Item": return "物品";
                case "Interactable": return "场景 / 交互";
                case "Hint": return "提示";
                case "MainMenu": return "界面 / 主菜单";
                case "UI": return "界面 / 游戏界面";
                case "Mechanic": return "经营 / 机制";
                case "NetworkUpgrade": return "成长 / 人脉升级";
                case "NewGame": return "新游戏";
                case "PerkTable": return "成长 / 专长";
                case "RepPerkTable": return "成长 / 声望专长";
                case "Tutorial": return "教程";
                default: return "未分类 / " + table;
            }
        }

        private static string CategoryHelp(string table) =>
            CatalogPresentation.CategoryDescription(Category(table));

        private sealed class Binding
        {
            public readonly TextEntry Entry;
            public readonly LoadedMod Mod;
            private readonly StringTable table;
            private readonly long keyId;
            private readonly string locale;
            private readonly string original;
            private readonly bool existed;
            private readonly bool smart;
            private readonly bool originalSmart;
            private bool applied;
            private bool createdLocale;

            public Binding(TextEntry entry, StringTable table, long keyId, string original, bool existed, bool originalSmart, bool smart)
            {
                Entry = entry;
                this.table = table;
                this.keyId = keyId;
                this.original = original;
                this.existed = existed;
                this.smart = smart;
                this.originalSmart = originalSmart;
            }

            public Binding(TextEntry entry, LoadedMod mod, string locale, string original, bool existed)
            {
                Entry = entry;
                Mod = mod;
                this.locale = locale;
                this.original = original;
                this.existed = existed;
            }

            public void Apply(string text)
            {
                if (Mod == null)
                {
                    if (table == null) throw new InvalidOperationException("文本表已被卸载，请重新启动游戏后再应用修改。");
                    var value = table.GetEntry(keyId) ?? table.AddEntry(keyId, text);
                    value.Value = text;
                    value.IsSmart = smart;
                }
                else
                {
                    if (!Mod.Localization.TryGetValue(locale, out var dictionary))
                    {
                        dictionary = new Dictionary<string, string>(StringComparer.Ordinal);
                        Mod.Localization[locale] = dictionary;
                        createdLocale = true;
                    }
                    dictionary[Entry.Id.Key] = text;
                }
                applied = true;
            }

            public void Restore()
            {
                if (!applied) return;
                if (Mod == null && table == null) { applied = false; return; }
                if (table != null)
                {
                    if (existed)
                    {
                        var value = table.GetEntry(keyId) ?? table.AddEntry(keyId, original);
                        value.Value = original;
                        value.IsSmart = originalSmart;
                    }
                    else table.RemoveEntry(keyId);
                }
                else if (Mod.Localization.TryGetValue(locale, out var dictionary))
                {
                    if (existed) dictionary[Entry.Id.Key] = original;
                    else dictionary.Remove(Entry.Id.Key);
                    if (createdLocale && dictionary.Count == 0) Mod.Localization.Remove(locale);
                }
                applied = false;
                createdLocale = false;
            }
        }
    }
}
