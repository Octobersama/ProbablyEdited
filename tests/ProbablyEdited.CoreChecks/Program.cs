using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ProbablyEdited;

internal static class Program
{
    private static int Main()
    {
        TestLocalization.Initialize();
        int failures = 0;
        Run("replacement text round-trips without exporting baselines", RoundTripsReplacementText, ref failures);
        Run("source choices keep game first and All sources last", SourceChoicesKeepGameFirstAndAllLast, ref failures);
        Run("export ordering is independent of edit order", ExportsDeterministically, ref failures);
        Run("invalid imports are rejected without changing drafts", RejectsInvalidImportsAtomically, ref failures);
        Run("imports preserve conflicts unless overwrite is selected", MergesImportConflicts, ref failures);
        Run("locale changes retain overrides from other locales", RetainsOverridesAcrossLocales, ref failures);
        Run("unavailable imported keys remain saved and other locales are not missing", RetainsUnavailableOverrides, ref failures);
        Run("saved snapshots track stage, revert, and restore", TracksSavedSnapshots, ref failures);
        Run("preview plans cannot apply after state changes", RejectsStalePlans, ref failures);
        Run("typing bursts form one undo step", TextHistoryGroupsTypingBurst, ref failures);
        Run("typing pauses start a new undo step", TextHistorySplitsAfterPause, ref failures);
        Run("continuous typing is capped at one second per group", TextHistorySplitsAtMaximumDuration, ref failures);
        Run("undo immediately flushes the current typing group", TextHistoryUndoFlushesCurrentGroup, ref failures);
        Run("atomic edits stay separate from nearby typing", TextHistoryKeepsAtomicEditsSeparate, ref failures);
        Run("insertion and deletion directions start separate groups", TextHistorySeparatesDirections, ref failures);
        Run("cursor jumps split typing groups", TextHistoryCursorJumpStartsNewGroup, ref failures);
        Run("a new edit after undo clears redo", TextHistoryClearsRedoAfterEditing, ref failures);
        Run("target reset starts fresh history", TextHistoryResetsTarget, ref failures);
        Run("selection-only replay preserves redo", TextHistoryPreservesRedoAcrossSelectionChanges, ref failures);
        Run("histories stay isolated per input", TextHistoryInstancesRemainIndependent, ref failures);
        Run("history retains UTF-16 selection positions", TextHistoryRetainsSelection, ref failures);
        Run("multi-character commits stay one undo step", TextHistoryGroupsMultiCharacterCommits, ref failures);
        Run("history supports repeated undo and redo", TextHistoryRestoresSnapshots, ref failures);
        Run("history shortcuts use platform command conventions", TextHistoryShortcutCommands, ref failures);
        Run("history shortcuts respect IME and ordinary characters", TextHistoryShortcutCharacterBoundaries, ref failures);

        Console.WriteLine(failures == 0 ? "All 25 Core checks passed." : $"{failures} Core check(s) failed.");
        return failures == 0 ? 0 : 1;
    }

    private static void SourceChoicesKeepGameFirstAndAllLast()
    {
        var sourceIds = new HashSet<string>(new[] { "zeta", "game", "alpha" }, StringComparer.Ordinal);
        List<string> choices = SourceFilterChoices.BuildValues(sourceIds, "game");
        Equal("game", choices[0], "The game tables were not first.");
        Equal("alpha", choices[1], "Mod sources were not sorted after the game tables.");
        Equal("zeta", choices[2], "Mod sources were not sorted after the game tables.");
        Equal(null, choices[3], "All sources was not the final option.");
        Equal("game", SourceFilterChoices.ResolveSelection(sourceIds, null, false, "game"), "Game tables were not selected by default.");

        Equal("zeta", SourceFilterChoices.ResolveSelection(sourceIds, "zeta", false, "game"), "A valid explicit source selection was not retained.");
        Equal("game", SourceFilterChoices.ResolveSelection(sourceIds, "removed", false, "game"), "An unavailable source did not fall back to game tables.");
        Equal(null, SourceFilterChoices.ResolveSelection(new HashSet<string> { "mod" }, "removed", false, "game"), "An unavailable source did not fall back to All sources when game tables were absent.");
        Equal(null, SourceFilterChoices.ResolveSelection(sourceIds, null, true, "game"), "An explicit All sources selection was not retained.");
    }

    private static void TextHistoryShortcutCommands()
    {
        Equal(TextHistoryCommand.Undo, TextHistoryShortcut.Match(false, true, false, false, false, true, false, false), "Ctrl+Z must undo.");
        Equal(TextHistoryCommand.Redo, TextHistoryShortcut.Match(false, true, false, false, false, false, true, false), "Ctrl+Y must redo.");
        Equal(TextHistoryCommand.Redo, TextHistoryShortcut.Match(false, true, false, true, false, true, false, false), "Ctrl+Shift+Z must redo.");
        Equal(TextHistoryCommand.Undo, TextHistoryShortcut.Match(true, false, true, false, false, true, false, false), "Command+Z must undo on macOS.");
        Equal(TextHistoryCommand.Redo, TextHistoryShortcut.Match(true, false, true, true, false, true, false, false), "Command+Shift+Z must redo on macOS.");
        Equal(TextHistoryCommand.None, TextHistoryShortcut.Match(true, true, false, false, false, true, false, false), "Control is not the macOS primary modifier.");
        Equal(TextHistoryCommand.None, TextHistoryShortcut.Match(true, false, true, false, false, false, true, false), "Command+Y is not the selected macOS redo chord.");
    }

    private static void TextHistoryShortcutCharacterBoundaries()
    {
        Equal(TextHistoryCommand.None, TextHistoryShortcut.Match(false, true, false, false, true, true, false, false), "Alt-modified input must not undo.");
        Equal(TextHistoryCommand.None, TextHistoryShortcut.Match(false, true, false, false, false, true, false, true), "IME composition must not trigger history commands.");
        Ensure(TextHistoryShortcut.BlocksCharacter(TextHistoryCommand.Undo, 'z'), "A shortcut must not append its letter.");
        Ensure(TextHistoryShortcut.BlocksCharacter(TextHistoryCommand.Undo, '\u001a'), "A shortcut must not append its control character.");
        Ensure(TextHistoryShortcut.BlocksCharacter(TextHistoryCommand.Redo, 'Y'), "Redo must not append its letter.");
        Ensure(!TextHistoryShortcut.BlocksCharacter(TextHistoryCommand.None, 'z'), "Ordinary typing must retain z.");
        Ensure(!TextHistoryShortcut.BlocksCharacter(TextHistoryCommand.Undo, '字'), "The guard must not filter unrelated Unicode text.");
    }

    private static void TextHistoryGroupsTypingBurst()
    {
        var clock = new HistoryClock();
        TextEditHistory history = clock.CreateHistory();
        history.Reset(string.Empty, 0, 0);

        clock.Now = 0.1d;
        history.Record("a", 1, 1);
        clock.Now = 0.2d;
        history.Record("aa", 2, 2);
        clock.Now = 0.3d;
        history.Record("aaa", 3, 3);

        Ensure(history.TryUndo(out TextEditSnapshot undone), "Typing burst had no undo step.");
        Equal(string.Empty, undone.Text, "One undo did not remove the whole typing burst.");
        Ensure(!history.CanUndo, "A burst of adjacent typing created multiple undo steps.");
        Ensure(history.TryRedo(out TextEditSnapshot redone), "Typing burst could not be redone.");
        Equal("aaa", redone.Text, "Redo did not restore the full typing burst.");
    }

    private static void TextHistorySplitsAfterPause()
    {
        var clock = new HistoryClock();
        TextEditHistory history = clock.CreateHistory();
        history.Reset(string.Empty, 0, 0);

        clock.Now = 0.1d;
        history.Record("a", 1, 1);
        clock.Now = 0.2d;
        history.Record("ab", 2, 2);
        clock.Now = 0.56d;
        history.Record("abc", 3, 3);

        Ensure(history.TryUndo(out TextEditSnapshot undone), "Typing after a pause had no undo step.");
        Equal("ab", undone.Text, "Typing after an idle pause merged with earlier typing.");
        Ensure(history.TryUndo(out undone), "The first typing group could not be undone.");
        Equal(string.Empty, undone.Text, "Undo did not restore text before both typing groups.");
    }

    private static void TextHistorySplitsAtMaximumDuration()
    {
        var clock = new HistoryClock();
        TextEditHistory history = clock.CreateHistory();
        history.Reset(string.Empty, 0, 0);

        clock.Now = 0d;
        history.Record("a", 1, 1);
        clock.Now = 0.3d;
        history.Record("ab", 2, 2);
        clock.Now = 0.6d;
        history.Record("abc", 3, 3);
        clock.Now = 0.9d;
        history.Record("abcd", 4, 4);
        clock.Now = 1.05d;
        history.Record("abcde", 5, 5);

        Ensure(history.TryUndo(out TextEditSnapshot undone), "Typing past the maximum group duration had no undo step.");
        Equal("abcd", undone.Text, "Continuous typing exceeded the maximum group duration.");
        Ensure(history.TryUndo(out undone), "The first maximum-duration group could not be undone.");
        Equal(string.Empty, undone.Text, "The first group did not restore its starting text.");
    }

    private static void TextHistoryUndoFlushesCurrentGroup()
    {
        var clock = new HistoryClock();
        TextEditHistory history = clock.CreateHistory();
        history.Reset("start", 5, 5);
        clock.Now = 0.1d;
        history.Record("start ", 6, 6);
        clock.Now = 0.2d;
        history.Record("start a", 7, 7);

        Ensure(history.TryUndo(out TextEditSnapshot undone), "Undo was unavailable before the idle boundary.");
        Equal("start", undone.Text, "Immediate undo did not flush the active typing group.");
        Ensure(!history.CanUndo, "Immediate undo left part of the active group behind.");
        Ensure(history.TryRedo(out TextEditSnapshot redone), "Immediate undo could not be redone.");
        Equal("start a", redone.Text, "Redo did not restore all text typed before immediate undo.");
    }

    private static void TextHistoryKeepsAtomicEditsSeparate()
    {
        var clock = new HistoryClock();
        TextEditHistory history = clock.CreateHistory();
        history.Reset("abcd", 4, 4);
        clock.Now = 0.1d;
        history.Record("abcde", 5, 5);
        clock.Now = 0.2d;
        history.RecordAtomic("abXde", 3, 3);
        clock.Now = 0.3d;
        history.Record("abX!de", 4, 4);

        Ensure(history.TryUndo(out TextEditSnapshot undone), "Typing after an atomic edit had no undo step.");
        Equal("abXde", undone.Text, "Typing after an atomic edit did not remain a separate undo step.");
        Ensure(history.TryUndo(out undone), "The atomic range replacement had no undo step.");
        Equal("abcde", undone.Text, "Undo did not restore text before the atomic replacement.");
        Ensure(history.TryUndo(out undone), "Typing before the atomic replacement had no undo step.");
        Equal("abcd", undone.Text, "Undo did not restore text before the typing group.");

        history.Reset("abcdef", 6, 6);
        clock.Now = 0.4d;
        history.RecordAtomic("adef", 1, 1);
        Ensure(history.TryUndo(out undone), "An atomic range deletion had no undo step.");
        Equal("abcdef", undone.Text, "Undo did not restore text removed by the range deletion.");

        history.Reset("a", 1, 1);
        clock.Now = 0.5d;
        history.Record("ab", 2, 2);
        clock.Now = 0.6d;
        history.RecordAtomic("abc", 3, 3);
        Ensure(history.TryUndo(out undone), "A single-character paste had no undo step.");
        Equal("ab", undone.Text, "A single-character paste merged into nearby typing.");
        Ensure(history.TryUndo(out undone), "Typing before a single-character paste had no undo step.");
        Equal("a", undone.Text, "Undo did not restore text before the paste.");
    }

    private static void TextHistorySeparatesDirections()
    {
        var clock = new HistoryClock();
        TextEditHistory history = clock.CreateHistory();
        history.Reset("a", 1, 1);
        clock.Now = 0.1d;
        history.Record("ab", 2, 2);
        clock.Now = 0.2d;
        history.Record("a", 1, 1);

        Ensure(history.TryUndo(out TextEditSnapshot undone), "Backspace after insertion had no undo step.");
        Equal("ab", undone.Text, "Backspace merged into the preceding insertion.");
        Ensure(history.TryUndo(out undone), "The insertion before backspace had no undo step.");
        Equal("a", undone.Text, "Undo did not restore text before insertion.");

        history.Reset("abcd", 2, 2);
        clock.Now = 0.3d;
        history.Record("acd", 1, 1);
        clock.Now = 0.4d;
        history.Record("ad", 1, 1);
        Ensure(history.TryUndo(out undone), "Forward delete after backspace had no undo step.");
        Equal("acd", undone.Text, "Forward delete merged into backspace.");
        Ensure(history.TryUndo(out undone), "Backspace before forward delete had no undo step.");
        Equal("abcd", undone.Text, "Undo did not restore text before backspace.");
    }

    private static void TextHistoryCursorJumpStartsNewGroup()
    {
        var clock = new HistoryClock();
        TextEditHistory history = clock.CreateHistory();
        history.Reset("aaa", 3, 3);
        clock.Now = 0.1d;
        history.Record("aaaa", 4, 4);
        history.UpdateCurrentSelection(1, 1);
        clock.Now = 0.2d;
        history.Record("aaaaa", 2, 2);
        clock.Now = 0.3d;
        history.Record("aaaaaa", 3, 3);

        Ensure(history.TryUndo(out TextEditSnapshot undone), "Typing after a cursor jump had no undo step.");
        Equal("aaaa", undone.Text, "Typing after a cursor jump merged into the earlier group.");
        Ensure(history.TryUndo(out undone), "The typing group before the cursor jump had no undo step.");
        Equal("aaa", undone.Text, "Undo did not restore text before the cursor jump.");
    }

    private static void TextHistoryRestoresSnapshots()
    {
        var clock = new HistoryClock();
        TextEditHistory history = clock.CreateHistory();
        history.Reset("hello", 5, 5);
        history.RecordAtomic("hello!", 6, 6);
        history.RecordAtomic("hello!!", 7, 7);
        history.RecordAtomic("hello!!!", 8, 8);

        Ensure(history.TryUndo(out TextEditSnapshot undone), "First undo was unavailable after three atomic edits.");
        Equal("hello!!", undone.Text, "First undo restored the wrong text.");
        Ensure(history.TryUndo(out undone), "Second undo was unavailable.");
        Equal("hello!", undone.Text, "Second undo restored the wrong text.");
        Ensure(history.TryUndo(out undone), "Undo did not return to the initial text.");
        Equal("hello", undone.Text, "Final undo did not restore the initial text.");
        Ensure(!history.CanUndo, "Undo remained available before the initial snapshot.");

        Ensure(history.TryRedo(out TextEditSnapshot redone), "First redo was unavailable after repeated undo.");
        Equal("hello!", redone.Text, "First redo restored the wrong text.");
        Ensure(history.TryRedo(out redone), "Second redo was unavailable.");
        Equal("hello!!", redone.Text, "Second redo restored the wrong text.");
        Ensure(history.TryRedo(out redone), "Redo did not restore the latest edit.");
        Equal("hello!!!", redone.Text, "Final redo restored the wrong text.");
        Ensure(!history.CanRedo, "Redo remained available beyond the latest snapshot.");
    }

    private static void TextHistoryClearsRedoAfterEditing()
    {
        var clock = new HistoryClock();
        TextEditHistory history = clock.CreateHistory();
        history.Reset("a", 1, 1);
        clock.Now = 0.1d;
        history.Record("ab", 2, 2);
        clock.Now = 0.2d;
        history.Record("abc", 3, 3);
        Ensure(history.TryUndo(out TextEditSnapshot undone), "Undo was unavailable after typing.");
        Equal("a", undone.Text, "Undo did not return to the start of the typing group.");

        clock.Now = 0.3d;
        history.Record("aB", 2, 2);
        Ensure(!history.CanRedo, "A real edit after undo left discarded redo history available.");
        Ensure(history.TryUndo(out undone), "The branch edit could not be undone.");
        Equal("a", undone.Text, "Undo did not return to the branch point.");
        Ensure(history.TryRedo(out TextEditSnapshot redone), "The branch edit could not be redone.");
        Equal("aB", redone.Text, "Redo restored a discarded edit instead of the branch.");
    }

    private static void TextHistoryResetsTarget()
    {
        var clock = new HistoryClock();
        TextEditHistory history = clock.CreateHistory();
        history.Reset("first target", 12, 12);
        history.RecordAtomic("first target edit", 17, 17);
        history.Reset("second target", 13, 13);

        Ensure(!history.CanUndo, "A target reset retained the previous target's undo history.");
        Equal("second target", history.Current.Text, "A target reset did not establish the current text.");
    }

    private static void TextHistoryPreservesRedoAcrossSelectionChanges()
    {
        var clock = new HistoryClock();
        TextEditHistory history = clock.CreateHistory();
        history.Reset("initial", 7, 7);
        history.RecordAtomic("edited", 6, 6);

        Ensure(history.TryUndo(out TextEditSnapshot undone), "Undo was unavailable before selection-only changes.");
        Equal("initial", undone.Text, "Undo restored the wrong text before selection-only changes.");
        history.UpdateCurrentSelection(2, 4);
        history.Record("initial", 1, 3);

        Ensure(history.CanRedo, "Selection changes or a same-text callback discarded redo history.");
        Ensure(history.TryRedo(out TextEditSnapshot redone), "Redo was unavailable after selection-only changes.");
        Equal("edited", redone.Text, "Redo restored the wrong text after selection-only changes.");
    }

    private static void TextHistoryInstancesRemainIndependent()
    {
        var clock = new HistoryClock();
        TextEditHistory first = clock.CreateHistory();
        TextEditHistory second = clock.CreateHistory();
        first.Reset("first", 5, 5);
        first.RecordAtomic("first edit", 10, 10);
        second.Reset("second", 6, 6);
        second.RecordAtomic("second edit", 11, 11);

        Ensure(first.TryUndo(out TextEditSnapshot firstUndo), "First history could not undo its own edit.");
        Equal("first", firstUndo.Text, "First history restored another target's text.");
        Equal("second edit", second.Current.Text, "Undo in the first history changed the second history.");
        Ensure(second.TryUndo(out TextEditSnapshot secondUndo), "Second history lost its independent undo step.");
        Equal("second", secondUndo.Text, "Second history restored another target's text.");
    }

    private static void TextHistoryRetainsSelection()
    {
        var clock = new HistoryClock();
        TextEditHistory history = clock.CreateHistory();
        history.Reset("A🌙BC", 3, 5);
        history.RecordAtomic("A🌙B!C", 4, 4);

        Ensure(history.TryUndo(out TextEditSnapshot undone), "Undo was unavailable for selection restoration.");
        Equal("A🌙BC", undone.Text, "Undo changed the UTF-16 text unexpectedly.");
        Equal(3, undone.AnchorPosition, "Undo lost the UTF-16 selection anchor.");
        Equal(5, undone.FocusPosition, "Undo lost the UTF-16 selection focus.");
    }

    private static void TextHistoryGroupsMultiCharacterCommits()
    {
        var clock = new HistoryClock();
        TextEditHistory history = clock.CreateHistory();
        history.Reset("p", 1, 1);
        clock.Now = 0.1d;
        history.Record("pr", 2, 2);
        clock.Now = 0.2d;
        history.Record("pr🌙字", 5, 5);

        Ensure(history.TryUndo(out TextEditSnapshot undone), "A multi-character commit had no undo step.");
        Equal("pr", undone.Text, "A multi-character commit merged into surrounding typing.");
        Ensure(history.TryRedo(out TextEditSnapshot redone), "A multi-character commit could not be redone.");
        Equal("pr🌙字", redone.Text, "Redo did not restore the full UTF-16 commit.");
        Equal(5, redone.FocusPosition, "Redo lost the UTF-16 caret position after the commit.");
        Ensure(history.TryUndo(out undone), "The commit could not be undone after replay.");
        Ensure(history.TryUndo(out undone), "Typing before the commit had no undo step.");
        Equal("p", undone.Text, "Undo did not restore text before typing preceding the commit.");
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

    private sealed class HistoryClock
    {
        public double Now { get; set; }

        public TextEditHistory CreateHistory()
        {
            return new TextEditHistory(() => Now);
        }
    }

}
