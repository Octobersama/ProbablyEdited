using System;
using System.Collections.Generic;
using System.Globalization;

namespace ProbablyEdited
{
    /// <summary>A changed range expressed as a UTF-16 offset into one of the source strings.</summary>
    public readonly struct TextDiffSpan
    {
        public int Start { get; }
        public int Length { get; }
        public int End => Start + Length;

        internal TextDiffSpan(int start, int length)
        {
            Start = start;
            Length = length;
        }
    }

    /// <summary>The removed ranges in the original text and inserted ranges in the replacement.</summary>
    public sealed class TextDiffResult
    {
        public IReadOnlyList<TextDiffSpan> Removed { get; }
        public IReadOnlyList<TextDiffSpan> Added { get; }

        internal TextDiffResult(IList<TextDiffSpan> removed, IList<TextDiffSpan> added)
        {
            Removed = new System.Collections.ObjectModel.ReadOnlyCollection<TextDiffSpan>(removed);
            Added = new System.Collections.ObjectModel.ReadOnlyCollection<TextDiffSpan>(added);
        }
    }

    /// <summary>Compares Unicode text elements without interpreting TMP formatting tags.</summary>
    public static class TextDiff
    {
        private static readonly TextDiffResult EmptyResult = new TextDiffResult(
            Array.Empty<TextDiffSpan>(),
            Array.Empty<TextDiffSpan>());

        public static TextDiffResult Compare(string original, string replacement)
        {
            original = original ?? string.Empty;
            replacement = replacement ?? string.Empty;
            if (string.Equals(original, replacement, StringComparison.Ordinal))
                return EmptyResult;

            int[] originalStarts = StringInfo.ParseCombiningCharacters(original);
            int[] replacementStarts = StringInfo.ParseCombiningCharacters(replacement);
            int originalCount = originalStarts.Length;
            int replacementCount = replacementStarts.Length;

            int prefixCount = 0;
            while (prefixCount < originalCount && prefixCount < replacementCount &&
                ElementsEqual(original, originalStarts, prefixCount, replacement, replacementStarts, prefixCount))
            {
                prefixCount++;
            }

            int suffixCount = 0;
            while (suffixCount < originalCount - prefixCount && suffixCount < replacementCount - prefixCount &&
                ElementsEqual(
                    original,
                    originalStarts,
                    originalCount - suffixCount - 1,
                    replacement,
                    replacementStarts,
                    replacementCount - suffixCount - 1))
            {
                suffixCount++;
            }

            int originalMiddleCount = originalCount - prefixCount - suffixCount;
            int replacementMiddleCount = replacementCount - prefixCount - suffixCount;
            if (originalMiddleCount == 0 && replacementMiddleCount == 0)
                return EmptyResult;

            List<ElementMatch> matches = null;
            if (originalMiddleCount > 0 && replacementMiddleCount > 0)
            {
                matches = new List<ElementMatch>();
                var forward = new int[replacementMiddleCount + 1];
                var backward = new int[replacementMiddleCount + 1];
                FindMatches(
                    original,
                    originalStarts,
                    prefixCount,
                    originalMiddleCount,
                    replacement,
                    replacementStarts,
                    prefixCount,
                    replacementMiddleCount,
                    forward,
                    backward,
                    matches);
            }

            var removed = new List<TextDiffSpan>();
            var added = new List<TextDiffSpan>();
            int nextOriginal = prefixCount;
            int nextReplacement = prefixCount;
            int originalMiddleEnd = originalCount - suffixCount;
            int replacementMiddleEnd = replacementCount - suffixCount;

            if (matches != null)
            {
                for (int i = 0; i < matches.Count; i++)
                {
                    ElementMatch match = matches[i];
                    AddSpan(original, originalStarts, nextOriginal, match.OriginalIndex - nextOriginal, removed);
                    AddSpan(replacement, replacementStarts, nextReplacement, match.ReplacementIndex - nextReplacement, added);
                    nextOriginal = match.OriginalIndex + 1;
                    nextReplacement = match.ReplacementIndex + 1;
                }
            }

            AddSpan(original, originalStarts, nextOriginal, originalMiddleEnd - nextOriginal, removed);
            AddSpan(replacement, replacementStarts, nextReplacement, replacementMiddleEnd - nextReplacement, added);

            if (removed.Count == 0 && added.Count == 0)
                return EmptyResult;

            return new TextDiffResult(removed, added);
        }

        private static void FindMatches(
            string original,
            int[] originalStarts,
            int originalStart,
            int originalCount,
            string replacement,
            int[] replacementStarts,
            int replacementStart,
            int replacementCount,
            int[] forward,
            int[] backward,
            List<ElementMatch> matches)
        {
            if (originalCount == 0 || replacementCount == 0)
                return;

            if (originalCount == 1)
            {
                for (int i = 0; i < replacementCount; i++)
                {
                    if (ElementsEqual(
                        original,
                        originalStarts,
                        originalStart,
                        replacement,
                        replacementStarts,
                        replacementStart + i))
                    {
                        matches.Add(new ElementMatch(originalStart, replacementStart + i));
                        break;
                    }
                }

                return;
            }

            if (replacementCount == 1)
            {
                for (int i = 0; i < originalCount; i++)
                {
                    if (ElementsEqual(
                        original,
                        originalStarts,
                        originalStart + i,
                        replacement,
                        replacementStarts,
                        replacementStart))
                    {
                        matches.Add(new ElementMatch(originalStart + i, replacementStart));
                        break;
                    }
                }

                return;
            }

            int originalLeftCount = originalCount / 2;
            int replacementSplit = FindMiddleSplit(
                original,
                originalStarts,
                originalStart,
                originalLeftCount,
                originalCount - originalLeftCount,
                replacement,
                replacementStarts,
                replacementStart,
                replacementCount,
                forward,
                backward);

            FindMatches(
                original,
                originalStarts,
                originalStart,
                originalLeftCount,
                replacement,
                replacementStarts,
                replacementStart,
                replacementSplit,
                forward,
                backward,
                matches);
            FindMatches(
                original,
                originalStarts,
                originalStart + originalLeftCount,
                originalCount - originalLeftCount,
                replacement,
                replacementStarts,
                replacementStart + replacementSplit,
                replacementCount - replacementSplit,
                forward,
                backward,
                matches);
        }

        private static int FindMiddleSplit(
            string original,
            int[] originalStarts,
            int originalStart,
            int originalLeftCount,
            int originalRightCount,
            string replacement,
            int[] replacementStarts,
            int replacementStart,
            int replacementCount,
            int[] forward,
            int[] backward)
        {
            Array.Clear(forward, 0, replacementCount + 1);
            for (int i = 0; i < originalLeftCount; i++)
            {
                int diagonal = 0;
                for (int j = 1; j <= replacementCount; j++)
                {
                    int previousRow = forward[j];
                    if (ElementsEqual(
                        original,
                        originalStarts,
                        originalStart + i,
                        replacement,
                        replacementStarts,
                        replacementStart + j - 1))
                    {
                        forward[j] = diagonal + 1;
                    }
                    else if (forward[j - 1] > forward[j])
                    {
                        forward[j] = forward[j - 1];
                    }

                    diagonal = previousRow;
                }
            }

            Array.Clear(backward, 0, replacementCount + 1);
            for (int i = originalRightCount - 1; i >= 0; i--)
            {
                int diagonal = 0;
                for (int j = replacementCount - 1; j >= 0; j--)
                {
                    int previousRow = backward[j];
                    if (ElementsEqual(
                        original,
                        originalStarts,
                        originalStart + originalLeftCount + i,
                        replacement,
                        replacementStarts,
                        replacementStart + j))
                    {
                        backward[j] = diagonal + 1;
                    }
                    else if (backward[j + 1] > backward[j])
                    {
                        backward[j] = backward[j + 1];
                    }

                    diagonal = previousRow;
                }
            }

            int bestSplit = 0;
            int bestLength = -1;
            for (int split = 0; split <= replacementCount; split++)
            {
                int length = forward[split] + backward[split];
                if (length > bestLength)
                {
                    bestLength = length;
                    bestSplit = split;
                }
            }

            return bestSplit;
        }

        private static bool ElementsEqual(
            string first,
            int[] firstStarts,
            int firstIndex,
            string second,
            int[] secondStarts,
            int secondIndex)
        {
            int firstStart = firstStarts[firstIndex];
            int secondStart = secondStarts[secondIndex];
            int firstLength = ElementLength(first, firstStarts, firstIndex);
            int secondLength = ElementLength(second, secondStarts, secondIndex);
            return firstLength == secondLength &&
                string.CompareOrdinal(first, firstStart, second, secondStart, firstLength) == 0;
        }

        private static int ElementLength(string text, int[] starts, int index)
        {
            int end = index + 1 < starts.Length ? starts[index + 1] : text.Length;
            return end - starts[index];
        }

        private static void AddSpan(
            string text,
            int[] starts,
            int startElement,
            int elementCount,
            List<TextDiffSpan> spans)
        {
            if (elementCount <= 0)
                return;

            int start = starts[startElement];
            int endElement = startElement + elementCount;
            int end = endElement < starts.Length ? starts[endElement] : text.Length;
            spans.Add(new TextDiffSpan(start, end - start));
        }

        private readonly struct ElementMatch
        {
            public int OriginalIndex { get; }
            public int ReplacementIndex { get; }

            public ElementMatch(int originalIndex, int replacementIndex)
            {
                OriginalIndex = originalIndex;
                ReplacementIndex = replacementIndex;
            }
        }
    }
}
