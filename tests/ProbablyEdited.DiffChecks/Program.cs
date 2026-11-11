using System;
using System.Collections.Generic;
using ProbablyEdited;

internal static class Program
{
    private static int Main()
    {
        int failures = 0;
        Run("unchanged text has no changed ranges", UnchangedTextHasNoRanges, ref failures);
        Run("pure insertions and deletions use exact UTF-16 spans", ReportsPureInsertionsAndDeletions, ref failures);
        Run("separated replacements preserve unchanged middle text", ReportsSeparatedMixedReplacements, ref failures);
        Run("multiline insertions and newline replacements are localized", ReportsMultilineChanges, ref failures);
        Run("CJK and surrogate pairs remain intact", PreservesCjkAndSurrogateBoundaries, ref failures);
        Run("combining sequences remain intact", PreservesCombiningSequenceBoundaries, ref failures);
        Run("formatting tags remain part of the raw offset space", PreservesTaggedOffsets, ref failures);
        return failures == 0 ? 0 : 1;
    }

    private static void UnchangedTextHasNoRanges()
    {
        TextDiffResult diff = TextDiff.Compare("same", "same");
        Equal(0, diff.Removed.Count, "Unchanged original ranges");
        Equal(0, diff.Added.Count, "Unchanged replacement ranges");
    }

    private static void ReportsPureInsertionsAndDeletions()
    {
        TextDiffResult insertion = TextDiff.Compare("ab", "aXYb");
        Spans(insertion.Removed, Array.Empty<ExpectedSpan>(), "Insertion removals");
        Spans(insertion.Added, new[] { new ExpectedSpan(1, 2) }, "Insertion additions");

        TextDiffResult deletion = TextDiff.Compare("keep old phrase", "keep  phrase");
        Spans(deletion.Removed, new[] { new ExpectedSpan(5, 3) }, "Deletion removals");
        Spans(deletion.Added, Array.Empty<ExpectedSpan>(), "Deletion additions");
    }

    private static void ReportsSeparatedMixedReplacements()
    {
        TextDiffResult diff = TextDiff.Compare("red cat and box dog", "red fox and shy dog");
        Spans(
            diff.Removed,
            new[] { new ExpectedSpan(4, 3), new ExpectedSpan(12, 3) },
            "Mixed replacement removals");
        Spans(
            diff.Added,
            new[] { new ExpectedSpan(4, 3), new ExpectedSpan(12, 3) },
            "Mixed replacement additions");
    }

    private static void ReportsMultilineChanges()
    {
        TextDiffResult insertion = TextDiff.Compare("alpha\nbeta", "alpha\nnew\nbeta");
        Spans(insertion.Removed, Array.Empty<ExpectedSpan>(), "Multiline insertion removals");
        Spans(insertion.Added, new[] { new ExpectedSpan(6, 4) }, "Multiline insertion additions");

        TextDiffResult newlineReplacement = TextDiff.Compare("one\ntwo", "one two");
        Spans(newlineReplacement.Removed, new[] { new ExpectedSpan(3, 1) }, "Newline removal");
        Spans(newlineReplacement.Added, new[] { new ExpectedSpan(3, 1) }, "Newline replacement");
    }

    private static void PreservesCjkAndSurrogateBoundaries()
    {
        TextDiffResult cjk = TextDiff.Compare("猫の駅", "猫と駅");
        Spans(cjk.Removed, new[] { new ExpectedSpan(1, 1) }, "CJK removals");
        Spans(cjk.Added, new[] { new ExpectedSpan(1, 1) }, "CJK additions");

        TextDiffResult surrogate = TextDiff.Compare("A😀e\u0301Z", "A😺e\u0301Z");
        Spans(surrogate.Removed, new[] { new ExpectedSpan(1, 2) }, "Surrogate removals");
        Spans(surrogate.Added, new[] { new ExpectedSpan(1, 2) }, "Surrogate additions");
    }

    private static void PreservesCombiningSequenceBoundaries()
    {
        TextDiffResult diff = TextDiff.Compare("Cafe\u0301!", "Cafe\u0300!");
        Spans(diff.Removed, new[] { new ExpectedSpan(3, 2) }, "Combining-sequence removals");
        Spans(diff.Added, new[] { new ExpectedSpan(3, 2) }, "Combining-sequence additions");
    }

    private static void PreservesTaggedOffsets()
    {
        TextDiffResult diff = TextDiff.Compare(
            "<color=#fff>hello</color>",
            "<color=#fff>hallo</color>");
        Spans(diff.Removed, new[] { new ExpectedSpan(13, 1) }, "Tagged-text removals");
        Spans(diff.Added, new[] { new ExpectedSpan(13, 1) }, "Tagged-text additions");
    }

    private static void Spans(
        IReadOnlyList<TextDiffSpan> actual,
        IReadOnlyList<ExpectedSpan> expected,
        string message)
    {
        Equal(expected.Count, actual.Count, message + " count");
        for (int i = 0; i < expected.Count; i++)
        {
            Equal(expected[i].Start, actual[i].Start, message + " start " + i);
            Equal(expected[i].Length, actual[i].Length, message + " length " + i);
        }
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

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(message + $" Expected: {expected}; actual: {actual}.");
    }

    private readonly struct ExpectedSpan
    {
        public int Start { get; }
        public int Length { get; }

        public ExpectedSpan(int start, int length)
        {
            Start = start;
            Length = length;
        }
    }
}
