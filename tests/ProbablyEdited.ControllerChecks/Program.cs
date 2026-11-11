using System;
using System.Collections.Generic;
using System.IO;
using ProbablyEdited;
using System.Linq;
using Newtonsoft.Json.Linq;

internal static class Program
{
    private static int Main()
    {
        TestLocalization.Initialize();
        int failures = 0;
        Run("fresh workspaces save and apply a stable preset ID", FreshWorkspaceCanSave, ref failures);
        Run("rename-only changes save and reload without override edits", RenameOnlyChangesSaveAndReload, ref failures);
        Run("legacy active data migrates with all languages intact", MigratesLegacyActive, ref failures);
        Run("corrupt workspace never falls back to legacy active data", CorruptWorkspaceIsProtected, ref failures);
        Run("switching active presets preloads every required language", InactivePresetPreloadsEveryLanguage, ref failures);
        Run("single-preset export imports into the selected target with conflict choices", SharesOnePresetIntoTarget, ref failures);
        Run("deleting a saved inactive preset preserves retryability and reloads", DeletingSavedPresetIsRetryable, ref failures);
        Run("runtime apply failure rolls back disk and runtime independently", FailedApplyRestoresWorkspace, ref failures);
        Run("failed first workspace write removes its partial file", FailedFirstWorkspaceWrite, ref failures);
        Run("runtime rollback still runs when disk rollback fails", RuntimeRollbackSurvivesDiskRollbackFailure, ref failures);

        Console.WriteLine(failures == 0 ? "All 10 controller checks passed." : $"{failures} controller check(s) failed.");
        return failures == 0 ? 0 : 1;
    }

    private static void FreshWorkspaceCanSave()
    {
        string root = NewStore();
        TextEntry entry = Entry("game", "en", "first-install", "original baseline");
        try
        {
            using (EditorController controller = Open(root, entry))
            {
                string workspacePath = Path.Combine(root, "presets.json");
                Ensure(!File.Exists(workspacePath), "Initialize created a workspace before a user save.");
                string presetId = controller.SelectedPresetId;
                Ensure(!string.IsNullOrWhiteSpace(presetId), "A fresh workspace did not assign a preset ID.");
                Equal(presetId, controller.ActivePresetId, "A fresh workspace did not start with its selected preset active.");

                controller.State.Stage(entry, "first saved replacement");
                controller.SaveAndApply();

                Ensure(File.Exists(workspacePath), "The first save did not create presets.json. Status: " + controller.Status);
                Ensure(!controller.HasUnsavedChanges, "A successful first save left workspace drafts dirty.");
                var saved = PresetWorkspace.Load(File.ReadAllText(workspacePath), null);
                Equal(presetId, saved.ActivePresetId, "The first save changed the preset ID.");
                Equal("first saved replacement", saved.State.Overrides[entry.Id], "The first save did not persist the staged value.");
                Equal("first saved replacement", GameTextCatalog.Latest.AppliedValues[entry.Id], "The first save did not apply the value to runtime state.");
            }
        }
        finally
        {
            RemoveStore(root);
        }
    }
    private static void RenameOnlyChangesSaveAndReload()
    {
        string root = NewStore();
        const string originalV1 = "{\"schema\":\"probably-edited-workspace\",\"schemaVersion\":1,\"activeSlot\":1,\"presets\":[{\"slot\":1,\"overrides\":[]}]}";
        string workspacePath = Path.Combine(root, "presets.json");
        string presetId = null;
        try
        {
            File.WriteAllText(workspacePath, originalV1);
            using (EditorController controller = Open(root))
            {
                presetId = controller.SelectedPresetId;
                Ensure(controller.HasUnsavedChanges, "A v1 workspace was not marked for deferred migration.");
                Equal(originalV1, File.ReadAllText(workspacePath), "Loading a v1 workspace rewrote it during startup.");

                controller.RenamePreset(presetId, "  夜航  ");
                Equal("夜航", controller.GetPresetName(presetId), "The controller did not normalize the preset name.");
                Ensure(controller.HasUnsavedChanges, "A controller-only rename was not marked dirty.");

                ModHelper.FailNextWrite("presets.previous.json", afterPartialWrite: false);
                controller.SaveAndApply();
                Equal(originalV1, File.ReadAllText(workspacePath), "A failed backup write deleted or replaced the original v1 workspace.");
                Ensure(controller.HasUnsavedChanges, "A failed name-only save discarded its dirty state.");

                controller.SaveAndApply();
                Ensure(!controller.HasUnsavedChanges, "Saving a name-only change left the workspace dirty.");
                PresetWorkspace saved = PresetWorkspace.Load(File.ReadAllText(workspacePath), null);
                Equal(presetId, saved.ActivePresetId, "The v1 migration changed the new stable ID during save.");
                Equal("夜航", saved.GetPresetName(presetId), "The controller save did not persist the renamed preset.");
                Equal(3, JObject.Parse(File.ReadAllText(workspacePath)).Value<int>("schemaVersion"), "A normal save did not write workspace schema v3.");
                Equal(originalV1, File.ReadAllText(Path.Combine(root, "presets.previous.json")), "Saving v1 data did not preserve the original workspace backup.");
            }

            using (EditorController restarted = Open(root))
            {
                PresetInfo preset = restarted.Presets[0];
                Equal("夜航", restarted.GetPresetName(preset.Id), "Restarting the controller lost the renamed preset name.");
                Equal(presetId, preset.Id, "Restarting changed the persisted preset ID.");
            }
        }
        finally
        {
            RemoveStore(root);
        }
    }

    private static void MigratesLegacyActive()
    {
        string root = NewStore();
        TextEntry english = Entry("game", "en", "legacy-en", "English baseline");
        TextEntry french = Entry("game", "fr", "legacy-fr", "Texte initial");
        var legacyState = new EditorState();
        legacyState.Stage(english, "Legacy English replacement");
        legacyState.Stage(french, "Texte historique");
        string legacyJson = legacyState.ExportJson(PlayerStore.VERSION);
        string legacyPath = Path.Combine(root, "active.json");
        string legacyBackupPath = Path.Combine(root, "previous.json");
        string workspaceBackupPath = Path.Combine(root, "presets.previous.json");
        const string legacyBackup = "legacy active backup stays unchanged";
        const string workspaceBackup = "existing workspace backup stays unchanged";
        try
        {
            File.WriteAllText(legacyPath, legacyJson);
            File.WriteAllText(legacyBackupPath, legacyBackup);
            File.WriteAllText(workspaceBackupPath, workspaceBackup);
            using (EditorController controller = Open(root, english, french))
            {
                Ensure(controller.HasUnsavedChanges, "Legacy active data was not marked for deferred migration.");
                Ensure(!File.Exists(Path.Combine(root, "presets.json")), "Initialize rewrote legacy active data before user save.");
                Equal("Legacy English replacement", GameTextCatalog.Latest.AppliedValues[english.Id], "Loading legacy active data lost the English override.");
                Equal("Texte historique", GameTextCatalog.Latest.AppliedValues[french.Id], "Loading legacy active data lost the French override.");
                Ensure(GameTextCatalog.Latest.LoadCalls.Contains("fr"), "Initialization did not preload the legacy French locale.");
                Equal(1, controller.Presets.Count, "Legacy active data did not load as exactly one preset.");
                string presetId = controller.Presets[0].Id;
                Equal(presetId, controller.ActivePresetId, "Legacy active data did not remain the active preset.");

                controller.SaveAndApply();

                Equal(legacyJson, File.ReadAllText(legacyPath), "Saving migration modified legacy recovery data.");
                Equal(legacyBackup, File.ReadAllText(legacyBackupPath), "Saving migration modified the legacy backup.");
                Equal(workspaceBackup, File.ReadAllText(workspaceBackupPath), "Saving migration overwrote the existing workspace backup.");
                PresetWorkspace workspace = PresetWorkspace.Load(File.ReadAllText(Path.Combine(root, "presets.json")), null);
                Equal(presetId, workspace.ActivePresetId, "Saving migration changed the generated ID.");
                Equal(2, workspace.GetPresetStatistics(presetId).LanguageCount, "Migrated workspace did not retain both locales.");
                Equal("Texte historique", workspace.State.Overrides[french.Id], "Migration changed the French locale identity or text.");
            }
        }
        finally
        {
            RemoveStore(root);
        }
    }

    private static void CorruptWorkspaceIsProtected()
    {
        string root = NewStore();
        TextEntry entry = Entry("game", "en", "corrupt", "original");
        const string invalidWorkspace = "{not a valid workspace";
        var legacy = new EditorState();
        legacy.Stage(entry, "legacy value that must not be used");
        string legacyJson = legacy.ExportJson(PlayerStore.VERSION);
        try
        {
            File.WriteAllText(Path.Combine(root, "presets.json"), invalidWorkspace);
            File.WriteAllText(Path.Combine(root, "active.json"), legacyJson);
            using (EditorController controller = Open(root, entry))
            {
                controller.State.Stage(entry, "protected draft");
                controller.SaveAndApply();

                Equal(invalidWorkspace, File.ReadAllText(Path.Combine(root, "presets.json")), "A corrupt workspace was overwritten.");
                Equal(0, GameTextCatalog.Latest.AppliedValues.Count, "The controller silently loaded legacy data despite an existing corrupt workspace.");
                Ensure(controller.HasUnsavedChanges, "Rejecting a save discarded the protected draft.");
                Equal(legacyJson, File.ReadAllText(Path.Combine(root, "active.json")), "The legacy recovery file was modified.");
            }
        }
        finally
        {
            RemoveStore(root);
        }
    }

    private static void InactivePresetPreloadsEveryLanguage()
    {
        string root = NewStore();
        TextEntry english = Entry("game", "en", "shared", "English baseline");
        TextEntry french = Entry("game", "fr", "shared", "Texte initial");
        TextEntry korean = Entry("game", "ko", "shared", "한국어 원문");
        var seed = new PresetWorkspace();
        seed.State.Stage(english, "Active English");
        seed.MarkSavedAndApplied();
        string activeId = seed.ActivePresetId;
        string secondId = seed.CreatePreset();
        seed.State.Stage(french, "Draft French");
        seed.State.Stage(korean, "Draft Korean");
        File.WriteAllText(Path.Combine(root, "presets.json"), seed.SerializeWorkspace());

        try
        {
            using (EditorController controller = Open(root, english, french, korean))
            {
                Ensure(!GameTextCatalog.Latest.LoadCalls.Contains("fr"), "Restart eagerly loaded an inactive preset language.");
                Ensure(!GameTextCatalog.Latest.LoadCalls.Contains("ko"), "Restart eagerly loaded another inactive preset language.");
                Equal("Active English", GameTextCatalog.Latest.AppliedValues[english.Id], "Restart did not apply the saved active preset.");

                controller.SelectPreset(secondId);
                Equal(activeId, controller.ActivePresetId, "Browsing the second preset changed the active identity.");
                Equal("Active English", GameTextCatalog.Latest.AppliedValues[english.Id], "Browsing an inactive preset applied its draft prematurely.");
                controller.SaveAndApply();

                Ensure(GameTextCatalog.Latest.LoadCalls.Contains("fr"), "SaveAndApply did not load the selected preset's French bindings.");
                Ensure(GameTextCatalog.Latest.LoadCalls.Contains("ko"), "SaveAndApply did not load the selected preset's Korean bindings.");
                Equal(secondId, controller.ActivePresetId, "Successful SaveAndApply did not activate the selected ID.");
                Ensure(!GameTextCatalog.Latest.AppliedValues.ContainsKey(english.Id), "Switching presets left the previous English override active.");
                Equal("Draft French", GameTextCatalog.Latest.AppliedValues[french.Id], "The new French override was not applied.");
                Equal("Draft Korean", GameTextCatalog.Latest.AppliedValues[korean.Id], "The new Korean override was not applied.");

                int lastApply = GameTextCatalog.Latest.ApplySnapshots.Count - 1;
                Ensure(GameTextCatalog.Latest.ApplySnapshots[lastApply - 1].ContainsKey(english.Id), "The prior active overlay was not restored before applying the selected preset.");
                Ensure(GameTextCatalog.Latest.ApplySnapshots[lastApply].ContainsKey(french.Id), "The final runtime snapshot omitted the French override.");
                Ensure(GameTextCatalog.Latest.ApplySnapshots[lastApply].ContainsKey(korean.Id), "The final runtime snapshot omitted the Korean override.");
                IReadOnlyList<string> loadedAtFinalApply = GameTextCatalog.Latest.LoadedLocalesAtApply[lastApply];
                Ensure(loadedAtFinalApply.Contains("en") && loadedAtFinalApply.Contains("fr") && loadedAtFinalApply.Contains("ko"), "The final apply began before every old-active and selected-preset locale was bound.");
            }

            using (EditorController restarted = Open(root, english, french, korean))
            {
                Equal(secondId, restarted.ActivePresetId, "Restart did not restore the newly saved active ID.");
                Equal("Draft French", GameTextCatalog.Latest.AppliedValues[french.Id], "Restart lost the active French override.");
                Equal("Draft Korean", GameTextCatalog.Latest.AppliedValues[korean.Id], "Restart lost the active Korean override.");
            }
        }
        finally
        {
            RemoveStore(root);
        }
    }

    private static void SharesOnePresetIntoTarget()
    {
        string root = NewStore();
        TextEntry conflict = Entry("game", "en", "conflict", "Conflict baseline");
        TextEntry incomingFrench = Entry("game", "fr", "incoming-fr", "French baseline");
        TextEntry unrelatedKorean = Entry("game", "ko", "unrelated-ko", "Korean baseline");
        TextEntry staleEntry = Entry("game", "en", "stale", "Stale baseline");
        try
        {
            using (EditorController controller = Open(root, conflict, incomingFrench, unrelatedKorean, staleEntry))
            {
                string sourceId = controller.SelectedPresetId;
                controller.State.Stage(conflict, "incoming conflict");
                controller.State.Stage(incomingFrench, "incoming French");
                controller.SaveAndApply();

                string targetId = controller.CreatePreset();
                controller.State.Stage(conflict, "local conflict");
                controller.State.Stage(unrelatedKorean, "keep unrelated value");
                controller.RenamePreset(sourceId, "Controller source");
                controller.RenamePreset(targetId, "Controller target");
                controller.Export("source-preset", sourceId);

                string sharePath = Path.Combine(root, "exports", "source-preset.json");
                string shareJson = File.ReadAllText(sharePath);
                Ensure(shareJson.Contains("\"schemaVersion\": 2", StringComparison.Ordinal), "The new share file is not version 2.");
                Ensure(shareJson.Contains("\"presetName\": \"Controller source\"", StringComparison.Ordinal), "The v2 share metadata did not use the exported preset's actual name.");
                Ensure(shareJson.Contains("incoming conflict", StringComparison.Ordinal), "The chosen preset override was not exported.");
                Ensure(!shareJson.Contains("local conflict", StringComparison.Ordinal), "The export included another preset's conflict value.");
                Ensure(!shareJson.Contains("keep unrelated value", StringComparison.Ordinal), "The export included an unrelated preset override.");
                Ensure(!shareJson.Contains("Conflict baseline", StringComparison.Ordinal), "The export included original baseline text.");
                File.WriteAllText(Path.Combine(root, "exports", "workspace-schema.json"), File.ReadAllText(Path.Combine(root, "presets.json")));
                Throws<NotSupportedException>(() => controller.PreviewImport("exports/workspace-schema.json", null));

                PresetImportPreview preview = controller.PreviewImport("exports/source-preset.json", targetId);
                Equal(targetId, preview.TargetPresetId, "The preview did not retain the selected existing target ID.");
                Ensure(!preview.CreatesPreset, "Importing into a named preset previewed creation.");
                Equal("Controller target", preview.PresetName, "The import preview did not preserve the existing target name.");
                Equal(2, preview.Incoming.OverrideCount, "Incoming stats did not count the chosen preset's overrides.");
                Equal(2, preview.Incoming.LanguageCount, "Incoming language stats did not include all languages.");
                Equal(1, preview.Incoming.SourceCount, "Incoming source stats are incorrect.");
                Equal(2, preview.Existing.OverrideCount, "Existing stats did not describe only the target preset.");
                Equal(1, preview.ConflictCount, "Preview did not identify the target's conflicting value.");
                Equal(0, preview.UnavailableCount, "An override from an unloaded language was counted as missing.");
                Ensure(preview.Warnings.Any(warning => warning.Contains("en", StringComparison.Ordinal)), "Unavailable-count scope omitted the current catalog locale.");

                string activeConflictBeforeImport = GameTextCatalog.Latest.AppliedValues[conflict.Id];
                string activeFrenchBeforeImport = GameTextCatalog.Latest.AppliedValues[incomingFrench.Id];
                controller.ConfirmImport(preview, overwriteConflicts: false);
                Equal("Controller target", controller.Presets[1].Name, "Importing into an existing preset changed its chosen name.");
                Equal("local conflict", controller.State.Overrides[conflict.Id], "A keep-conflicts import replaced the target value.");
                Equal("incoming French", controller.State.Overrides[incomingFrench.Id], "A non-conflicting language was not merged into the target.");
                Equal("keep unrelated value", controller.State.Overrides[unrelatedKorean.Id], "Import discarded an unrelated target override.");
                Equal(sourceId, controller.ActivePresetId, "Importing into an inactive preset changed active identity.");
                Equal(activeConflictBeforeImport, GameTextCatalog.Latest.AppliedValues[conflict.Id], "Import applied the target conflict to runtime immediately.");
                Equal(activeFrenchBeforeImport, GameTextCatalog.Latest.AppliedValues[incomingFrench.Id], "Import applied a non-conflicting target draft to runtime immediately.");

                PresetImportPreview stale = controller.PreviewImport("exports/source-preset.json", targetId);
                controller.State.Stage(staleEntry, "changed after preview");
                Throws<InvalidOperationException>(() => controller.ConfirmImport(stale, overwriteConflicts: true));
                Equal("changed after preview", controller.State.Overrides[staleEntry.Id], "Rejecting a stale preview changed the target draft.");

                PresetImportPreview overwrite = controller.PreviewImport("exports/source-preset.json", targetId);
                controller.ConfirmImport(overwrite, overwriteConflicts: true);
                Equal("incoming conflict", controller.State.Overrides[conflict.Id], "Explicit overwrite did not replace the target conflict.");

                PresetImportPreview newTarget = controller.PreviewImport("exports/source-preset.json", null);
                Ensure(newTarget.CreatesPreset && newTarget.TargetPresetId == null, "The explicit new preset target was not previewed as creation.");
                Equal(2, controller.Presets.Count, "Previewing a new import created a preset before confirmation.");
                controller.ConfirmImport(newTarget, overwriteConflicts: false);
                string importedId = controller.SelectedPresetId;
                Equal("Controller source", newTarget.PresetName, "A new-target preview did not adopt the v2 share name.");
                Ensure(!StringComparer.Ordinal.Equals(importedId, sourceId) && !StringComparer.Ordinal.Equals(importedId, targetId), "Confirming a new import did not allocate a distinct ID.");
                Equal(sourceId, controller.ActivePresetId, "Confirming a new import changed active identity before SaveAndApply.");
                Equal(2, controller.GetPresetStatistics(importedId).OverrideCount, "Confirmed import did not create the target with incoming data.");
                Equal("Controller source", controller.GetPresetName(importedId), "A new imported preset did not retain the share name.");

                File.WriteAllText(Path.Combine(root, "active.json"), "legacy internal data");
                File.WriteAllText(Path.Combine(root, "previous.json"), "legacy internal backup");
                string[] importFiles = new List<string>(controller.GetImportFiles()).ToArray();
                Ensure(Array.Exists(importFiles, file => file == "exports/source-preset.json"), "The share file was missing from import choices.");
                Ensure(!Array.Exists(importFiles, file => file == "presets.json"), "presets.json was exposed as a share file.");
                Ensure(!Array.Exists(importFiles, file => file == "presets.previous.json"), "The workspace backup was exposed as a share file.");
                Ensure(!Array.Exists(importFiles, file => file == "active.json"), "The legacy active file was exposed as a share file.");
                Ensure(!Array.Exists(importFiles, file => file == "previous.json"), "The legacy active backup was exposed as a share file.");

                var legacyPatch = new EditorState();
                legacyPatch.Stage(incomingFrench, "legacy v1 value");
                Directory.CreateDirectory(Path.Combine(root, "exports"));
                File.WriteAllText(Path.Combine(root, "exports", "legacy-v1.json"), legacyPatch.ExportJson(PlayerStore.VERSION));
                PresetImportPreview legacyPreview = controller.PreviewImport("exports/legacy-v1.json", null);
                Equal(1, legacyPreview.Incoming.OverrideCount, "A 1.0 share file was not previewed as one modification.");
                controller.ConfirmImport(legacyPreview, overwriteConflicts: true);
                Equal("legacy v1 value", controller.State.Overrides[incomingFrench.Id], "A 1.0 share file did not import its original data.");
            }
        }
        finally
        {
            RemoveStore(root);
        }
    }

    private static void DeletingSavedPresetIsRetryable()
    {
        string root = NewStore();
        TextEntry entry = Entry("game", "en", "delete-preset", "original baseline");
        string workspacePath = Path.Combine(root, "presets.json");
        string secondId = null;
        try
        {
            using (EditorController controller = Open(root, entry))
            {
                string firstId = controller.SelectedPresetId;
                controller.State.Stage(entry, "first preset saved value");
                controller.SaveAndApply();
                secondId = controller.CreatePreset();
                controller.State.Stage(entry, "second preset saved value");
                controller.SaveAndApply();
                Equal(secondId, controller.ActivePresetId, "Saving the second preset did not make its ID active.");

                controller.SelectPreset(firstId);
                string previousWorkspace = File.ReadAllText(workspacePath);
                controller.DeletePreset(firstId);

                Equal(secondId, controller.SelectedPresetId, "Deleting the selected inactive preset did not fall back to the active ID.");
                Equal(secondId, controller.ActivePresetId, "Deleting an inactive preset changed active identity.");
                Equal("second preset saved value", controller.State.GetText(entry), "The active fallback did not restore its saved state.");
                Ensure(controller.HasUnsavedChanges, "Deleting a saved preset did not leave a dirty workspace draft.");
                Equal(1, controller.Presets.Count, "Deleting the inactive preset left it in the controller's list.");

                ModHelper.FailNextWrite("presets.json", afterPartialWrite: true);
                controller.SaveAndApply();
                Equal(previousWorkspace, File.ReadAllText(workspacePath), "A failed deletion save did not restore the original workspace file.");
                Ensure(controller.HasUnsavedChanges, "A failed deletion save discarded its retryable draft.");
                Equal(1, controller.Presets.Count, "A failed deletion save restored the removed preset in the draft.");

                controller.SaveAndApply();
                Ensure(!controller.HasUnsavedChanges, "Retrying the deletion save left the workspace dirty.");
                PresetWorkspace saved = PresetWorkspace.Load(File.ReadAllText(workspacePath), null);
                Equal(1, saved.Presets.Count, "Saving the deletion did not persist the one remaining preset.");
                Equal(secondId, saved.Presets[0].Id, "Saving deletion changed the surviving ID.");
                Equal(secondId, saved.ActivePresetId, "Saving deletion changed the active ID.");
            }

            using (EditorController restarted = Open(root, entry))
            {
                Equal(1, restarted.Presets.Count, "Restarting restored the deleted preset.");
                Equal(secondId, restarted.Presets[0].Id, "Restarting changed the surviving preset ID.");
                Equal(secondId, restarted.ActivePresetId, "Restarting changed the active preset ID.");
                Equal("second preset saved value", restarted.State.GetText(entry), "Restarting lost the surviving preset's saved value.");
            }
        }
        finally
        {
            RemoveStore(root);
        }
    }

    private static void FailedApplyRestoresWorkspace()
    {
        string root = NewStore();
        TextEntry entry = Entry("game", "en", "apply-rollback", "original baseline");
        try
        {
            using (EditorController controller = Open(root, entry))
            {
                controller.State.Stage(entry, "previously active value");
                controller.SaveAndApply();
                string activeId = controller.ActivePresetId;
                string previousWorkspace = File.ReadAllText(Path.Combine(root, "presets.json"));

                string selectedId = controller.CreatePreset();
                controller.State.Stage(entry, "new draft value");
                GameTextCatalog.Latest.FailNextApplyAfterPartial = true;
                controller.SaveAndApply();

                Equal(previousWorkspace, File.ReadAllText(Path.Combine(root, "presets.json")), "A failed runtime apply did not restore the previous workspace file.");
                Equal(activeId, controller.ActivePresetId, "A failed apply changed the active preset ID.");
                Equal(selectedId, controller.SelectedPresetId, "A failed apply discarded the selected preset ID.");
                Equal("new draft value", controller.State.GetText(entry), "A failed save discarded the draft.");
                Ensure(controller.HasUnsavedChanges, "A failed runtime apply marked drafts saved.");
                Equal("previously active value", GameTextCatalog.Latest.AppliedValues[entry.Id], "Runtime rollback did not restore the previous active overlay.");
            }
        }
        finally
        {
            RemoveStore(root);
        }
    }

    private static void FailedFirstWorkspaceWrite()
    {
        string root = NewStore();
        TextEntry entry = Entry("game", "en", "first-write-failure", "original");
        string workspacePath = Path.Combine(root, "presets.json");
        try
        {
            using (EditorController controller = Open(root, entry))
            {
                controller.State.Stage(entry, "unsaved replacement");
                ModHelper.FailNextWrite("presets.json", afterPartialWrite: true);

                controller.SaveAndApply();

                Ensure(!File.Exists(workspacePath), "A partial presets.json remained after the first write failed.");
                Ensure(controller.HasUnsavedChanges, "A failed first write marked the draft saved.");
                Equal(0, GameTextCatalog.Latest.AppliedValues.Count, "A failed first write applied a runtime override.");
            }
        }
        finally
        {
            RemoveStore(root);
        }
    }

    private static void RuntimeRollbackSurvivesDiskRollbackFailure()
    {
        string root = NewStore();
        TextEntry entry = Entry("game", "en", "rollback-failure", "original");
        string workspacePath = Path.Combine(root, "presets.json");
        try
        {
            using (EditorController controller = Open(root, entry))
            {
                controller.State.Stage(entry, "last active value");
                controller.SaveAndApply();
                string previousWorkspace = File.ReadAllText(workspacePath);
                controller.CreatePreset();
                controller.State.Stage(entry, "draft whose apply fails");
                GameTextCatalog.Latest.FailNextApplyAfterPartial = true;
                ModHelper.FailWriteAfterSuccessfulMatches("presets.json", 1, afterPartialWrite: true);

                controller.SaveAndApply();

                Equal("partial write", File.ReadAllText(workspacePath), "The injected disk rollback failure did not leave the expected partial workspace.");
                Equal(previousWorkspace, File.ReadAllText(Path.Combine(root, "presets.previous.json")), "The failed rollback did not preserve a usable workspace backup.");
                Equal("last active value", GameTextCatalog.Latest.AppliedValues[entry.Id], "Runtime rollback was skipped after disk rollback failed.");
                Equal("draft whose apply fails", controller.State.GetText(entry), "The failed draft was discarded.");
                Ensure(controller.HasUnsavedChanges, "The failed draft was marked saved.");

                controller.State.Stage(entry, "later protected draft");
                controller.SaveAndApply();

                Equal("partial write", File.ReadAllText(workspacePath), "A later save overwrote the file after rollback failure.");
                Equal("later protected draft", controller.State.GetText(entry), "The protected later draft was discarded.");
                Ensure(controller.HasUnsavedChanges, "The protected later draft was marked saved.");
                Equal("last active value", GameTextCatalog.Latest.AppliedValues[entry.Id], "A blocked save changed runtime text.");
            }
        }
        finally
        {
            RemoveStore(root);
        }
    }

    private static EditorController Open(string root, params TextEntry[] entries)
    {
        ModHelper.Configure(root);
        GameTextCatalog.SetEntries(entries, "en");
        var controller = new EditorController(new ModLog());
        try
        {
            controller.Initialize();
            Ensure(!controller.IsLoading, "The isolated coroutine runner did not complete initialization.");
            return controller;
        }
        catch
        {
            controller.Dispose();
            throw;
        }
    }

    private static string NewStore()
    {
        string path = Path.Combine(Path.GetTempPath(), "ProbablyEdited.ControllerChecks", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void RemoveStore(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    private static TextEntry Entry(string source, string locale, string key, string original)
    {
        var id = new TextId(source, locale, "controller-checks", key);
        return new TextEntry(
            id,
            source,
            string.Empty,
            id.TableId,
            id.Key,
            string.Empty,
            original,
            isFallback: false,
            isSmart: false,
            description: string.Empty);
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
