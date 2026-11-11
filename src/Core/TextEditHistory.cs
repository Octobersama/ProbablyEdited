using System;
using System.Collections.Generic;

namespace ProbablyEdited
{
    internal readonly struct TextEditSnapshot
    {
        public TextEditSnapshot(string text, int anchorPosition, int focusPosition)
        {
            Text = text ?? string.Empty;
            AnchorPosition = anchorPosition;
            FocusPosition = focusPosition;
        }

        public string Text { get; }
        public int AnchorPosition { get; }
        public int FocusPosition { get; }
    }

    internal sealed class TextEditHistory
    {
        private const double TypingIdleBoundarySeconds = 0.35d;
        private const double MaximumTypingGroupSeconds = 1.0d;

        private readonly List<TextEditSnapshot> _snapshots = new List<TextEditSnapshot>();
        private readonly Func<double> _getTime;
        private int _currentIndex = -1;
        private bool _hasOpenTypingGroup;
        private TextEditKind _typingGroupKind;
        private double _typingGroupStartTime;
        private double _typingGroupLastEditTime;

        public TextEditHistory(Func<double> getTime)
        {
            _getTime = getTime ?? throw new ArgumentNullException(nameof(getTime));
        }

        public bool CanUndo => _currentIndex > 0;
        public bool CanRedo => _currentIndex >= 0 && _currentIndex < _snapshots.Count - 1;

        public TextEditSnapshot Current
        {
            get
            {
                if (_currentIndex < 0)
                    throw new InvalidOperationException("Text edit history has not been initialized.");

                return _snapshots[_currentIndex];
            }
        }

        public void Reset(string text, int anchorPosition, int focusPosition)
        {
            _snapshots.Clear();
            _snapshots.Add(new TextEditSnapshot(text, anchorPosition, focusPosition));
            _currentIndex = 0;
            FlushTypingGroup();
        }

        public void Record(string text, int anchorPosition, int focusPosition)
        {
            RecordEdit(text, anchorPosition, focusPosition, false);
        }

        public void RecordAtomic(string text, int anchorPosition, int focusPosition)
        {
            RecordEdit(text, anchorPosition, focusPosition, true);
        }

        public void FlushTypingGroup()
        {
            _hasOpenTypingGroup = false;
        }

        public void UpdateCurrentSelection(int anchorPosition, int focusPosition)
        {
            if (_currentIndex < 0)
                return;

            TextEditSnapshot current = _snapshots[_currentIndex];
            if (current.AnchorPosition != anchorPosition || current.FocusPosition != focusPosition)
                FlushTypingGroup();

            _snapshots[_currentIndex] = new TextEditSnapshot(current.Text, anchorPosition, focusPosition);
        }

        public bool TryUndo(out TextEditSnapshot snapshot)
        {
            FlushTypingGroup();
            if (!CanUndo)
            {
                snapshot = default(TextEditSnapshot);
                return false;
            }

            snapshot = _snapshots[--_currentIndex];
            return true;
        }

        public bool TryRedo(out TextEditSnapshot snapshot)
        {
            FlushTypingGroup();
            if (!CanRedo)
            {
                snapshot = default(TextEditSnapshot);
                return false;
            }

            snapshot = _snapshots[++_currentIndex];
            return true;
        }

        private void RecordEdit(string text, int anchorPosition, int focusPosition, bool forceAtomic)
        {
            text = text ?? string.Empty;
            if (_currentIndex < 0)
            {
                Reset(text, anchorPosition, focusPosition);
                return;
            }

            TextEditSnapshot previous = _snapshots[_currentIndex];
            if (string.Equals(previous.Text, text, StringComparison.Ordinal))
            {
                UpdateCurrentSelection(anchorPosition, focusPosition);
                return;
            }

            double editTime = _getTime();
            TextEditKind kind = TextEditKind.None;
            bool canGroup = !forceAtomic
                && TryDescribeTypingEdit(previous, text, anchorPosition, focusPosition, out kind);
            if (!canGroup)
            {
                AddSnapshot(text, anchorPosition, focusPosition);
                return;
            }

            if (_hasOpenTypingGroup
                && _typingGroupKind == kind
                && editTime - _typingGroupLastEditTime < TypingIdleBoundarySeconds
                && editTime - _typingGroupStartTime < MaximumTypingGroupSeconds)
            {
                _snapshots[_currentIndex] = new TextEditSnapshot(text, anchorPosition, focusPosition);
                _typingGroupLastEditTime = editTime;
                return;
            }

            AddSnapshot(text, anchorPosition, focusPosition);
            _hasOpenTypingGroup = true;
            _typingGroupKind = kind;
            _typingGroupStartTime = editTime;
            _typingGroupLastEditTime = editTime;
        }

        private void AddSnapshot(string text, int anchorPosition, int focusPosition)
        {
            int redoCount = _snapshots.Count - _currentIndex - 1;
            if (redoCount > 0)
                _snapshots.RemoveRange(_currentIndex + 1, redoCount);

            _snapshots.Add(new TextEditSnapshot(text, anchorPosition, focusPosition));
            _currentIndex = _snapshots.Count - 1;
            FlushTypingGroup();
        }

        private static bool TryDescribeTypingEdit(
            TextEditSnapshot previous,
            string text,
            int anchorPosition,
            int focusPosition,
            out TextEditKind kind)
        {
            kind = TextEditKind.None;
            string previousText = previous.Text;
            bool previousSelectionCollapsed = previous.AnchorPosition == previous.FocusPosition;
            bool currentSelectionCollapsed = anchorPosition == focusPosition;
            if (!previousSelectionCollapsed || !currentSelectionCollapsed)
                return false;

            int lengthChange = text.Length - previousText.Length;
            if (lengthChange == 1
                && previous.FocusPosition >= 0 && previous.FocusPosition <= previousText.Length
                && focusPosition == previous.FocusPosition + 1
                && MatchesInsertionAt(previousText, text, previous.FocusPosition))
            {
                kind = TextEditKind.Insert;
                return true;
            }

            if (lengthChange == -1 && MatchesDeletionAt(previousText, text, focusPosition))
            {
                if (previous.FocusPosition > 0 && focusPosition == previous.FocusPosition - 1)
                {
                    kind = TextEditKind.Backspace;
                    return true;
                }

                if (focusPosition == previous.FocusPosition)
                {
                    kind = TextEditKind.ForwardDelete;
                    return true;
                }
            }

            return false;
        }

        private static bool MatchesInsertionAt(string previousText, string text, int position)
        {
            if (position < 0 || position > previousText.Length || text.Length != previousText.Length + 1)
                return false;

            for (int i = 0; i < position; i++)
            {
                if (previousText[i] != text[i])
                    return false;
            }

            for (int i = position; i < previousText.Length; i++)
            {
                if (previousText[i] != text[i + 1])
                    return false;
            }

            return true;
        }

        private static bool MatchesDeletionAt(string previousText, string text, int position)
        {
            if (position < 0 || position > text.Length || previousText.Length != text.Length + 1)
                return false;

            for (int i = 0; i < position; i++)
            {
                if (previousText[i] != text[i])
                    return false;
            }

            for (int i = position; i < text.Length; i++)
            {
                if (previousText[i + 1] != text[i])
                    return false;
            }

            return true;
        }

        private enum TextEditKind
        {
            None,
            Insert,
            Backspace,
            ForwardDelete
        }
    }
}