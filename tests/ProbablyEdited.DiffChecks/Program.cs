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
        Run("alphabetic and numeric changes replace whole words", ReportsWholeWordsAndNumbers, ref failures);
        Run("mixed scripts keep CJK, words, and numbers separate", ReportsMixedScriptTokenBoundaries, ref failures);
        Run("separated replacements preserve unchanged middle text", ReportsSeparatedMixedReplacements, ref failures);
        Run("multiline insertions and newline replacements are localized", ReportsMultilineChanges, ref failures);
        Run("Han, Kana, Hangul, and surrogate pairs remain intact", PreservesCjkAndSurrogateBoundaries, ref failures);
        Run("combining and variation sequences remain intact", PreservesCombiningAndVariationSequences, ref failures);
        Run("rich-text and placeholder offsets remain exact", PreservesTaggedOffsets, ref failures);
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
        TextDiffResult insertion = TextDiff.Compare("cat", "new cat");
        Spans(insertion.Removed, Array.Empty<ExpectedSpan>(), "Insertion removals");
        Spans(insertion.Added, new[] { new ExpectedSpan(0, 4) }, "Insertion additions");

        TextDiffResult deletion = TextDiff.Compare("new cat", "cat");
        Spans(deletion.Removed, new[] { new ExpectedSpan(0, 4) }, "Deletion removals");
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

    private static void ReportsWholeWordsAndNumbers()
    {
        TextDiffResult diff = TextDiff.Compare("the cat 123!", "the cut 124!");
        Spans(
            diff.Removed,
            new[] { new ExpectedSpan(4, 3), new ExpectedSpan(8, 3) },
            "Whole-word removals");
        Spans(
            diff.Added,
            new[] { new ExpectedSpan(4, 3), new ExpectedSpan(8, 3) },
            "Whole-word additions");
    }

    private static void ReportsMixedScriptTokenBoundaries()
    {
        TextDiffResult diff = TextDiff.Compare("猫-cat-12-駅", "犬-cut-13-韓");
        Spans(
            diff.Removed,
            new[]
            {
                new ExpectedSpan(0, 1),
                new ExpectedSpan(2, 3),
                new ExpectedSpan(6, 2),
                new ExpectedSpan(9, 1)
            },
            "Mixed-script removals");
        Spans(
            diff.Added,
            new[]
            {
                new ExpectedSpan(0, 1),
                new ExpectedSpan(2, 3),
                new ExpectedSpan(6, 2),
                new ExpectedSpan(9, 1)
            },
            "Mixed-script additions");
        TextDiffResult adjacent = TextDiff.Compare("猫cat-12駅", "猫cut-13駅");
        Spans(
            adjacent.Removed,
            new[] { new ExpectedSpan(1, 3), new ExpectedSpan(5, 2) },
            "Adjacent-script removals");
        Spans(
            adjacent.Added,
            new[] { new ExpectedSpan(1, 3), new ExpectedSpan(5, 2) },
            "Adjacent-script additions");

        TextDiffResult adjacentLettersAndNumbers = TextDiff.Compare(
            "猫Cafe\u030112-100金币",
            "猫Cafe\u030113-101金币");
        Spans(
            adjacentLettersAndNumbers.Removed,
            new[] { new ExpectedSpan(1, 7), new ExpectedSpan(9, 3) },
            "Adjacent alphanumeric removals");
        Spans(
            adjacentLettersAndNumbers.Added,
            new[] { new ExpectedSpan(1, 7), new ExpectedSpan(9, 3) },
            "Adjacent alphanumeric additions");
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
        TextDiffResult han = TextDiff.Compare("猫駅", "狗駅");
        Spans(han.Removed, new[] { new ExpectedSpan(0, 1) }, "Han removals");
        Spans(han.Added, new[] { new ExpectedSpan(0, 1) }, "Han additions");

        TextDiffResult kana = TextDiff.Compare("猫の駅", "猫と駅");
        Spans(kana.Removed, new[] { new ExpectedSpan(1, 1) }, "Kana removals");
        Spans(kana.Added, new[] { new ExpectedSpan(1, 1) }, "Kana additions");

        TextDiffResult hangul = TextDiff.Compare("안녕하세요", "안녕하셔요");
        Spans(hangul.Removed, new[] { new ExpectedSpan(3, 1) }, "Hangul removals");
        Spans(hangul.Added, new[] { new ExpectedSpan(3, 1) }, "Hangul additions");

        TextDiffResult surrogate = TextDiff.Compare("A😀e\u0301Z", "A😺e\u0301Z");
        Spans(surrogate.Removed, new[] { new ExpectedSpan(1, 2) }, "Surrogate removals");
        Spans(surrogate.Added, new[] { new ExpectedSpan(1, 2) }, "Surrogate additions");
    }

    private static void PreservesCombiningAndVariationSequences()
    {
        TextDiffResult combining = TextDiff.Compare("A Cafe\u0301!", "A Cafe\u0300!");
        Spans(combining.Removed, new[] { new ExpectedSpan(2, 5) }, "Combining-word removals");
        Spans(combining.Added, new[] { new ExpectedSpan(2, 5) }, "Combining-word additions");

        TextDiffResult variation = TextDiff.Compare("A\u908A\uFE00Z", "A\u908A\uFE01Z");
        Spans(variation.Removed, new[] { new ExpectedSpan(1, 2) }, "Variation-sequence removals");
        Spans(variation.Added, new[] { new ExpectedSpan(1, 2) }, "Variation-sequence additions");
    }

    private static void PreservesTaggedOffsets()
    {
        TextDiffResult tagged = TextDiff.Compare(
            "<color=#fff>hello</color>",
            "<color=#fff>hallo</color>");
        Spans(tagged.Removed, new[] { new ExpectedSpan(12, 5) }, "Tagged-text removals");
        Spans(tagged.Added, new[] { new ExpectedSpan(12, 5) }, "Tagged-text additions");

        TextDiffResult placeholder = TextDiff.Compare("Use {name}.", "Use {user}.");
        Spans(placeholder.Removed, new[] { new ExpectedSpan(5, 4) }, "Placeholder removals");
        Spans(placeholder.Added, new[] { new ExpectedSpan(5, 4) }, "Placeholder additions");
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
