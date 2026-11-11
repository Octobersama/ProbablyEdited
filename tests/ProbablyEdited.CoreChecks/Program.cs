using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ProbablyEdited;

internal static class Program
{
    private static int Main()
    {
        int failures = 0;
        Run("replacement text round-trips without exporting baselines", RoundTripsReplacementText, ref failures);
        Run("export ordering is independent of edit order", ExportsDeterministically, ref failures);
        Run("invalid imports are rejected without changing drafts", RejectsInvalidImportsAtomically, ref failures);
        Run("imports preserve conflicts unless overwrite is selected", MergesImportConflicts, ref failures);
        Run("locale changes retain overrides from other locales", RetainsOverridesAcrossLocales, ref failures);
        Run("unavailable imported keys remain saved and other locales are not missing", RetainsUnavailableOverrides, ref failures);
        Run("saved snapshots track stage, revert, and restore", TracksSavedSnapshots, ref failures);
        Run("preview plans cannot apply after state changes", RejectsStalePlans, ref failures);
        Run("text edit history restores undo and redo snapshots", TextHistoryRestoresSnapshots, ref failures);
        Run("text edit history clears redo after a new edit", TextHistoryClearsRedoAfterEditing, ref failures);
        Run("text edit history reset starts from the current target", TextHistoryResetsTarget, ref failures);
        Run("text edit history retains selection positions", TextHistoryRetainsSelection, ref failures);

        Console.WriteLine(failures == 0 ? "All 12 Core checks passed." : $"{failures} Core check(s) failed.");
        return failures == 0 ? 0 : 1;
    }

    private static void TextHistoryRestoresSnapshots()
    {
        var history = new TextEditHistory();
        history.Reset("hello", 5, 5);
        history.Record("hello!", 6, 6);
        history.Record("hello!!", 7, 7);

        Ensure(history.TryUndo(out TextEditSnapshot undone), "Undo was unavailable after two edits.");
        Equal("hello!", undone.Text, "Undo did not restore the previous text.");
        Ensure(history.TryRedo(out TextEditSnapshot redone), "Redo was unavailable after undo.");
        Equal("hello!!", redone.Text, "Redo did not restore the edited text.");
    }

    private static void TextHistoryClearsRedoAfterEditing()
    {
        var history = new TextEditHistory();
        history.Reset("a", 1, 1);
        history.Record("ab", 2, 2);
        history.Record("abc", 3, 3);
        Ensure(history.TryUndo(out TextEditSnapshot undone), "Undo was unavailable after typing.");
        Equal("ab", undone.Text, "Undo restored the wrong text before branching.");

        history.Record("aB", 2, 2);
        Ensure(!history.CanRedo, "A new edit after undo left redo history available.");
        Ensure(history.TryUndo(out undone), "The branch edit could not be undone.");
        Equal("ab", undone.Text, "Undo did not return to the branch point.");
        Ensure(history.TryRedo(out TextEditSnapshot redone), "The branch edit could not be redone.");
        Equal("aB", redone.Text, "Redo restored a discarded edit instead of the branch.");
    }

    private static void TextHistoryResetsTarget()
    {
        var history = new TextEditHistory();
        history.Reset("first target", 12, 12);
        history.Record("first target edit", 17, 17);
        history.Reset("second target", 13, 13);

        Ensure(!history.CanUndo, "A target reset retained the previous target's undo history.");
        Equal("second target", history.Current.Text, "A target reset did not establish the current text.");
    }

    private static void TextHistoryRetainsSelection()
    {
        var history = new TextEditHistory();
        history.Reset("abcdef", 5, 2);
        history.Record("abcXdef", 4, 4);

        Ensure(history.TryUndo(out TextEditSnapshot undone), "Undo was unavailable for selection restoration.");
        Equal(5, undone.AnchorPosition, "Undo lost the selection anchor.");
        Equal(2, undone.FocusPosition, "Undo lost the selection focus.");
    }

    private static void RoundTripsReplacementText()
    {
        TextEntry multiline = Entry("mod-a", "zh", "Localization", "greeting", "PRIVATE_BASELINE_ONE");
        TextEntry empty = Entry("mod-a", "zh", "Localization", "blank", "PRIVATE_BASELINE_TWO");
        var state = new ProbablyEdited.EditorState();
        state.SetCatalog(new[] { multiline, empty });
        state.Stage(multiline, "你好，🌙\r\n第二行\n第三行");
        state.Stage(empty, string.Empty);

        string json = state.ExportJson("fixture");
        Ensure(!json.Contains(multiline.OriginalText, StringComparison.Ordinal), "Export included the first baseline text.");
        Ensure(!json.Contains(empty.OriginalText, StringComparison.Ordinal), "Export included the second baseline text.");

        var loaded = new ProbablyEdited.EditorState();
        loaded.SetCatalog(new[] { multiline, empty });
        loaded.LoadSaved(json);
        Equal("你好，🌙\r\n第二行\n第三行", loaded.GetText(multiline), "Multiline Unicode replacement changed during round-trip.");
        Equal(string.Empty, loaded.GetText(empty), "Empty replacement changed during round-trip.");
        Ensure(loaded.IsModified(empty), "An empty replacement was not retained as an override.");
        Ensure(!loaded.IsDirty, "Loading saved overrides should establish a clean snapshot.");
    }

    private static void ExportsDeterministically()
    {
        TextEntry first = Entry("mod-a", "en", "Localization", "first", "base-one");
        TextEntry second = Entry("game", "en", "table-b", "2", "base-two");
        TextEntry third = Entry("mod-z", "en", "Localization", "third", "base-three");
        var entries = new[] { first, second, third };
        var forward = new ProbablyEdited.EditorState();
        var reverse = new ProbablyEdited.EditorState();
        forward.SetCatalog(entries);
        reverse.SetCatalog(entries);

        forward.Stage(first, "replacement-one");
        forward.Stage(second, "replacement-two");
        forward.Stage(third, "replacement-three");
        reverse.Stage(third, "replacement-three");
        reverse.Stage(second, "replacement-two");
        reverse.Stage(first, "replacement-one");

        Equal(forward.ExportJson("fixture"), reverse.ExportJson("fixture"), "The same overrides produced order-dependent JSON.");
    }

    private static void RejectsInvalidImportsAtomically()
    {
        TextId id = new TextId("mod-a", "en", "Localization", "greeting");
        TextEntry localEntry = Entry(id, "PRIVATE_LOCAL_BASELINE");
        var state = new ProbablyEdited.EditorState();
        state.SetCatalog(new[] { localEntry });
        state.Stage(localEntry, "local draft");
        Dictionary<TextId, string> before = Snapshot(state);
        string validPatch = CreatePatch(new[] { id }, new[] { "incoming" });

        Throws<FormatException>(() => state.PreviewImport("{", "fixture"));

        JObject duplicateRoot = JObject.Parse(validPatch);
        JArray duplicateItems = (JArray)duplicateRoot["overrides"];
        duplicateItems.Add(duplicateItems[0].DeepClone());
        Throws<FormatException>(() => state.PreviewImport(duplicateRoot.ToString(Formatting.None), "fixture"));

        JObject nullRoot = JObject.Parse(validPatch);
        ((JObject)((JArray)nullRoot["overrides"])[0])["text"] = JValue.CreateNull();
        Throws<FormatException>(() => state.PreviewImport(nullRoot.ToString(Formatting.None), "fixture"));

        JObject foreignRoot = JObject.Parse(validPatch);
        foreignRoot["schema"] = "foreign-format";
        Throws<NotSupportedException>(() => state.PreviewImport(foreignRoot.ToString(Formatting.None), "fixture"));

        JObject futureVersionRoot = JObject.Parse(validPatch);
        futureVersionRoot["schemaVersion"] = 999;
        Throws<NotSupportedException>(() => state.PreviewImport(futureVersionRoot.ToString(Formatting.None), "fixture"));

        SameOverrides(before, state, "A rejected import changed the local draft.");
    }

    private static void MergesImportConflicts()
    {
        TextEntry conflict = Entry("mod-a", "en", "Localization", "conflict", "base-conflict");
        TextEntry same = Entry("mod-a", "en", "Localization", "same", "base-same");
        TextEntry added = Entry("mod-a", "en", "Localization", "added", "base-added");
        TextId[] ids = { conflict.Id, same.Id, added.Id };
        var state = new ProbablyEdited.EditorState();
        state.SetCatalog(new[] { conflict, same, added });
        state.Stage(conflict, "local value");
        state.Stage(same, "shared value");
        string incoming = CreatePatch(ids, new[] { "incoming value", "shared value", "new value" });

        ProbablyEdited.ImportPlan plan = state.PreviewImport(incoming, "fixture");
        Equal(3, plan.TotalCount, "Import count did not include every patch entry.");
        Equal(1, plan.ConflictCount, "Same-value entries should not count as conflicts.");
        state.ApplyImport(plan, overwriteConflicts: false);
        Equal("local value", state.GetText(conflict), "A conflict replaced local text without consent.");
        Equal("shared value", state.GetText(same), "An identical incoming override changed the result.");
        Equal("new value", state.GetText(added), "A non-conflicting incoming override was not applied.");

        plan = state.PreviewImport(incoming, "fixture");
        state.ApplyImport(plan, overwriteConflicts: true);
        Equal("incoming value", state.GetText(conflict), "Explicit overwrite did not replace the local conflict.");
    }

    private static void RetainsOverridesAcrossLocales()
    {
        TextEntry french = Entry("mod-a", "fr", "Localization", "title", "titre initial");
        TextEntry english = Entry("mod-a", "en", "Localization", "title", "initial title");
        var state = new ProbablyEdited.EditorState();
        state.SetCatalog(new[] { french });
        state.Stage(french, "titre modifié");
        state.SetCatalog(new[] { english });
        state.Stage(english, "edited title");

        Ensure(state.Overrides.ContainsKey(french.Id), "Switching to English discarded the French override.");
        Ensure(state.Overrides.ContainsKey(english.Id), "The English override was not staged.");
        Equal(0, state.UnavailableOverrideCount, "A French override was counted missing in the English catalog.");

        state.SetCatalog(new[] { french });
        Equal("titre modifié", state.GetText(french), "Returning to French did not restore its override.");
        Equal("edited title", state.GetText(english), "Changing catalogs discarded the English override.");
        Equal(0, state.UnavailableOverrideCount, "An English override was counted missing in the French catalog.");
    }

    private static void RetainsUnavailableOverrides()
    {
        TextEntry knownEnglish = Entry("mod-a", "en", "Localization", "known", "known baseline");
        TextId missingEnglish = new TextId("missing-mod", "en", "Localization", "new-key");
        TextId missingFrench = new TextId("missing-mod", "fr", "Localization", "autre-cle");
        var state = new ProbablyEdited.EditorState();
        state.SetCatalog(new[] { knownEnglish });
        string patch = CreatePatch(
            new[] { missingEnglish, missingFrench },
            new[] { "English replacement", "Remplacement français" });

        ProbablyEdited.ImportPlan plan = state.PreviewImport(patch, "fixture");
        Equal(1, plan.UnavailableCount, "Unavailable count included a key from another locale.");
        Ensure(plan.Warnings.Count > 0, "Unavailable entries did not produce a preview warning.");
        state.ApplyImport(plan, overwriteConflicts: false);
        Ensure(state.Overrides.ContainsKey(missingEnglish), "An unavailable English override was discarded.");
        Ensure(state.Overrides.ContainsKey(missingFrench), "A non-current French override was discarded.");
        Equal(1, state.UnavailableOverrideCount, "Unavailable saved count included another locale or lost the missing key.");

        var reloaded = new ProbablyEdited.EditorState();
        reloaded.SetCatalog(new[] { knownEnglish });
        reloaded.LoadSaved(state.ExportJson("fixture"));
        Equal("English replacement", reloaded.Overrides[missingEnglish], "An unavailable override did not survive save/load.");
        Equal("Remplacement français", reloaded.Overrides[missingFrench], "An override for another locale did not survive save/load.");
    }

    private static void TracksSavedSnapshots()
    {
        TextEntry entry = Entry("mod-a", "en", "Localization", "title", "original");
        var state = new ProbablyEdited.EditorState();
        state.SetCatalog(new[] { entry });
        state.Stage(entry, "saved value");
        state.MarkSaved();
        Ensure(!state.IsDirty, "MarkSaved did not capture the current override snapshot.");

        state.Stage(entry, "temporary value");
        Ensure(state.IsDirty, "A changed draft was not marked dirty.");
        state.Stage(entry, "saved value");
        Ensure(!state.IsDirty, "Reverting to the saved snapshot remained dirty.");

        state.Restore(entry);
        Ensure(state.IsDirty, "Restoring a saved override did not mark the state dirty.");
        Equal(0, state.OverrideCount, "Restore did not remove the override.");
        state.MarkSaved();
        Ensure(!state.IsDirty, "Saving the restored state did not clear dirty state.");

        state.Stage(entry, "another value");
        state.MarkSaved();
        Ensure(!state.IsDirty, "MarkSaved did not capture the staged override.");
        state.Stage(entry, entry.OriginalText);
        Equal(0, state.OverrideCount, "Staging the original value did not remove the override.");
        Ensure(state.IsDirty, "Removing a saved override did not mark the state dirty.");
    }

    private static void RejectsStalePlans()
    {
        TextEntry entry = Entry("mod-a", "en", "Localization", "title", "original");
        TextId incomingId = new TextId("mod-b", "en", "Localization", "new-key");
        var state = new ProbablyEdited.EditorState();
        state.SetCatalog(new[] { entry });
        string patch = CreatePatch(new[] { incomingId }, new[] { "incoming" });

        ProbablyEdited.ImportPlan plan = state.PreviewImport(patch, "fixture");
        state.Stage(entry, "local change");
        Dictionary<TextId, string> afterDraftChange = Snapshot(state);
        Throws<InvalidOperationException>(() => state.ApplyImport(plan, overwriteConflicts: true));
        SameOverrides(afterDraftChange, state, "Applying a stale draft preview changed overrides.");

        plan = state.PreviewImport(patch, "fixture");
        state.SetCatalog(new[] { entry });
        Dictionary<TextId, string> afterCatalogChange = Snapshot(state);
        Throws<InvalidOperationException>(() => state.ApplyImport(plan, overwriteConflicts: true));
        SameOverrides(afterCatalogChange, state, "Applying a stale catalog preview changed overrides.");
    }

    private static string CreatePatch(TextId[] ids, string[] replacements)
    {
        if (ids.Length != replacements.Length)
            throw new ArgumentException("Each patch identity must have one replacement.");

        var state = new ProbablyEdited.EditorState();
        for (int index = 0; index < ids.Length; index++)
            state.Stage(Entry(ids[index], "synthetic baseline"), replacements[index]);

        return state.ExportJson("fixture");
    }

    private static TextEntry Entry(string sourceId, string locale, string tableId, string key, string original)
    {
        return Entry(new TextId(sourceId, locale, tableId, key), original);
    }

    private static TextEntry Entry(TextId id, string original)
    {
        return new TextEntry(
            id,
            id.SourceId,
            string.Empty,
            id.TableId,
            id.Key,
            string.Empty,
            original,
            isFallback: false,
            isSmart: false,
            description: string.Empty);
    }

    private static Dictionary<TextId, string> Snapshot(ProbablyEdited.EditorState state)
    {
        var snapshot = new Dictionary<TextId, string>();
        foreach (KeyValuePair<TextId, string> pair in state.Overrides)
            snapshot.Add(pair.Key, pair.Value);
        return snapshot;
    }

    private static void SameOverrides(
        IReadOnlyDictionary<TextId, string> expected,
        ProbablyEdited.EditorState state,
        string message)
    {
        Equal(expected.Count, state.Overrides.Count, message);
        foreach (KeyValuePair<TextId, string> pair in expected)
        {
            Ensure(state.Overrides.TryGetValue(pair.Key, out string value), message);
            Equal(pair.Value, value, message);
        }
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

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
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
