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

    /// <summary>Compares CJK characters and whole words without interpreting TMP formatting tags.</summary>
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

            TextTokens originalTokens = Tokenize(original);
            TextTokens replacementTokens = Tokenize(replacement);
            int originalCount = originalTokens.Count;
            int replacementCount = replacementTokens.Count;

            int prefixCount = 0;
            while (prefixCount < originalCount && prefixCount < replacementCount &&
                TokensEqual(originalTokens, prefixCount, replacementTokens, prefixCount))
            {
                prefixCount++;
            }

            int suffixCount = 0;
            while (suffixCount < originalCount - prefixCount && suffixCount < replacementCount - prefixCount &&
                TokensEqual(
                    originalTokens,
                    originalCount - suffixCount - 1,
                    replacementTokens,
                    replacementCount - suffixCount - 1))
            {
                suffixCount++;
            }

            int originalMiddleCount = originalCount - prefixCount - suffixCount;
            int replacementMiddleCount = replacementCount - prefixCount - suffixCount;
            if (originalMiddleCount == 0 && replacementMiddleCount == 0)
                return EmptyResult;

            List<TokenMatch> matches = null;
            if (originalMiddleCount > 0 && replacementMiddleCount > 0)
            {
                matches = new List<TokenMatch>();
                var forward = new int[replacementMiddleCount + 1];
                var backward = new int[replacementMiddleCount + 1];
                FindMatches(
                    originalTokens,
                    prefixCount,
                    originalMiddleCount,
                    replacementTokens,
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
                    TokenMatch match = matches[i];
                    AddSpan(originalTokens, nextOriginal, match.OriginalIndex - nextOriginal, removed);
                    AddSpan(replacementTokens, nextReplacement, match.ReplacementIndex - nextReplacement, added);
                    nextOriginal = match.OriginalIndex + 1;
                    nextReplacement = match.ReplacementIndex + 1;
                }
            }

            AddSpan(originalTokens, nextOriginal, originalMiddleEnd - nextOriginal, removed);
            AddSpan(replacementTokens, nextReplacement, replacementMiddleEnd - nextReplacement, added);

            if (removed.Count == 0 && added.Count == 0)
                return EmptyResult;

            return new TextDiffResult(removed, added);
        }

        private static void FindMatches(
            TextTokens original,
            int originalStart,
            int originalCount,
            TextTokens replacement,
            int replacementStart,
            int replacementCount,
            int[] forward,
            int[] backward,
            List<TokenMatch> matches)
        {
            if (originalCount == 0 || replacementCount == 0)
                return;

            if (originalCount == 1)
            {
                for (int i = 0; i < replacementCount; i++)
                {
                    if (TokensEqual(original, originalStart, replacement, replacementStart + i))
                    {
                        matches.Add(new TokenMatch(originalStart, replacementStart + i));
                        break;
                    }
                }

                return;
            }

            if (replacementCount == 1)
            {
                for (int i = 0; i < originalCount; i++)
                {
                    if (TokensEqual(original, originalStart + i, replacement, replacementStart))
                    {
                        matches.Add(new TokenMatch(originalStart + i, replacementStart));
                        break;
                    }
                }

                return;
            }

            int originalLeftCount = originalCount / 2;
            int replacementSplit = FindMiddleSplit(
                original,
                originalStart,
                originalLeftCount,
                originalCount - originalLeftCount,
                replacement,
                replacementStart,
                replacementCount,
                forward,
                backward);

            FindMatches(
                original,
                originalStart,
                originalLeftCount,
                replacement,
                replacementStart,
                replacementSplit,
                forward,
                backward,
                matches);
            FindMatches(
                original,
                originalStart + originalLeftCount,
                originalCount - originalLeftCount,
                replacement,
                replacementStart + replacementSplit,
                replacementCount - replacementSplit,
                forward,
                backward,
                matches);
        }

        private static int FindMiddleSplit(
            TextTokens original,
            int originalStart,
            int originalLeftCount,
            int originalRightCount,
            TextTokens replacement,
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
                    if (TokensEqual(original, originalStart + i, replacement, replacementStart + j - 1))
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
                    if (TokensEqual(
                        original,
                        originalStart + originalLeftCount + i,
                        replacement,
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

        private static bool TokensEqual(
            TextTokens first,
            int firstIndex,
            TextTokens second,
            int secondIndex)
        {
            int firstStart = first.Starts[firstIndex];
            int secondStart = second.Starts[secondIndex];
            int firstLength = TokenLength(first, firstIndex);
            int secondLength = TokenLength(second, secondIndex);
            return firstLength == secondLength &&
                string.CompareOrdinal(first.Text, firstStart, second.Text, secondStart, firstLength) == 0;
        }

        private static int TokenLength(TextTokens tokens, int index)
        {
            int end = index + 1 < tokens.Count ? tokens.Starts[index + 1] : tokens.Text.Length;
            return end - tokens.Starts[index];
        }

        private static void AddSpan(
            TextTokens tokens,
            int startToken,
            int tokenCount,
            List<TextDiffSpan> spans)
        {
            if (tokenCount <= 0)
                return;

            int start = tokens.Starts[startToken];
            int endToken = startToken + tokenCount;
            int end = endToken < tokens.Count ? tokens.Starts[endToken] : tokens.Text.Length;
            spans.Add(new TextDiffSpan(start, end - start));
        }

        private static TextTokens Tokenize(string text)
        {
            // Reuse the grapheme-start buffer and compact it to token starts in place.
            int[] starts = StringInfo.ParseCombiningCharacters(text);
            int tokenCount = 0;
            bool inWordRun = false;

            for (int elementIndex = 0; elementIndex < starts.Length; elementIndex++)
            {
                int start = starts[elementIndex];
                int codePoint = GetCodePoint(text, start);
                UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(text, start);

                if (IsCjkCharacter(codePoint))
                {
                    starts[tokenCount++] = start;
                    inWordRun = false;
                    continue;
                }

                if (IsTextElementExtender(codePoint, category))
                {
                    if (tokenCount == 0)
                        starts[tokenCount++] = start;
                    continue;
                }

                if (IsLetter(category) || IsNumber(category))
                {
                    if (!inWordRun)
                        starts[tokenCount++] = start;
                    inWordRun = true;
                    continue;
                }

                starts[tokenCount++] = start;
                inWordRun = false;
            }

            return new TextTokens(text, starts, tokenCount);
        }

        private static int GetCodePoint(string text, int index)
        {
            char first = text[index];
            if (char.IsHighSurrogate(first) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
                return char.ConvertToUtf32(first, text[index + 1]);

            return first;
        }

        private static bool IsCjkCharacter(int codePoint)
        {
            // These script blocks keep Han, Kana, and Hangul out of adjacent word runs.
            return IsInRange(codePoint, 0x2E80, 0x2FFF) ||
                IsInRange(codePoint, 0x3005, 0x3007) ||
                IsInRange(codePoint, 0x3021, 0x3029) ||
                IsInRange(codePoint, 0x3038, 0x303B) ||
                IsInRange(codePoint, 0x3040, 0x30FF) ||
                IsInRange(codePoint, 0x31F0, 0x31FF) ||
                IsInRange(codePoint, 0x3400, 0x4DBF) ||
                IsInRange(codePoint, 0x4E00, 0x9FFF) ||
                IsInRange(codePoint, 0xF900, 0xFAFF) ||
                IsInRange(codePoint, 0xFF65, 0xFF9F) ||
                IsInRange(codePoint, 0x1100, 0x11FF) ||
                IsInRange(codePoint, 0x3130, 0x318F) ||
                IsInRange(codePoint, 0xA960, 0xA97F) ||
                IsInRange(codePoint, 0xAC00, 0xD7AF) ||
                IsInRange(codePoint, 0xD7B0, 0xD7FF) ||
                IsInRange(codePoint, 0x1AFF0, 0x1AFFF) ||
                IsInRange(codePoint, 0x1B000, 0x1B16F) ||
                IsInRange(codePoint, 0x20000, 0x2EE5F) ||
                IsInRange(codePoint, 0x2F800, 0x2FA1F) ||
                IsInRange(codePoint, 0x30000, 0x323AF);
        }

        private static bool IsTextElementExtender(int codePoint, UnicodeCategory category)
        {
            return category == UnicodeCategory.NonSpacingMark ||
                category == UnicodeCategory.SpacingCombiningMark ||
                category == UnicodeCategory.EnclosingMark ||
                IsInRange(codePoint, 0xFE00, 0xFE0F) ||
                IsInRange(codePoint, 0xE0100, 0xE01EF);
        }

        private static bool IsLetter(UnicodeCategory category)
        {
            return category == UnicodeCategory.UppercaseLetter ||
                category == UnicodeCategory.LowercaseLetter ||
                category == UnicodeCategory.TitlecaseLetter ||
                category == UnicodeCategory.ModifierLetter ||
                category == UnicodeCategory.OtherLetter;
        }

        private static bool IsNumber(UnicodeCategory category)
        {
            return category == UnicodeCategory.DecimalDigitNumber ||
                category == UnicodeCategory.LetterNumber ||
                category == UnicodeCategory.OtherNumber;
        }

        private static bool IsInRange(int codePoint, int start, int end)
        {
            return codePoint >= start && codePoint <= end;
        }

        private readonly struct TextTokens
        {
            public string Text { get; }
            public int[] Starts { get; }
            public int Count { get; }

            public TextTokens(string text, int[] starts, int count)
            {
                Text = text;
                Starts = starts;
                Count = count;
            }
        }

        private readonly struct TokenMatch
        {
            public int OriginalIndex { get; }
            public int ReplacementIndex { get; }

            public TokenMatch(int originalIndex, int replacementIndex)
            {
                OriginalIndex = originalIndex;
                ReplacementIndex = replacementIndex;
            }
        }
    }
}

