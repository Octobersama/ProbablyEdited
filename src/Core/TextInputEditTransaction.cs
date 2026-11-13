using System;

namespace ProbablyEdited
{
    internal sealed class TextInputEditTransaction
    {
        private readonly TextEditHistory _history;
        private long _targetVersion;
        private int _scopeDepth;
        private int _expiredScopeCount;
        private bool _hasPendingSnapshot;
        private TextEditSnapshot _pendingSnapshot;
        private bool _hasDeferredSnapshot;
        private TextEditSnapshot _deferredSnapshot;
        private long _deferredTargetVersion;
        private int _deferredFrame = -1;

        public TextInputEditTransaction(Func<double> getTime)
        {
            _history = new TextEditHistory(getTime);
        }

        public TextEditSnapshot Current => _history.Current;
        public bool CanUndo => _history.CanUndo;
        public bool CanRedo => _history.CanRedo;
        public long TargetVersion => _targetVersion;
        public bool IsAtomicEditActive => _scopeDepth > 0;
        public bool HasPendingAtomicEdit => _hasDeferredSnapshot;

        public void Reset(string text, int anchorPosition, int focusPosition)
        {
            _targetVersion++;
            _expiredScopeCount += _scopeDepth;
            _scopeDepth = 0;
            _hasPendingSnapshot = false;
            _pendingSnapshot = default(TextEditSnapshot);
            _hasDeferredSnapshot = false;
            _deferredSnapshot = default(TextEditSnapshot);
            _deferredTargetVersion = _targetVersion;
            _deferredFrame = -1;
            _history.Reset(text, anchorPosition, focusPosition);
        }

        public Scope BeginAtomicEdit()
        {
            FinalizePendingAtomicEdit();
            if (_scopeDepth == 0)
                BreakTypingGroup();
            _scopeDepth++;
            return new Scope(this, _targetVersion);
        }

        public bool Record(string text, int anchorPosition, int focusPosition)
        {
            return RecordSnapshot(false, text, anchorPosition, focusPosition);
        }

        public bool RecordAtomic(string text, int anchorPosition, int focusPosition)
        {
            return RecordSnapshot(true, text, anchorPosition, focusPosition);
        }

        public bool QueueAtomicEdit(long targetVersion, string text, int anchorPosition, int focusPosition, int frame)
        {
            if (targetVersion != _targetVersion || (_expiredScopeCount > 0 && _scopeDepth == 0))
                return false;

            _deferredSnapshot = new TextEditSnapshot(text, anchorPosition, focusPosition);
            _deferredTargetVersion = targetVersion;
            _deferredFrame = frame;
            _hasDeferredSnapshot = true;
            BreakTypingGroup();
            return true;
        }

        public bool FinalizePendingAtomicEdit()
        {
            if (!_hasDeferredSnapshot)
                return false;

            TextEditSnapshot snapshot = _deferredSnapshot;
            long targetVersion = _deferredTargetVersion;
            _deferredSnapshot = default(TextEditSnapshot);
            _deferredTargetVersion = _targetVersion;
            _deferredFrame = -1;
            _hasDeferredSnapshot = false;
            return RecordSnapshot(targetVersion, true, snapshot.Text, snapshot.AnchorPosition, snapshot.FocusPosition);
        }

        public bool FinalizePendingAtomicEdit(int currentFrame)
        {
            return _hasDeferredSnapshot && _deferredFrame != currentFrame && FinalizePendingAtomicEdit();
        }

        public void UpdateCurrentSelection(int anchorPosition, int focusPosition)
        {
            if (_expiredScopeCount > 0 && _scopeDepth == 0)
                return;

            if (_scopeDepth > 0 && _hasPendingSnapshot)
            {
                _pendingSnapshot = new TextEditSnapshot(_pendingSnapshot.Text, anchorPosition, focusPosition);
                return;
            }

            _history.UpdateCurrentSelection(anchorPosition, focusPosition);
        }

        public void FlushTypingGroup()
        {
            FinalizePendingAtomicEdit();
            BreakTypingGroup();
        }

        public void BreakTypingGroup()
        {
            _history.FlushTypingGroup();
        }

        public bool TryUndo(out TextEditSnapshot snapshot)
        {
            FinalizePendingAtomicEdit();
            return _history.TryUndo(out snapshot);
        }

        public bool TryRedo(out TextEditSnapshot snapshot)
        {
            FinalizePendingAtomicEdit();
            return _history.TryRedo(out snapshot);
        }

        private bool RecordSnapshot(bool atomic, string text, int anchorPosition, int focusPosition)
        {
            return RecordSnapshot(_targetVersion, atomic, text, anchorPosition, focusPosition);
        }

        private bool RecordSnapshot(long targetVersion, bool atomic, string text, int anchorPosition, int focusPosition)
        {
            if (targetVersion != _targetVersion || (_expiredScopeCount > 0 && _scopeDepth == 0))
                return false;

            TextEditSnapshot snapshot = new TextEditSnapshot(text, anchorPosition, focusPosition);
            if (_scopeDepth > 0)
            {
                _pendingSnapshot = snapshot;
                _hasPendingSnapshot = true;
                return true;
            }

            RecordHistory(snapshot, atomic);
            return true;
        }

        private bool RecordFromScope(Scope scope, string text, int anchorPosition, int focusPosition)
        {
            if (scope == null || !scope.IsCurrent)
                return false;

            return RecordSnapshot(scope.TargetVersion, true, text, anchorPosition, focusPosition);
        }

        private void EndScope(long targetVersion)
        {
            if (targetVersion != _targetVersion)
            {
                if (_expiredScopeCount > 0)
                    _expiredScopeCount--;
                return;
            }

            _scopeDepth--;
            if (_scopeDepth != 0 || !_hasPendingSnapshot)
                return;

            TextEditSnapshot snapshot = _pendingSnapshot;
            _pendingSnapshot = default(TextEditSnapshot);
            _hasPendingSnapshot = false;
            RecordHistory(snapshot, true);
        }

        private void RecordHistory(TextEditSnapshot snapshot, bool atomic)
        {
            if (atomic)
                _history.RecordAtomic(snapshot.Text, snapshot.AnchorPosition, snapshot.FocusPosition);
            else
                _history.Record(snapshot.Text, snapshot.AnchorPosition, snapshot.FocusPosition);
        }

        internal sealed class Scope : IDisposable
        {
            private TextInputEditTransaction _owner;
            private readonly long _targetVersion;

            internal Scope(TextInputEditTransaction owner, long targetVersion)
            {
                _owner = owner;
                _targetVersion = targetVersion;
            }

            public bool IsCurrent => _owner != null && _targetVersion == _owner._targetVersion;
            public long TargetVersion => _targetVersion;

            public bool RecordAtomic(string text, int anchorPosition, int focusPosition)
            {
                TextInputEditTransaction owner = _owner;
                return owner != null && owner.RecordFromScope(this, text, anchorPosition, focusPosition);
            }

            public void Dispose()
            {
                TextInputEditTransaction owner = _owner;
                _owner = null;
                if (owner != null)
                    owner.EndScope(_targetVersion);
            }
        }
    }
}
