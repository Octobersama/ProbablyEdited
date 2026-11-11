using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using ProbablyEdited;

internal static class Program
{
    private static int Main()
    {
        int failures = 0;
        Run("stable slots stop at 36 without mutation", StableSlotLimit, ref failures);
        Run("legacy migration retains exact multi-language identities", MigratesLegacyLanguages, ref failures);
        Run("preset drafts are isolated from each other and the active snapshot", PresetDraftsAreIsolated, ref failures);
        Run("renamed slot names are dirty and persist through workspace serialization", RenamedPresetPersists, ref failures);
        Run("version 1 workspaces load with default preset names", LoadsVersionOneWorkspace, ref failures);
        Run("version 2 exports preserve names and imports preserve/adopt slot names", SharesOnePreset, ref failures);
        Run("damaged workspace and stale previews are rejected safely", RejectsCorruptionAndStalePreview, ref failures);
        Run("saved slot deletion stays dirty and falls back safely", SavedPresetDeletionTracksDirtyState, ref failures);
        Run("deleting a new slot is clean and reuses the freed slot", NewPresetDeletionIsCleanAndReusesSlot, ref failures);
        Run("deleting and recreating a slot invalidates old import previews", DeletedPresetPreviewCannotTargetRecreatedSlot, ref failures);
        Console.WriteLine(failures == 0 ? "All 10 preset checks passed." : $"{failures} preset check(s) failed.");
        return failures == 0 ? 0 : 1;
    }

    private static void StableSlotLimit()
    {
        var workspace = new PresetWorkspace();
        Equal("预设01", PresetWorkspace.GetPresetName(1), "Slot 01 has the wrong stable name.");
        for (int expectedSlot = 2; expectedSlot <= PresetWorkspace.MaxSlots; expectedSlot++)
            Equal(expectedSlot, workspace.CreatePreset(), "CreatePreset did not select the first free stable slot.");

        Equal(36, workspace.Presets.Count, "The workspace did not contain all 36 allowed slots.");
        Equal("预设36", workspace.Presets[35].Name, "The final slot name is incorrect.");
        Ensure(!workspace.CanCreatePreset, "The workspace still allowed a 37th slot.");
        Throws<InvalidOperationException>(() => workspace.CreatePreset());
        Equal(36, workspace.Presets.Count, "A rejected 37th creation mutated the workspace.");
        Equal(36, workspace.SelectedPresetSlot, "A rejected 37th creation changed the selected slot.");
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
        Equal(1, migrated.ActivePresetSlot, "Legacy migration did not activate slot 01.");
        Equal(1, migrated.SelectedPresetSlot, "Legacy migration did not select slot 01.");
        Equal("预设01", migrated.Presets[0].Name, "Legacy active data did not receive the default slot name.");
        Equal(3, migrated.State.OverrideCount, "Legacy migration lost one or more languages.");
        Equal("Português editado", migrated.State.Overrides[brazilian.Id], "Migration changed the ptBR identity.");
        Equal("Português com hífen", migrated.State.Overrides[brazilianHyphen.Id], "Migration normalized the pt-BR identity.");
        IReadOnlyList<string> activeLocales = migrated.GetActiveLocales();
        var activeLocaleSet = new HashSet<string>(activeLocales, StringComparer.Ordinal);
        Ensure(activeLocaleSet.Contains("en") && activeLocaleSet.Contains("ptBR") && activeLocaleSet.Contains("pt-BR"), "Migrating active.json did not snapshot all active override locales.");
        Ensure(migrated.RequiresWorkspaceMigration, "Legacy data was not marked for workspace migration.");

        string workspaceJson = migrated.SerializeWorkspace();
        migrated.MarkWorkspacePersisted();
        Ensure(!migrated.RequiresWorkspaceMigration, "Persisting the migrated schema did not clear the migration marker.");
        PresetWorkspace reloaded = PresetWorkspace.Load(workspaceJson, null);
        Equal(3, reloaded.GetPresetStatistics(1).OverrideCount, "The migrated workspace lost overrides on reload.");
        Equal(3, reloaded.GetPresetStatistics(1).LanguageCount, "The migrated workspace lost distinct language identities.");
        Ensure(!reloaded.State.IsDirty, "Loading persisted workspace data established a dirty draft.");
    }

    private static void PresetDraftsAreIsolated()
    {
        TextEntry english = Entry("game", "en", "shared", "English baseline");
        TextEntry french = Entry("game", "fr", "shared", "Texte initial");
        var workspace = new PresetWorkspace();
        workspace.State.SetCatalog(new[] { english });
        workspace.State.Stage(english, "slot 01 English");
        workspace.MarkSavedAndApplied();

        Equal(2, workspace.CreatePreset(), "The new preset did not receive slot 02.");
        Equal(0, workspace.State.OverrideCount, "A newly created preset cloned the active overrides.");
        Equal("English baseline", workspace.State.GetText(english), "A new preset did not show the current baseline.");
        workspace.State.SetCatalog(new[] { french });
        workspace.State.Stage(french, "slot 02 French draft");

        workspace.SelectPreset(1);
        Equal("slot 01 English", workspace.State.GetText(english), "Switching back did not restore slot 01's draft.");
        Equal(1, workspace.ActivePresetSlot, "Browsing another slot changed the active preset.");
        workspace.SelectPreset(2);
        Equal("slot 02 French draft", workspace.State.GetText(french), "Switching back did not restore slot 02's French draft.");
        Ensure(!workspace.State.IsModified(english), "Slot 02 inherited slot 01's English override.");
        Equal(1, workspace.ActivePresetSlot, "Selecting slot 02 applied it before saving.");
        Equal(1, workspace.GetPresetStatistics(1).OverrideCount, "Slot 01 statistics changed when editing slot 02.");
        Equal(1, workspace.GetPresetStatistics(2).LanguageCount, "Slot 02 statistics omitted its draft language.");

        string activeJson = workspace.SerializeWorkspace();
        string nextActiveJson = workspace.SerializeForSaveAndApply();
        Ensure(activeJson.Contains("\"activeSlot\": 1", StringComparison.Ordinal), "Current workspace serialization changed the active slot before application.");
        Ensure(nextActiveJson.Contains("\"activeSlot\": 2", StringComparison.Ordinal), "Save serialization did not prepare the selected slot as active.");
        Equal(1, workspace.ActivePresetSlot, "Preparing a save mutated the active slot prematurely.");
        workspace.MarkSavedAndApplied();
        Equal(2, workspace.ActivePresetSlot, "Committing a successful save did not activate the selected slot.");
        Ensure(!workspace.HasUnsavedChanges, "A successful commit left workspace drafts dirty.");
        workspace.SelectPreset(1);
        Equal("slot 01 English", workspace.State.GetText(english), "The old preset's saved draft was lost when the active snapshot changed.");
    }

    private static void SharesOnePreset()
    {
        TextEntry conflict = Entry("game", "en", "conflict", "Conflict baseline");
        TextEntry french = Entry("mod-a", "fr", "french", "French baseline");
        TextEntry targetOnly = Entry("mod-b", "ko", "target-only", "Korean baseline");
        var workspace = new PresetWorkspace();
        workspace.State.SetCatalog(new[] { conflict });
        workspace.State.Stage(conflict, "incoming conflict");
        workspace.State.Stage(french, "incoming French");
        workspace.MarkSavedAndApplied();

        workspace.CreatePreset();
        workspace.State.Stage(conflict, "target conflict");
        workspace.State.Stage(targetOnly, "preserve this target value");
        workspace.RenamePreset(1, "  <旅行 & 方案>  ");
        workspace.RenamePreset(2, "本地方案");
        string export = workspace.ExportPresetJson(1, "fixture");
        Ensure(export.Contains("\"schemaVersion\": 2", StringComparison.Ordinal), "New preset exports are not schema version 2.");
        Equal("<旅行 & 方案>", JObject.Parse(export).Value<string>("presetName"), "The v2 export did not preserve the stored preset name literally.");
        Ensure(!export.Contains("preserve this target value", StringComparison.Ordinal), "The export included unrelated target data.");
        Ensure(!export.Contains("Conflict baseline", StringComparison.Ordinal), "The export included original baseline text.");

        PresetImportPreview preview = workspace.PreviewImport(export, 2, "fixture");
        Equal(2, preview.Incoming.OverrideCount, "Incoming stats did not count one preset's overrides.");
        Equal(2, preview.Incoming.LanguageCount, "Incoming stats did not count both override languages.");
        Equal(1, preview.ConflictCount, "The target conflict was not previewed.");
        Equal(2, preview.Existing.OverrideCount, "Existing stats did not describe the selected target only.");
        Ensure(preview.Warnings.Count > 0, "Preview omitted the current-catalog availability scope.");
        Equal("本地方案", preview.PresetName, "An existing-slot import preview did not retain its chosen target name.");
        Equal("本地方案", workspace.Presets[1].Name, "Importing into an existing slot changed its chosen name.");
        workspace.ConfirmImport(preview, overwriteConflicts: false);
        Equal("本地方案", workspace.Presets[1].Name, "Confirming an import changed the existing target name.");
        Equal("target conflict", workspace.State.Overrides[conflict.Id], "Keep-conflicts import replaced the existing value.");
        Equal("preserve this target value", workspace.State.Overrides[targetOnly.Id], "Import removed an unrelated target override.");
        Equal("incoming French", workspace.State.Overrides[french.Id], "Import did not merge a non-conflicting language.");
        Equal(1, workspace.ActivePresetSlot, "ConfirmImport activated the target before SaveAndApply.");

        PresetImportPreview emptyPreview = workspace.PreviewImport(export, 3, "fixture");
        Ensure(emptyPreview.CreatesPreset, "An empty target was not identified as a new preset.");
        Equal("<旅行 & 方案>", emptyPreview.PresetName, "An empty-slot v2 import preview did not expose the adopted name.");
        Equal(0, emptyPreview.Existing.OverrideCount, "The empty target reported existing data.");
        workspace.ConfirmImport(emptyPreview, overwriteConflicts: true);
        Equal(3, workspace.SelectedPresetSlot, "Confirmed import did not select the requested empty slot.");
        Equal(1, workspace.ActivePresetSlot, "Importing into an empty slot activated it early.");
        Equal(2, workspace.GetPresetStatistics(3).OverrideCount, "The empty target did not receive the complete incoming preset.");
        Equal("<旅行 & 方案>", workspace.Presets[2].Name, "Importing a v2 share into an empty slot did not adopt its literal name.");

        var legacyShare = new EditorState();
        legacyShare.Stage(french, "legacy French");
        PresetImportPreview legacyPreview = workspace.PreviewImport(legacyShare.ExportJson("fixture"), 4, "fixture");
        workspace.ConfirmImport(legacyPreview, overwriteConflicts: true);
        Equal("预设04", workspace.Presets[3].Name, "Importing a v1 share into an empty slot did not use its default name.");
    }

    private static void RenamedPresetPersists()
    {
        var workspace = new PresetWorkspace();
        workspace.RenamePreset(1, "  夜航  ");
        Equal("夜航", workspace.Presets[0].Name, "Rename did not trim surrounding whitespace.");
        Ensure(workspace.HasUnsavedChanges, "A name-only edit did not mark the workspace dirty.");
        Ensure(workspace.Presets[0].IsDirty, "A name-only edit did not mark its preset dirty.");
        Throws<ArgumentException>(() => workspace.RenamePreset(1, "  \t "));
        Equal("夜航", workspace.Presets[0].Name, "Rejecting an empty name changed the preset.");

        string json = workspace.SerializeWorkspace();
        Ensure(json.Contains("\"schemaVersion\": 2", StringComparison.Ordinal), "Renamed names were not serialized in workspace schema v2.");
        PresetWorkspace reloaded = PresetWorkspace.Load(json, null);
        Equal("夜航", reloaded.Presets[0].Name, "Workspace reload lost the preset name.");
        Ensure(!reloaded.HasUnsavedChanges, "Reloading a saved name-only workspace was dirty.");
        int duplicateSlot = reloaded.CreatePreset();
        reloaded.RenamePreset(duplicateSlot, "夜航");
        Equal("夜航", reloaded.Presets[1].Name, "Duplicate preset names were rejected or altered.");
    }

    private static void LoadsVersionOneWorkspace()
    {
        const string versionOne = "{\"schema\":\"probably-edited-workspace\",\"schemaVersion\":1,\"activeSlot\":1,\"presets\":[{\"slot\":1,\"overrides\":[{\"sourceId\":\"mod\",\"locale\":\"ptBR\",\"tableId\":\"preset-checks\",\"key\":\"v1-slot\",\"text\":\"saved\"}]}]}";
        PresetWorkspace workspace = PresetWorkspace.Load(versionOne, null);
        TextEntry legacyEntry = Entry("mod", "ptBR", "v1-slot", "original");
        Equal("预设01", workspace.Presets[0].Name, "A v1 workspace did not receive the default slot name.");
        Equal("saved", workspace.State.Overrides[legacyEntry.Id], "A v1 workspace load lost its override data.");
        Ensure(!workspace.RequiresWorkspaceMigration, "Loading a compatible v1 workspace triggered a startup rewrite.");
        Ensure(workspace.SerializeWorkspace().Contains("\"schemaVersion\": 2", StringComparison.Ordinal), "Serializing v1 data did not write the current schema version.");
    }

    private static void RejectsCorruptionAndStalePreview()
    {
        var legacy = new EditorState();
        legacy.Stage(Entry("game", "en", "legacy", "baseline"), "kept legacy value");
        string legacyJson = legacy.ExportJson("fixture");
        Throws<FormatException>(() => PresetWorkspace.Load("{ damaged", legacyJson));

        var workspace = new PresetWorkspace();
        string workspaceJson = workspace.SerializeWorkspace();
        Throws<NotSupportedException>(() => PresetWorkspace.Load(null, workspaceJson));
        Throws<NotSupportedException>(() => workspace.PreviewImport(workspaceJson, 1, "fixture"));
        workspace.RenamePreset(1, "Keep this name");
        JObject invalidNameShare = JObject.Parse(workspace.ExportPresetJson(1, "fixture"));
        invalidNameShare["presetName"] = "  \t ";
        Throws<FormatException>(() => workspace.PreviewImport(invalidNameShare.ToString(), 2, "fixture"));
        Equal("Keep this name", workspace.Presets[0].Name, "Rejecting an invalid share name changed the stored name.");
        Equal(1, workspace.Presets.Count, "Rejecting an invalid share name created a preset.");

        JObject duplicateSlot = JObject.Parse(workspaceJson);
        JArray presets = (JArray)duplicateSlot["presets"];
        presets.Add(presets[0].DeepClone());
        Throws<FormatException>(() => PresetWorkspace.Load(duplicateSlot.ToString(), null));

        var duplicateIdentityWorkspace = new PresetWorkspace();
        TextEntry duplicateIdentity = Entry("game", "en", "duplicate", "baseline");
        duplicateIdentityWorkspace.State.Stage(duplicateIdentity, "replacement");
        JObject duplicateIdentityRoot = JObject.Parse(duplicateIdentityWorkspace.SerializeWorkspace());
        JArray duplicateOverrides = (JArray)((JObject)((JArray)duplicateIdentityRoot["presets"])[0])["overrides"];
        duplicateOverrides.Add(duplicateOverrides[0].DeepClone());
        Throws<FormatException>(() => PresetWorkspace.Load(duplicateIdentityRoot.ToString(), null));
        JObject futureWorkspace = JObject.Parse(workspaceJson);
        futureWorkspace["schemaVersion"] = 999;
        Throws<NotSupportedException>(() => PresetWorkspace.Load(futureWorkspace.ToString(), null));

        TextEntry entry = Entry("game", "en", "target", "baseline");
        TextEntry incoming = Entry("mod-a", "en", "incoming", "other baseline");
        workspace.State.SetCatalog(new[] { entry });
        workspace.CreatePreset();
        workspace.State.SetCatalog(new[] { entry });
        workspace.State.Stage(entry, "local draft");
        var patch = new EditorState();
        patch.Stage(incoming, "incoming value");
        PresetImportPreview preview = workspace.PreviewImport(patch.ExportJson("fixture"), 2, "fixture");
        workspace.State.Stage(entry, "changed after preview");
        Throws<InvalidOperationException>(() => workspace.ConfirmImport(preview, overwriteConflicts: true));
        Equal("changed after preview", workspace.State.Overrides[entry.Id], "Rejecting a stale preview changed the target draft.");
        Equal(0, workspace.GetPresetStatistics(3).OverrideCount, "A stale empty target was created.");
    }

    private static void SavedPresetDeletionTracksDirtyState()
    {
        var workspace = new PresetWorkspace();
        TextEntry entry = Entry("game", "en", "delete-active", "baseline");
        workspace.State.SetCatalog(new[] { entry });
        workspace.State.Stage(entry, "slot 01 saved value");
        workspace.MarkSavedAndApplied();

        string onePreset = workspace.SerializeWorkspace();
        Throws<InvalidOperationException>(() => workspace.DeletePreset(1));
        Equal(onePreset, workspace.SerializeWorkspace(), "Rejecting deletion of the last preset changed the workspace.");
        Equal(1, workspace.Presets.Count, "Rejecting deletion of the last preset removed it.");
        Equal(1, workspace.SelectedPresetSlot, "Rejecting deletion of the last preset changed the selected slot.");
        Equal(1, workspace.ActivePresetSlot, "Rejecting deletion of the last preset changed the active slot.");

        Equal(2, workspace.CreatePreset(), "The second preset did not use slot 02.");
        workspace.State.Stage(entry, "slot 02 saved value");
        workspace.MarkSavedAndApplied();
        string activeWorkspace = workspace.SerializeWorkspace();
        Throws<InvalidOperationException>(() => workspace.DeletePreset(2));
        Throws<InvalidOperationException>(() => workspace.DeletePreset(3));
        Equal(activeWorkspace, workspace.SerializeWorkspace(), "Rejecting active or missing slot deletion changed the workspace.");
        Equal(2, workspace.SelectedPresetSlot, "Rejecting active or missing slot deletion changed the selected slot.");
        Equal(2, workspace.ActivePresetSlot, "A rejected deletion changed the active slot.");

        IReadOnlyDictionary<TextId, string> activeSnapshot = workspace.ActiveOverrides;
        workspace.SelectPreset(1);
        workspace.DeletePreset(1);
        Equal(2, workspace.SelectedPresetSlot, "Deleting the selected non-active slot did not fall back to the active slot.");
        Equal(2, workspace.ActivePresetSlot, "Deleting an inactive slot changed the active slot.");
        Ensure(ReferenceEquals(activeSnapshot, workspace.ActiveOverrides), "Deleting an inactive slot replaced the active override snapshot.");
        Equal("slot 02 saved value", workspace.ActiveOverrides[entry.Id], "Deleting an inactive slot changed active override data.");
        Ensure(workspace.HasUnsavedChanges, "Deleting a saved slot did not mark the workspace dirty.");
        Ensure(!workspace.State.IsDirty, "The remaining saved preset became dirty after deleting another slot.");
        Equal(1, workspace.Presets.Count, "The deleted saved slot is still present.");
        Equal(2, workspace.Presets[0].Slot, "Deletion changed the surviving slot identity.");

        PresetWorkspace reloaded = PresetWorkspace.Load(workspace.SerializeWorkspace(), null);
        Equal(1, reloaded.Presets.Count, "Reloading a saved deletion restored the removed slot.");
        Equal(2, reloaded.ActivePresetSlot, "Reloading a workspace without slot 01 changed its active slot.");
        Ensure(!reloaded.HasUnsavedChanges, "Reloading the serialized deletion left a dirty draft.");
    }

    private static void NewPresetDeletionIsCleanAndReusesSlot()
    {
        var workspace = new PresetWorkspace();
        TextEntry entry = Entry("game", "en", "delete-new", "baseline");
        workspace.State.SetCatalog(new[] { entry });
        int createdSlot = workspace.CreatePreset();
        workspace.State.Stage(entry, "discarded new-slot draft");

        workspace.DeletePreset(createdSlot);

        Ensure(!workspace.HasUnsavedChanges, "Creating and deleting an unsaved slot left the workspace dirty.");
        Equal(1, workspace.SelectedPresetSlot, "Deleting a new selected slot did not fall back to the active preset.");
        Equal(2, workspace.CreatePreset(), "A deleted unsaved slot was not available for reuse.");
        Equal(2, workspace.Presets.Count, "Reusing the freed slot created an unexpected number of presets.");
    }

    private static void DeletedPresetPreviewCannotTargetRecreatedSlot()
    {
        var workspace = new PresetWorkspace();
        TextEntry target = Entry("game", "en", "preview-target", "target baseline");
        TextEntry incoming = Entry("mod", "en", "preview-incoming", "incoming baseline");
        workspace.State.SetCatalog(new[] { target });
        Equal(2, workspace.CreatePreset(), "The import target did not use slot 02.");
        workspace.State.Stage(target, "old target value");
        workspace.SelectPreset(1);
        workspace.MarkSavedAndApplied();

        var patch = new EditorState();
        patch.Stage(incoming, "incoming value");
        PresetImportPreview preview = workspace.PreviewImport(patch.ExportJson("fixture"), 2, "fixture");
        workspace.DeletePreset(2);
        Equal(2, workspace.CreatePreset(), "The deleted import target slot was not reused.");
        workspace.SelectPreset(1);

        Throws<InvalidOperationException>(() => workspace.ConfirmImport(preview, overwriteConflicts: true));
        Equal(0, workspace.GetPresetStatistics(2).OverrideCount, "A stale preview modified the recreated slot.");
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
