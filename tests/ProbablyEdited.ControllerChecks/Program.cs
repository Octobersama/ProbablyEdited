using System;
using System.Collections.Generic;
using System.IO;
using ProbablyEdited;
using System.Linq;

internal static class Program
{
    private static int Main()
    {
        int failures = 0;
        Run("fresh workspaces save and apply slot 01", FreshWorkspaceCanSave, ref failures);
        Run("rename-only changes save and reload without override edits", RenameOnlyChangesSaveAndReload, ref failures);
        Run("legacy active data migrates with all languages intact", MigratesLegacyActive, ref failures);
        Run("corrupt workspace never falls back to legacy active data", CorruptWorkspaceIsProtected, ref failures);
        Run("switching active presets preloads every required language", InactivePresetPreloadsEveryLanguage, ref failures);
        Run("single-preset export imports into the selected target with conflict choices", SharesOnePresetIntoTarget, ref failures);
        Run("runtime apply failure rolls back disk and runtime independently", FailedApplyRestoresWorkspace, ref failures);
        Run("failed first workspace write removes its partial file", FailedFirstWorkspaceWrite, ref failures);
        Run("runtime rollback still runs when disk rollback fails", RuntimeRollbackSurvivesDiskRollbackFailure, ref failures);

        Console.WriteLine(failures == 0 ? "All 9 controller checks passed." : $"{failures} controller check(s) failed.");
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
                Equal(1, controller.SelectedPresetSlot, "A fresh workspace did not select slot 01.");
                Equal(1, controller.ActivePresetSlot, "A fresh workspace did not start with slot 01 active.");

                controller.State.Stage(entry, "first saved replacement");
                controller.SaveAndApply();

                Ensure(File.Exists(workspacePath), "The first save did not create presets.json.");
                Ensure(!controller.HasUnsavedChanges, "A successful first save left workspace drafts dirty.");
                var saved = PresetWorkspace.Load(File.ReadAllText(workspacePath), null);
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
        try
        {
            File.WriteAllText(workspacePath, originalV1);
            using (EditorController controller = Open(root))
            {
                Equal("预设01", controller.GetPresetName(1), "A v1 workspace did not load its default name.");
                Ensure(!controller.HasUnsavedChanges, "Loading a v1 workspace marked it dirty before user changes.");
                Equal(originalV1, File.ReadAllText(workspacePath), "Loading a v1 workspace rewrote it during startup.");

                controller.RenamePreset(1, "  夜航  ");
                Equal("夜航", controller.GetPresetName(1), "The controller did not expose the normalized preset name.");
                Ensure(controller.HasUnsavedChanges, "A controller-only rename was not marked dirty.");

                ModHelper.FailNextWrite("presets.previous.json", afterPartialWrite: false);
                controller.SaveAndApply();
                Equal(originalV1, File.ReadAllText(workspacePath), "A failed backup write deleted or replaced the original v1 workspace.");
                Ensure(controller.HasUnsavedChanges, "A failed name-only save discarded its dirty state.");

                controller.SaveAndApply();
                Ensure(!controller.HasUnsavedChanges, "Saving a name-only change left the workspace dirty.");
                PresetWorkspace saved = PresetWorkspace.Load(File.ReadAllText(workspacePath), null);
                Equal("夜航", saved.Presets[0].Name, "The controller save did not persist the renamed slot.");
                Equal(originalV1, File.ReadAllText(Path.Combine(root, "presets.previous.json")), "Saving v1 data did not preserve the original workspace backup.");
            }

            using (EditorController restarted = Open(root))
                Equal("夜航", restarted.GetPresetName(1), "Restarting the controller lost the renamed slot name.");
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
        try
        {
            File.WriteAllText(legacyPath, legacyJson);
            using (EditorController controller = Open(root, english, french))
            {
                Ensure(!controller.HasUnsavedChanges, "A successfully migrated workspace was left dirty.");
                Ensure(File.Exists(Path.Combine(root, "presets.json")), "Legacy active data was not persisted as a workspace on first load.");
                Equal("Legacy English replacement", GameTextCatalog.Latest.AppliedValues[english.Id], "Migration lost the English legacy override.");
                Equal("Texte historique", GameTextCatalog.Latest.AppliedValues[french.Id], "Migration did not load and apply the French legacy override.");
                Ensure(GameTextCatalog.Latest.LoadCalls.Contains("fr"), "Migration did not preload the legacy French locale.");
                Equal(1, controller.Presets.Count, "Migration did not place legacy data in exactly slot 01.");
                Equal(1, controller.Presets[0].Slot, "Legacy data migrated to the wrong slot.");

                controller.SaveAndApply();

                Equal(legacyJson, File.ReadAllText(legacyPath), "Migration modified or removed legacy recovery data.");
                var workspace = PresetWorkspace.Load(File.ReadAllText(Path.Combine(root, "presets.json")), null);
                Equal(2, workspace.GetPresetStatistics(1).LanguageCount, "Migrated workspace did not retain both legacy languages.");
                Equal("Texte historique", workspace.State.Overrides[french.Id], "Migrated workspace changed the French locale identity or text.");
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
        Equal(2, seed.CreatePreset(), "The second preset did not receive stable slot ID 02.");
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

                controller.SelectPreset(2);
                Equal(1, controller.ActivePresetSlot, "Browsing slot 02 changed the active slot.");
                Equal("Active English", GameTextCatalog.Latest.AppliedValues[english.Id], "Browsing slot 02 applied its draft prematurely.");
                controller.SaveAndApply();

                Ensure(GameTextCatalog.Latest.LoadCalls.Contains("fr"), "SaveAndApply did not load the selected preset's French bindings.");
                Ensure(GameTextCatalog.Latest.LoadCalls.Contains("ko"), "SaveAndApply did not load the selected preset's Korean bindings.");
                Equal(2, controller.ActivePresetSlot, "Successful SaveAndApply did not activate slot 02.");
                Ensure(!GameTextCatalog.Latest.AppliedValues.ContainsKey(english.Id), "Switching presets left the previous English override active.");
                Equal("Draft French", GameTextCatalog.Latest.AppliedValues[french.Id], "The new French override was not applied.");
                Equal("Draft Korean", GameTextCatalog.Latest.AppliedValues[korean.Id], "The new Korean override was not applied.");

                int lastApply = GameTextCatalog.Latest.ApplySnapshots.Count - 1;
                Ensure(GameTextCatalog.Latest.ApplySnapshots[lastApply - 1].ContainsKey(english.Id), "The prior active overlay was not restored before applying slot 02.");
                Ensure(GameTextCatalog.Latest.ApplySnapshots[lastApply].ContainsKey(french.Id), "The final runtime snapshot omitted the French override.");
                Ensure(GameTextCatalog.Latest.ApplySnapshots[lastApply].ContainsKey(korean.Id), "The final runtime snapshot omitted the Korean override.");
                IReadOnlyList<string> loadedAtFinalApply = GameTextCatalog.Latest.LoadedLocalesAtApply[lastApply];
                Ensure(loadedAtFinalApply.Contains("en") && loadedAtFinalApply.Contains("fr") && loadedAtFinalApply.Contains("ko"), "The final apply began before every old-active and selected-preset locale was bound.");
            }

            using (EditorController restarted = Open(root, english, french, korean))
            {
                Equal(2, restarted.ActivePresetSlot, "Restart did not restore the newly saved active slot.");
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
                controller.State.Stage(conflict, "incoming conflict");
                controller.State.Stage(incomingFrench, "incoming French");
                controller.SaveAndApply();

                Equal(2, controller.CreatePreset(), "The target preset did not use slot 02.");
                controller.State.Stage(conflict, "local conflict");
                controller.State.Stage(unrelatedKorean, "keep unrelated value");
                controller.RenamePreset(1, "Controller source");
                controller.RenamePreset(2, "Controller target");
                controller.Export("source-preset", 1);

                string sharePath = Path.Combine(root, "exports", "source-preset.json");
                string shareJson = File.ReadAllText(sharePath);
                Ensure(shareJson.Contains("\"schemaVersion\": 2", StringComparison.Ordinal), "The new share file is not version 2.");
                Ensure(shareJson.Contains("\"presetName\": \"Controller source\"", StringComparison.Ordinal), "The v2 share metadata did not use the exported preset's actual name.");
                Ensure(shareJson.Contains("incoming conflict", StringComparison.Ordinal), "The chosen preset override was not exported.");
                Ensure(!shareJson.Contains("local conflict", StringComparison.Ordinal), "The export included another preset's conflict value.");
                Ensure(!shareJson.Contains("keep unrelated value", StringComparison.Ordinal), "The export included an unrelated preset override.");
                Ensure(!shareJson.Contains("Conflict baseline", StringComparison.Ordinal), "The export included original baseline text.");
                File.WriteAllText(Path.Combine(root, "exports", "workspace-schema.json"), File.ReadAllText(Path.Combine(root, "presets.json")));
                Throws<NotSupportedException>(() => controller.PreviewImport("exports/workspace-schema.json", 5));

                PresetImportPreview preview = controller.PreviewImport("exports/source-preset.json", 2);
                Equal("Controller target", preview.PresetName, "The import preview did not preserve the existing target name.");
                Equal(2, preview.Incoming.OverrideCount, "Incoming stats did not count the chosen preset's overrides.");
                Equal(2, preview.Incoming.LanguageCount, "Incoming language stats did not include all languages.");
                Equal(1, preview.Incoming.SourceCount, "Incoming source stats are incorrect.");
                Equal(2, preview.Existing.OverrideCount, "Existing stats did not describe only the target preset.");
                Equal(1, preview.ConflictCount, "Preview did not identify the target's conflicting value.");
                Equal(0, preview.UnavailableCount, "An override from an unloaded language was counted as missing.");
                Ensure(preview.Warnings.Any(warning => warning.Contains("en", StringComparison.Ordinal) && warning.Contains("未加载语言不计入", StringComparison.Ordinal)), "Unavailable-count scope was not stated for the currently loaded language.");

                string activeConflictBeforeImport = GameTextCatalog.Latest.AppliedValues[conflict.Id];
                string activeFrenchBeforeImport = GameTextCatalog.Latest.AppliedValues[incomingFrench.Id];
                controller.ConfirmImport(preview, overwriteConflicts: false);
                Equal("Controller target", controller.Presets[1].Name, "Importing into an existing slot changed its chosen name.");
                Equal("local conflict", controller.State.Overrides[conflict.Id], "A keep-conflicts import replaced the target value.");
                Equal("incoming French", controller.State.Overrides[incomingFrench.Id], "A non-conflicting language was not merged into the target.");
                Equal("keep unrelated value", controller.State.Overrides[unrelatedKorean.Id], "Import discarded an unrelated target override.");
                Equal(1, controller.ActivePresetSlot, "Importing into slot 02 changed the active slot.");
                Equal(activeConflictBeforeImport, GameTextCatalog.Latest.AppliedValues[conflict.Id], "Import applied the target conflict to runtime immediately.");
                Equal(activeFrenchBeforeImport, GameTextCatalog.Latest.AppliedValues[incomingFrench.Id], "Import applied a non-conflicting target draft to runtime immediately.");

                PresetImportPreview stale = controller.PreviewImport("exports/source-preset.json", 2);
                controller.State.Stage(staleEntry, "changed after preview");
                Throws<InvalidOperationException>(() => controller.ConfirmImport(stale, overwriteConflicts: true));
                Equal("changed after preview", controller.State.Overrides[staleEntry.Id], "Rejecting a stale preview changed the target draft.");

                PresetImportPreview overwrite = controller.PreviewImport("exports/source-preset.json", 2);
                controller.ConfirmImport(overwrite, overwriteConflicts: true);
                Equal("incoming conflict", controller.State.Overrides[conflict.Id], "Explicit overwrite did not replace the target conflict.");

                PresetImportPreview emptyTarget = controller.PreviewImport("exports/source-preset.json", 3);
                Ensure(emptyTarget.CreatesPreset, "Importing into an empty slot did not preview creation.");
                Equal(0, emptyTarget.Existing.OverrideCount, "An empty target reported existing overrides.");
                controller.ConfirmImport(emptyTarget, overwriteConflicts: false);
                Equal("Controller source", emptyTarget.PresetName, "The empty-slot preview did not adopt the v2 share name.");
                Equal(3, controller.SelectedPresetSlot, "Confirmed import did not select its chosen target slot.");
                Equal(1, controller.ActivePresetSlot, "Confirmed import activated the target without SaveAndApply.");
                Equal(2, controller.GetPresetStatistics(3).OverrideCount, "Confirmed import did not create the target with incoming data.");
                Equal("Controller source", controller.Presets[2].Name, "Importing into an empty slot did not adopt the v2 share name.");

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
                PresetImportPreview legacyPreview = controller.PreviewImport("exports/legacy-v1.json", 4);
                Equal(1, legacyPreview.Incoming.OverrideCount, "A 1.0 share file was not previewed as one modification.");
                controller.ConfirmImport(legacyPreview, overwriteConflicts: true);
                Equal("legacy v1 value", controller.State.Overrides[incomingFrench.Id], "A 1.0 share file did not import its original data.");
                Equal("预设04", controller.Presets[3].Name, "Importing a v1 share into an empty slot did not use its default name.");
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
                string previousWorkspace = File.ReadAllText(Path.Combine(root, "presets.json"));

                controller.CreatePreset();
                controller.State.Stage(entry, "new draft value");
                GameTextCatalog.Latest.FailNextApplyAfterPartial = true;
                controller.SaveAndApply();

                Equal(previousWorkspace, File.ReadAllText(Path.Combine(root, "presets.json")), "A failed runtime apply did not restore the previous workspace file.");
                Equal(1, controller.ActivePresetSlot, "A failed apply changed the active slot.");
                Equal(2, controller.SelectedPresetSlot, "A failed apply discarded the selected target.");
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
