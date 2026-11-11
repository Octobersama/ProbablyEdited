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
        private readonly List<TextEditSnapshot> _snapshots = new List<TextEditSnapshot>();
        private int _currentIndex = -1;

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
        }

        public void Record(string text, int anchorPosition, int focusPosition)
        {
            text = text ?? string.Empty;
            if (_currentIndex < 0)
            {
                Reset(text, anchorPosition, focusPosition);
                return;
            }

            // Text-identical callbacks only update selection; they must not discard redo history.
            if (string.Equals(_snapshots[_currentIndex].Text, text, StringComparison.Ordinal))
            {
                UpdateCurrentSelection(anchorPosition, focusPosition);
                return;
            }

            int redoCount = _snapshots.Count - _currentIndex - 1;
            if (redoCount > 0)
                _snapshots.RemoveRange(_currentIndex + 1, redoCount);

            _snapshots.Add(new TextEditSnapshot(text, anchorPosition, focusPosition));
            _currentIndex = _snapshots.Count - 1;
        }

        public void UpdateCurrentSelection(int anchorPosition, int focusPosition)
        {
            if (_currentIndex < 0)
                return;

            TextEditSnapshot current = _snapshots[_currentIndex];
            _snapshots[_currentIndex] = new TextEditSnapshot(current.Text, anchorPosition, focusPosition);
        }

        public bool TryUndo(out TextEditSnapshot snapshot)
        {
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
            if (!CanRedo)
            {
                snapshot = default(TextEditSnapshot);
                return false;
            }

            snapshot = _snapshots[++_currentIndex];
            return true;
        }
    }
}