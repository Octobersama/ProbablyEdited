using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using ProbablyEdited;

internal static class Program
{
    private static int Main()
    {
        int failures = 0;
        Run("preset IDs enforce the 36-preset limit without reuse", PresetIdsAreUniqueAndLimited, ref failures);
        Run("legacy active data retains locale identities during deferred migration", MigratesLegacyLanguages, ref failures);
        Run("v1 and v2 workspaces migrate names, order and active identity without slot 01", MigratesLegacyWorkspaces, ref failures);
        Run("stable IDs preserve duplicate-name preset isolation across reload", DuplicateNamesRemainIsolated, ref failures);
        Run("preset drafts remain isolated and active choice changes only on commit", PresetDraftsAreIsolated, ref failures);
        Run("share imports merge into existing presets or create only on confirmation", SharesOnePreset, ref failures);
        Run("new imports reject capacity changes while full-capacity merges remain allowed", ImportCapacityIsAtomic, ref failures);
        Run("deletion protects active and last presets while tracking saved removals", PresetDeletionIsProtected, ref failures);
        Run("stale imports cannot target a deleted preset recreated with another ID", DeletedPresetPreviewCannotTargetReplacement, ref failures);
        Console.WriteLine(failures == 0 ? "All 9 preset checks passed." : $"{failures} preset check(s) failed.");
        return failures == 0 ? 0 : 1;
    }

    private static void PresetIdsAreUniqueAndLimited()
    {
        var workspace = new PresetWorkspace();
        string firstId = workspace.SelectedPresetId;
        Equal("预设01", workspace.GetPresetName(firstId), "The initial preset name changed.");
        Ensure(!string.IsNullOrWhiteSpace(firstId), "The initial preset has no ID.");

        TextEntry entry = Entry("game", "en", "baseline", "English baseline");
        workspace.State.SetCatalog(new[] { entry });
        workspace.State.Stage(entry, "first preset override");
        string secondId = workspace.CreatePreset();
        Ensure(!StringComparer.Ordinal.Equals(firstId, secondId), "A created preset reused the first preset ID.");
        Equal(0, workspace.State.OverrideCount, "A new preset inherited another preset's override.");
        Equal("English baseline", workspace.State.GetText(entry), "A new preset did not start from the catalog baseline.");

        var ids = new HashSet<string>(StringComparer.Ordinal) { firstId, secondId };
        while (workspace.Presets.Count < PresetWorkspace.MaxPresets)
        {
            string createdId = workspace.CreatePreset();
            Ensure(ids.Add(createdId), "Preset creation reused an existing ID.");
        }

        Equal(36, workspace.Presets.Count, "The workspace did not reach its 36-preset limit.");
        Ensure(!workspace.CanCreatePreset, "The workspace allowed a 37th preset.");
        string selectedAtCapacity = workspace.SelectedPresetId;
        Throws<InvalidOperationException>(() => workspace.CreatePreset());
        Equal(selectedAtCapacity, workspace.SelectedPresetId, "Rejecting a 37th preset changed selection.");
        Equal(36, workspace.Presets.Count, "Rejecting a 37th preset changed the preset count.");

        workspace.SelectPreset(firstId);
        Throws<InvalidOperationException>(() => workspace.DeletePreset(firstId));
        Equal(36, workspace.Presets.Count, "Rejecting deletion of the active preset changed the workspace.");

        workspace.SelectPreset(selectedAtCapacity);
        workspace.DeletePreset(selectedAtCapacity);
        string replacementId = workspace.CreatePreset();
        Ensure(!ids.Contains(replacementId), "Create-delete-create reused a deleted preset ID.");
        Ensure(!StringComparer.Ordinal.Equals(selectedAtCapacity, replacementId), "The replacement preset has the deleted preset's identity.");
        Equal(36, workspace.Presets.Count, "Deleting and recreating a preset changed the count.");

        var singlePreset = new PresetWorkspace();
        Throws<InvalidOperationException>(() => singlePreset.DeletePreset(singlePreset.SelectedPresetId));
        Equal(1, singlePreset.Presets.Count, "Deleting the last preset changed the workspace.");
    }

    private static void MigratesLegacyLanguages()
    {
        TextEntry english = Entry("game", "en", "legacy", "English baseline");
        TextEntry brazilian = Entry("mod-a", "ptBR", "legacy", "Português original");
        TextEntry brazilianHyphen = Entry("mod-a", "pt-BR", "legacy-hyphen", "Texto com hífen");
        var legacy = new EditorState();
        legacy.Stage(english, "English replacement");
        legacy.Stage(brazilian, "Português editado");
        legacy.Stage(brazilianHyphen, "Português com hífen");
        string activeJson = legacy.ExportJson("fixture");

        PresetWorkspace migrated = PresetWorkspace.Load(null, activeJson);
        Equal(migrated.ActivePresetId, migrated.SelectedPresetId, "Legacy active data was not both active and selected.");
        Equal("预设01", migrated.Presets[0].Name, "Legacy active data did not receive its initial display name.");
        Equal(3, migrated.State.OverrideCount, "Legacy migration lost one or more languages.");
        Equal("Português editado", migrated.State.Overrides[brazilian.Id], "Migration changed the ptBR identity.");
        Equal("Português com hífen", migrated.State.Overrides[brazilianHyphen.Id], "Migration normalized the pt-BR identity.");
        var activeLocaleSet = new HashSet<string>(migrated.GetActiveLocales(), StringComparer.Ordinal);
        Ensure(activeLocaleSet.Contains("en") && activeLocaleSet.Contains("ptBR") && activeLocaleSet.Contains("pt-BR"), "Migration did not retain all active override locales.");
        Ensure(migrated.RequiresWorkspaceMigration && migrated.HasUnsavedChanges, "Legacy active data was not marked for deferred migration.");

        string workspaceJson = migrated.SerializeWorkspace();
        JObject root = JObject.Parse(workspaceJson);
        Equal(3, root.Value<int>("schemaVersion"), "Legacy active data did not serialize as workspace schema v3.");
        Equal(migrated.ActivePresetId, root.Value<string>("activePresetId"), "Serialization changed the active preset ID.");
        Ensure(root["activeSlot"] == null, "The v3 workspace still contains the numeric active slot.");
        PresetWorkspace reloaded = PresetWorkspace.Load(workspaceJson, null);
        Equal(migrated.ActivePresetId, reloaded.ActivePresetId, "Reloading changed the migrated preset ID.");
        Equal(3, reloaded.GetPresetStatistics(reloaded.ActivePresetId).LanguageCount, "The migrated workspace lost language identities on reload.");
        Ensure(!reloaded.RequiresWorkspaceMigration, "A v3 workspace remained marked for migration.");
    }

    private static void MigratesLegacyWorkspaces()
    {
        const string versionOne = "{\"schema\":\"probably-edited-workspace\",\"schemaVersion\":1,\"activeSlot\":5,\"presets\":[{\"slot\":5,\"overrides\":[{\"sourceId\":\"mod\",\"locale\":\"ptBR\",\"tableId\":\"preset-checks\",\"key\":\"v1\",\"text\":\"saved\"}]}]}";
        PresetWorkspace v1 = PresetWorkspace.Load(versionOne, null);
        Equal(1, v1.Presets.Count, "The v1 migration invented an absent slot-01 preset.");
        Equal("预设05", v1.Presets[0].Name, "A v1 preset did not retain its slot-derived display name.");
        Ensure(v1.RequiresWorkspaceMigration, "Loading v1 data did not defer migration until save.");
        TextEntry v1Entry = Entry("mod", "ptBR", "v1", "original");
        Equal("saved", v1.State.Overrides[v1Entry.Id], "V1 migration lost its override.");

        const string versionTwo = "{\"schema\":\"probably-edited-workspace\",\"schemaVersion\":2,\"activeSlot\":9,\"presets\":[{\"slot\":9,\"name\":\"同名\",\"overrides\":[{\"sourceId\":\"game\",\"locale\":\"fr\",\"tableId\":\"preset-checks\",\"key\":\"active\",\"text\":\"active value\"}]},{\"slot\":3,\"name\":\"同名\",\"overrides\":[{\"sourceId\":\"mod\",\"locale\":\"pt-BR\",\"tableId\":\"preset-checks\",\"key\":\"inactive\",\"text\":\"inactive value\"}]}]}";
        PresetWorkspace v2 = PresetWorkspace.Load(versionTwo, null);
        Equal(2, v2.Presets.Count, "The v2 migration added an absent slot-01 preset.");
        Equal("同名", v2.Presets[0].Name, "The first v2 name was not retained.");
        Equal("同名", v2.Presets[1].Name, "The second v2 name was not retained.");
        Ensure(!StringComparer.Ordinal.Equals(v2.Presets[0].Id, v2.Presets[1].Id), "Duplicate names merged distinct v2 presets.");
        Equal(v2.Presets[1].Id, v2.ActivePresetId, "The v2 active slot did not map to its generated ID.");
        Ensure(v2.RequiresWorkspaceMigration, "Loading v2 data did not defer migration until save.");
        TextEntry french = Entry("game", "fr", "active", "French baseline");
        TextEntry brazilian = Entry("mod", "pt-BR", "inactive", "Portuguese baseline");
        Equal("active value", v2.State.Overrides[french.Id], "The v2 active choice lost its text.");
        v2.SelectPreset(v2.Presets[0].Id);
        Equal("inactive value", v2.State.Overrides[brazilian.Id], "The v2 inactive preset lost its locale override.");

        string migratedJson = v2.SerializeWorkspace();
        JObject migratedRoot = JObject.Parse(migratedJson);
        Equal(3, migratedRoot.Value<int>("schemaVersion"), "Saving v2 data did not write schema v3.");
        Equal(v2.ActivePresetId, migratedRoot.Value<string>("activePresetId"), "V2 migration changed the active choice in serialized data.");
        JArray migratedPresets = (JArray)migratedRoot["presets"];
        Ensure(((JObject)migratedPresets[0])["slot"] == null, "V3 preset records still contain numeric slots.");
        PresetWorkspace reloaded = PresetWorkspace.Load(migratedJson, null);
        Equal(v2.Presets[0].Id, reloaded.Presets[0].Id, "Saving and reloading changed a migrated preset ID.");
        Equal(v2.Presets[1].Id, reloaded.Presets[1].Id, "Saving and reloading merged duplicate-name preset IDs.");
        Equal(v2.ActivePresetId, reloaded.ActivePresetId, "Saving and reloading changed the v2 active choice.");
    }

    private static void DuplicateNamesRemainIsolated()
    {
        TextEntry english = Entry("game", "en", "duplicate-name-en", "English baseline");
        TextEntry french = Entry("game", "fr", "duplicate-name-fr", "Texte initial");
        var workspace = new PresetWorkspace();
        workspace.State.SetCatalog(new[] { english });
        workspace.State.Stage(english, "first preset text");
        string firstId = workspace.SelectedPresetId;
        workspace.RenamePreset(firstId, "方案");

        string secondId = workspace.CreatePreset();
        workspace.State.Stage(french, "second preset text");
        workspace.RenamePreset(secondId, "方案");
        Ensure(!StringComparer.Ordinal.Equals(firstId, secondId), "Duplicate names changed preset identity.");

        string json = workspace.SerializeWorkspace();
        PresetWorkspace reloaded = PresetWorkspace.Load(json, null);
        Equal(firstId, reloaded.Presets[0].Id, "Saving/reloading changed the first stable ID.");
        Equal(secondId, reloaded.Presets[1].Id, "Saving/reloading changed the second stable ID.");
        Equal("方案", reloaded.Presets[0].Name, "The first duplicate name was not saved.");
        Equal("方案", reloaded.Presets[1].Name, "The second duplicate name was not saved.");

        reloaded.SelectPreset(firstId);
        Equal("first preset text", reloaded.State.Overrides[english.Id], "The first duplicate-name preset lost its own text.");
        Ensure(!reloaded.State.Overrides.ContainsKey(french.Id), "The first duplicate-name preset inherited the second preset's text.");
        reloaded.SelectPreset(secondId);
        Equal("second preset text", reloaded.State.Overrides[french.Id], "The second duplicate-name preset lost its own text.");
        Ensure(!reloaded.State.Overrides.ContainsKey(english.Id), "The second duplicate-name preset inherited the first preset's text.");
    }

    private static void PresetDraftsAreIsolated()
    {
        TextEntry english = Entry("game", "en", "draft-isolation-en", "English baseline");
        TextEntry french = Entry("game", "fr", "draft-isolation-fr", "Texte initial");
        var workspace = new PresetWorkspace();
        string firstId = workspace.SelectedPresetId;
        workspace.State.SetCatalog(new[] { english });
        workspace.State.Stage(english, "first preset English");
        workspace.MarkSavedAndApplied();

        string secondId = workspace.CreatePreset();
        workspace.State.SetCatalog(new[] { french });
        workspace.State.Stage(french, "second preset French draft");
        workspace.SelectPreset(firstId);
        Equal("first preset English", workspace.State.GetText(english), "Switching did not restore the first preset draft.");
        Equal(firstId, workspace.ActivePresetId, "Browsing the second preset changed the active identity.");
        workspace.SelectPreset(secondId);
        Equal("second preset French draft", workspace.State.GetText(french), "Switching did not restore the second preset draft.");
        Ensure(!workspace.State.IsModified(english), "The second preset inherited the first preset's override.");

        string activeJson = workspace.SerializeWorkspace();
        string nextActiveJson = workspace.SerializeForSaveAndApply();
        Equal(firstId, JObject.Parse(activeJson).Value<string>("activePresetId"), "Ordinary serialization changed active identity before apply.");
        Equal(secondId, JObject.Parse(nextActiveJson).Value<string>("activePresetId"), "Save serialization did not target the selected identity.");
        Equal(firstId, workspace.ActivePresetId, "Preparing a save mutated active identity prematurely.");
        workspace.MarkSavedAndApplied();
        Equal(secondId, workspace.ActivePresetId, "A successful commit did not activate the selected ID.");
        Ensure(!workspace.HasUnsavedChanges, "A successful commit left workspace drafts dirty.");
        workspace.SelectPreset(firstId);
        Equal("first preset English", workspace.State.GetText(english), "The first preset draft was lost when active identity changed.");
    }

    private static void SharesOnePreset()
    {
        TextEntry conflict = Entry("game", "en", "share-conflict", "Conflict baseline");
        TextEntry french = Entry("mod-a", "fr", "share-french", "French baseline");
        TextEntry targetOnly = Entry("mod-b", "ko", "share-target-only", "Korean baseline");
        var workspace = new PresetWorkspace();
        string sourceId = workspace.SelectedPresetId;
        workspace.State.SetCatalog(new[] { conflict });
        workspace.State.Stage(conflict, "incoming conflict");
        workspace.State.SetCatalog(new[] { french });
        workspace.State.Stage(french, "incoming French");
        workspace.MarkSavedAndApplied();

        string targetId = workspace.CreatePreset();
        workspace.State.SetCatalog(new[] { conflict });
        workspace.State.Stage(conflict, "target conflict");
        workspace.State.SetCatalog(new[] { targetOnly });
        workspace.State.Stage(targetOnly, "preserve this target value");
        workspace.RenamePreset(sourceId, "<旅行 & 方案>");
        workspace.RenamePreset(targetId, "本地方案");
        string export = workspace.ExportPresetJson(sourceId, "fixture");
        Equal(2, JObject.Parse(export).Value<int>("schemaVersion"), "Preset share files changed from schema v2.");
        Equal("<旅行 & 方案>", JObject.Parse(export).Value<string>("presetName"), "The v2 export did not preserve the stored preset name.");
        Ensure(!export.Contains("preserve this target value", StringComparison.Ordinal), "The export included unrelated target data.");
        Ensure(!export.Contains("Conflict baseline", StringComparison.Ordinal), "The export included baseline text.");

        PresetImportPreview existingPreview = workspace.PreviewImport(export, targetId, "fixture");
        Equal(targetId, existingPreview.TargetPresetId, "The preview did not retain the existing target ID.");
        Ensure(!existingPreview.CreatesPreset, "An existing target was treated as a new preset.");
        Equal(1, existingPreview.ConflictCount, "The target conflict was not previewed.");
        Equal("本地方案", existingPreview.PresetName, "The existing target preview did not retain its name.");
        workspace.ConfirmImport(existingPreview, overwriteConflicts: false);
        Equal("target conflict", workspace.State.Overrides[conflict.Id], "Keep-conflicts import replaced the target value.");
        Equal("preserve this target value", workspace.State.Overrides[targetOnly.Id], "Import removed an unrelated target override.");
        Equal("incoming French", workspace.State.Overrides[french.Id], "Import did not merge a non-conflicting language.");
        Equal(sourceId, workspace.ActivePresetId, "Confirming an import changed active identity before SaveAndApply.");

        int beforeNewImport = workspace.Presets.Count;
        PresetImportPreview newPreview = workspace.PreviewImport(export, null, "fixture");
        Ensure(newPreview.CreatesPreset && newPreview.TargetPresetId == null, "The explicit new target was not represented as new.");
        Equal(beforeNewImport, workspace.Presets.Count, "Previewing a new target created a preset before confirmation.");
        Equal("<旅行 & 方案>", newPreview.PresetName, "A new-target v2 import did not adopt the share name.");
        Equal(0, newPreview.Existing.OverrideCount, "A new target reported existing data.");
        workspace.ConfirmImport(newPreview, overwriteConflicts: true);
        string importedId = workspace.SelectedPresetId;
        Ensure(importedId != null && !StringComparer.Ordinal.Equals(importedId, sourceId) && !StringComparer.Ordinal.Equals(importedId, targetId), "Confirming a new import did not allocate a distinct ID.");
        Equal(beforeNewImport + 1, workspace.Presets.Count, "Confirming a new import did not create exactly one preset.");
        Equal(2, workspace.GetPresetStatistics(importedId).OverrideCount, "The new target did not receive all imported overrides.");
        Equal("<旅行 & 方案>", workspace.GetPresetName(importedId), "The new target did not retain the share name.");

        var legacyShare = new EditorState();
        legacyShare.Stage(french, "legacy French");
        PresetImportPreview legacyPreview = workspace.PreviewImport(legacyShare.ExportJson("fixture"), null, "fixture");
        workspace.ConfirmImport(legacyPreview, overwriteConflicts: true);
        Equal("预设04", workspace.GetPresetName(workspace.SelectedPresetId), "A v1 share without a name did not receive a generated display name.");
    }

    private static void ImportCapacityIsAtomic()
    {
        var workspace = new PresetWorkspace();
        var patch = new EditorState();
        TextEntry incoming = Entry("mod", "en", "capacity-import", "incoming baseline");
        patch.Stage(incoming, "incoming text");
        string share = patch.ExportJson("fixture");

        while (workspace.Presets.Count < PresetWorkspace.MaxPresets - 1)
            workspace.CreatePreset();

        string selectedBeforePreview = workspace.SelectedPresetId;
        PresetImportPreview newPreview = workspace.PreviewImport(share, null, "fixture");
        string extraId = workspace.CreatePreset();
        workspace.SelectPreset(selectedBeforePreview);
        Equal(PresetWorkspace.MaxPresets, workspace.Presets.Count, "The test did not fill the workspace during preview.");
        Throws<InvalidOperationException>(() => workspace.ConfirmImport(newPreview, overwriteConflicts: true));
        Equal(PresetWorkspace.MaxPresets, workspace.Presets.Count, "A capacity-rejected import created a preset.");
        Equal(0, workspace.GetPresetStatistics(extraId).OverrideCount, "A capacity-rejected import changed an unrelated preset.");
        Ensure(!workspace.State.Overrides.ContainsKey(incoming.Id), "A capacity-rejected import modified the selected preset.");

        string mergeTargetId = workspace.Presets[0].Id;
        PresetImportPreview existingPreview = workspace.PreviewImport(share, mergeTargetId, "fixture");
        workspace.ConfirmImport(existingPreview, overwriteConflicts: true);
        Equal(36, workspace.Presets.Count, "Merging into an existing preset changed capacity.");
        Equal("incoming text", workspace.State.Overrides[incoming.Id], "A full-capacity existing-target import was rejected or lost data.");
    }

    private static void PresetDeletionIsProtected()
    {
        var workspace = new PresetWorkspace();
        string firstId = workspace.ActivePresetId;
        Throws<InvalidOperationException>(() => workspace.DeletePreset(firstId));
        Equal(1, workspace.Presets.Count, "Rejecting deletion of the last preset removed it.");

        string secondId = workspace.CreatePreset();
        workspace.MarkSavedAndApplied();
        Throws<InvalidOperationException>(() => workspace.DeletePreset(secondId));
        Equal(secondId, workspace.ActivePresetId, "Rejecting deletion changed the active preset.");

        workspace.SelectPreset(firstId);
        workspace.DeletePreset(firstId);
        Equal(secondId, workspace.SelectedPresetId, "Deleting an inactive selected preset did not fall back to the active ID.");
        Equal(secondId, workspace.ActivePresetId, "Deleting an inactive preset changed the active ID.");
        Ensure(workspace.HasUnsavedChanges, "Deleting a saved preset did not mark the workspace dirty.");
        Equal(1, workspace.Presets.Count, "The saved preset remained after deletion.");

        PresetWorkspace reloaded = PresetWorkspace.Load(workspace.SerializeWorkspace(), null);
        Equal(secondId, reloaded.ActivePresetId, "Reloading a deletion changed active identity.");
        Equal(secondId, reloaded.Presets[0].Id, "Reloading a deletion changed the surviving ID.");
        Ensure(!reloaded.HasUnsavedChanges, "Reloading the serialized deletion left a dirty draft.");
    }

    private static void DeletedPresetPreviewCannotTargetReplacement()
    {
        var workspace = new PresetWorkspace();
        string activeId = workspace.ActivePresetId;
        string targetId = workspace.CreatePreset();
        workspace.SelectPreset(activeId);
        workspace.MarkSavedAndApplied();

        var patch = new EditorState();
        TextEntry incoming = Entry("mod", "en", "stale-import", "incoming baseline");
        patch.Stage(incoming, "incoming value");
        PresetImportPreview preview = workspace.PreviewImport(patch.ExportJson("fixture"), targetId, "fixture");
        workspace.DeletePreset(targetId);
        string replacementId = workspace.CreatePreset();
        workspace.SelectPreset(activeId);

        Ensure(!StringComparer.Ordinal.Equals(targetId, replacementId), "Recreating a deleted target reused its identity.");
        Throws<InvalidOperationException>(() => workspace.ConfirmImport(preview, overwriteConflicts: true));
        Equal(0, workspace.GetPresetStatistics(replacementId).OverrideCount, "A stale preview modified the replacement preset.");
    }

    private static TextEntry Entry(string source, string locale, string key, string original)
    {
        var id = new TextId(source, locale, "preset-checks", key);
        return new TextEntry(id, source, string.Empty, id.TableId, id.Key, string.Empty, original, false, false, string.Empty);
    }

    private static void Throws<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException("Expected " + typeof(TException).Name + ".");
    }

    private static void Run(string name, Action check, ref int failures)
    {
        try
        {
            check();
            Console.WriteLine("PASS " + name);
        }
        catch (Exception exception)
        {
            failures++;
            Console.Error.WriteLine("FAIL " + name + ": " + exception);
        }
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(message + $" Expected: {expected}; actual: {actual}.");
    }
}
