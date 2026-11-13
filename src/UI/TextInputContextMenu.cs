using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ProbablyEdited
{
    /// <summary>Attaches a native-styled text-edit context menu to a TMP input field.</summary>
    public static class TextInputContextMenu
    {
        private const string WindowId = "probably_edited.text_input_context_menu";
        private const float MenuWidth = 212f;
        private const float MenuHeight = 206f;
        private const float MenuItemHeight = 34f;
        private const float MenuInset = 8f;
        private const float MenuHeaderHeight = 28f;

        private static readonly Dictionary<int, InputBinding> Bindings = new Dictionary<int, InputBinding>();
        private static readonly List<Button> MenuActionButtons = new List<Button>(5);
        private static readonly List<Button> MenuButtons = new List<Button>(6);
        private static readonly List<Graphic> PopupGraphics = new List<Graphic>();
        private static MenuSession _activeSession;
        private static int _nextGeneration;

        /// <summary>Attaches the context menu and initializes history for the supplied logical editing target.</summary>
        /// <param name="element">The Custom UI element containing the input field.</param>
        /// <param name="logicalTarget">A stable equality key for the current editing target; use the same key only while its text belongs to the same target.</param>
        /// <param name="readOnly">When true, keeps edit actions disabled while leaving selection and copy available.</param>
        public static void Attach(CustomUIElement element, object logicalTarget, bool readOnly = false)
        {
            if (element == null)
                throw new ArgumentNullException(nameof(element));
            if (element.input == null)
                throw new ArgumentException("The UI element has no TMP input field.", nameof(element));

            Attach(element.input, logicalTarget, readOnly);
        }

        /// <summary>Attaches the context menu directly to a TextMesh Pro input field.</summary>
        public static void Attach(TMP_InputField input, object logicalTarget, bool readOnly = false)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            int instanceId = input.GetInstanceID();
            if (!Bindings.TryGetValue(instanceId, out InputBinding binding) || binding.Input != input)
            {
                if (binding != null)
                    RemoveBinding(binding);

                binding = new InputBinding(input);
                Bindings[instanceId] = binding;
            }

            TextInputContextMenuInputRelay relay = input.GetComponent<TextInputContextMenuInputRelay>();
            if (relay == null)
                relay = input.gameObject.AddComponent<TextInputContextMenuInputRelay>();

            binding.Relay = relay;
            relay.Configure(input, instanceId);
            binding.ConfigureTarget(logicalTarget, readOnly);
        }

        /// <summary>Resets history after a target or programmatic text change; unchanged key and text preserve the undo chain.</summary>
        public static void ResetHistory(CustomUIElement element, object logicalTarget)
        {
            if (element == null)
                throw new ArgumentNullException(nameof(element));
            if (element.input == null)
                throw new ArgumentException("The UI element has no TMP input field.", nameof(element));

            ResetHistory(element.input, logicalTarget);
        }

        public static void ResetHistory(TMP_InputField input, object logicalTarget)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            int instanceId = input.GetInstanceID();
            if (!Bindings.TryGetValue(instanceId, out InputBinding binding) || binding.Input != input)
                throw new InvalidOperationException("Attach the context menu before resetting its history.");

            binding.ConfigureTarget(logicalTarget, binding.ConfiguredReadOnly);
        }

        /// <summary>Begins or joins an atomic edit; nested scopes commit with the outermost scope.</summary>
        public static IDisposable BeginAtomicEdit(CustomUIElement element)
        {
            if (element == null)
                throw new ArgumentNullException(nameof(element));
            if (element.input == null)
                throw new ArgumentException("The UI element has no TMP input field.", nameof(element));

            return BeginAtomicEdit(element.input);
        }

        /// <summary>Begins or joins an atomic edit; nested scopes commit with the outermost scope.</summary>
        public static IDisposable BeginAtomicEdit(TMP_InputField input)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            int instanceId = input.GetInstanceID();
            if (!Bindings.TryGetValue(instanceId, out InputBinding binding) || binding.Input != input)
                throw new InvalidOperationException("Attach the context menu before beginning an atomic edit.");

            return binding.BeginAtomicEdit();
        }

        /// <summary>Closes the active context menu and restores its input selection.</summary>
        public static void CloseAll()
        {
            MenuSession session = _activeSession;
            if (session == null)
                return;

            if (session.Window != null && session.Window.IsAlive)
                session.Window.Close();
            else
                FinishSession(session);
        }

        internal static void Open(TMP_InputField input, PointerEventData eventData)
        {
            if (input == null || eventData == null)
                return;

            int instanceId = input.GetInstanceID();
            if (!Bindings.TryGetValue(instanceId, out InputBinding binding) || binding.Input != input)
                return;
            if (!IsTargetUsable(input))
                return;

            binding.ObserveInputState();
            CloseAll();
            binding.FlushTypingGroup();
            binding.CaptureSelection();

            MenuSession session = new MenuSession(binding, eventData.position, eventData.pressEventCamera ?? eventData.enterEventCamera);
            _activeSession = session;
            ShowMenu(session);
            eventData.Use();
        }

        internal static void InvokeAction(int generation, int action)
        {
            MenuSession session = _activeSession;
            if (session == null || session.Generation != generation)
                return;
            if (!IsSessionUsable(session))
            {
                CloseAll();
                return;
            }
            if (!IsActionEnabled(session, action))
                return;

            InputBinding binding = session.Binding;
            TMP_InputField input = binding.Input;

            switch (action)
            {
                case MenuAction.SelectAll:
                    binding.SetSelection(0, input.text.Length);
                    session.SetSelection(0, input.text.Length);
                    break;
                case MenuAction.Copy:
                    CopySelection(session);
                    break;
                case MenuAction.Paste:
                    PasteSelection(session);
                    break;
                case MenuAction.Undo:
                    binding.Undo();
                    session.CaptureSelection();
                    break;
                case MenuAction.Redo:
                    binding.Redo();
                    session.CaptureSelection();
                    break;
                default:
                    return;
            }

            CloseAll();
        }

        internal static void CheckTarget(CustomUIWindow window)
        {
            MenuSession session = _activeSession;
            if (session == null || session.Window != window)
                return;

            if (!IsSessionUsable(session))
                CloseAll();
        }

        internal static void MenuRootDestroyed(CustomUIWindow window)
        {
            MenuSession session = _activeSession;
            if (session != null && session.Window == window)
                FinishSession(session);
        }

        internal static void RemoveInput(int instanceId, TextInputContextMenuInputRelay relay)
        {
            if (!Bindings.TryGetValue(instanceId, out InputBinding binding) || binding.Relay != relay)
                return;

            RemoveBinding(binding);
            Bindings.Remove(instanceId);
        }
        internal static void RestoreSelection(TMP_InputField input, int anchor, int focus)
        {
            if (input == null)
                return;

            int instanceId = input.GetInstanceID();
            if (Bindings.TryGetValue(instanceId, out InputBinding binding) && binding.Input == input)
                binding.SetSelection(anchor, focus);
        }

        internal static void ObserveInput(TMP_InputField input)
        {
            if (input == null)
                return;

            int instanceId = input.GetInstanceID();
            if (Bindings.TryGetValue(instanceId, out InputBinding binding) && binding.Input == input)
                binding.ObserveInputState();
        }

        internal static void FinalizePendingInput(TMP_InputField input)
        {
            if (input == null)
                return;

            int instanceId = input.GetInstanceID();
            if (Bindings.TryGetValue(instanceId, out InputBinding binding) && binding.Input == input)
                binding.FinalizePendingAtomicEdit();
        }

        internal static void InputDeselected(TMP_InputField input)
        {
            if (input == null)
                return;

            int instanceId = input.GetInstanceID();
            if (Bindings.TryGetValue(instanceId, out InputBinding binding) && binding.Input == input)
            {
                binding.ObserveInputState();
                binding.CaptureSelection();
                binding.FlushTypingGroup();
            }
        }

        internal static bool TryHandleHistoryShortcut(TMP_InputField input)
        {
            if (input == null || !input.isFocused)
                return false;

            TextHistoryCommand command = GetCurrentHistoryCommand();
            if (command == TextHistoryCommand.None)
                return false;

            int instanceId = input.GetInstanceID();
            if (!Bindings.TryGetValue(instanceId, out InputBinding binding) || binding.Input != input)
                return false;
            if (!IsTargetUsable(input) || !binding.CanEdit)
                return false;

            binding.Relay?.CancelPendingRestore();
            binding.CaptureSelection();
            MenuSession session = _activeSession;
            if (session != null && session.Binding == binding)
                InvokeAction(session.Generation, command == TextHistoryCommand.Undo ? MenuAction.Undo : MenuAction.Redo);
            else if (command == TextHistoryCommand.Undo)
                binding.Undo();
            else
                binding.Redo();
            return true;
        }

        private static TextHistoryCommand GetCurrentHistoryCommand()
        {
            bool isMacOS = Application.platform == RuntimePlatform.OSXPlayer
                || Application.platform == RuntimePlatform.OSXEditor;
            return TextHistoryShortcut.Match(isMacOS,
                Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl),
                Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand),
                Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift),
                Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt),
                Input.GetKeyDown(KeyCode.Z), Input.GetKeyDown(KeyCode.Y),
                !string.IsNullOrEmpty(Input.compositionString));
        }

        private static bool IsNativePasteOrCutGesture()
        {
            bool isMacOS = Application.platform == RuntimePlatform.OSXPlayer
                || Application.platform == RuntimePlatform.OSXEditor;
            bool primaryModifierHeld = isMacOS
                ? Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand)
                : Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            return primaryModifierHeld && (Input.GetKey(KeyCode.V) || Input.GetKey(KeyCode.X));
        }

        private static double GetUnscaledTime()
        {
            return Time.unscaledTime;
        }


        private static void ShowMenu(MenuSession session)
        {
            CustomUIManager manager = CustomUIManager.Instance;
            if (manager == null)
                throw new InvalidOperationException("Custom UI is unavailable while opening the text context menu.");

            string layer = StoreUIManager.Instance != null
                ? CustomUIManager.LAYER_META
                : CustomUIManager.LAYER_MENU;
            CustomUIBuilder builder = manager.CreateWindow(
                    WindowId,
                    string.Empty,
                    layer,
                    manager.optionUIPrefab,
                    CustomUIManager.Style.Auto)
                .UseMetaPrefabs()
                .SetDraggable(false)
                .SetCloseOnEscape(true)
                .OnClose(OnMenuWindowClosed);

            builder.BeginColumn(0f, TextAnchor.UpperLeft);
            MenuActionButtons.Clear();
            AddMenuItem(builder, session, MenuAction.SelectAll, UiText.Get("menu.select_all"), session.Binding.Input.text.Length > 0, "select_all");
            AddMenuItem(builder, session, MenuAction.Copy, UiText.Get("menu.copy"), HasSelection(session), "copy");
            AddMenuItem(builder, session, MenuAction.Paste, UiText.Get("menu.paste"), CanPaste(session), "paste");
            AddMenuItem(builder, session, MenuAction.Undo, UiText.Get("menu.undo"), CanUndo(session), "undo");
            AddMenuItem(builder, session, MenuAction.Redo, UiText.Get("menu.redo"), CanRedo(session), "redo");
            builder.End();

            CustomUIWindow window = builder.Handle;
            session.Window = window;
            if (window.frame != null && window.frame.titleText != null)
                window.frame.titleText.text = string.Empty;

            HideOtherButtons(window);
            RemoveBackdropDimming(window);
            InstallDismissLayer(window);
            window.Rect.gameObject.AddComponent<TextInputContextMenuWindowRelay>().Configure(window);
            builder.Show();
            PositionMenu(window, session.ScreenPosition, session.EventCamera);
        }

        private static void AddMenuItem(
            CustomUIBuilder builder,
            MenuSession session,
            int action,
            string label,
            bool enabled,
            string id)
        {
            builder.AddButton(label, IgnoreNativeButtonClick, "text_context_" + id);
            CustomUIElement element = builder.Last;
            element.SetPreferredSize(MenuWidth - MenuInset * 2f, MenuItemHeight);
            element.SetInteractable(enabled);

            Button button = element.Rect != null ? element.Rect.GetComponent<Button>() : null;
            if (button == null && element.Rect != null)
                button = element.Rect.GetComponentInChildren<Button>(true);
            if (button == null)
                throw new InvalidOperationException("A native Custom UI menu item has no Button component.");

            TextInputContextMenuActionRelay relay = button.GetComponent<TextInputContextMenuActionRelay>();
            if (relay == null)
                relay = button.gameObject.AddComponent<TextInputContextMenuActionRelay>();
            relay.Configure(session.Generation, action, enabled);
            button.enabled = false;
            MenuActionButtons.Add(button);

            if (!enabled && element.text != null)
                element.text.color = Color.Lerp(element.text.color, Color.gray, 0.45f);
        }

        private static void HideOtherButtons(CustomUIWindow window)
        {
            MenuButtons.Clear();
            window.Rect.GetComponentsInChildren(true, MenuButtons);
            for (int i = 0; i < MenuButtons.Count; i++)
            {
                Button button = MenuButtons[i];
                if (!MenuActionButtons.Contains(button))
                    button.gameObject.SetActive(false);
            }
        }

        private static RectTransform GetNativeWindowPanel(CustomUIWindow window)
        {
            if (window == null || window.frame == null || window.frame.titleText == null)
                return null;

            RectTransform contentRoot = window.ContentRoot;
            RectTransform titleText = window.frame.titleText.rectTransform;
            Transform panel = contentRoot != null ? contentRoot.parent : null;
            return panel != null && panel == titleText.parent ? panel as RectTransform : null;
        }

        private static void RemoveBackdropDimming(CustomUIWindow window)
        {
            if (window == null || window.Rect == null)
                return;

            RectTransform panel = GetNativeWindowPanel(window);
            if (panel == null)
                return;
            PopupGraphics.Clear();
            window.Rect.GetComponentsInChildren(true, PopupGraphics);
            foreach (Graphic graphic in PopupGraphics)
            {
                if (graphic == null || graphic.transform == panel || graphic.transform.IsChildOf(panel))
                    continue;
                Color color = graphic.color;
                color.a = 0f;
                graphic.color = color;
            }
            PopupGraphics.Clear();
        }

        private static void InstallDismissLayer(CustomUIWindow window)
        {
            GameObject overlay = new GameObject(
                "Text input context menu dismiss layer",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(TextInputContextMenuDismissRelay));
            RectTransform rect = overlay.transform as RectTransform;
            rect.SetParent(window.Rect, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.SetAsLastSibling();

            RectTransform panel = GetNativeWindowPanel(window);
            if (panel != null && panel.parent == window.Rect)
                panel.SetAsLastSibling();

            Image image = overlay.GetComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0f);
            image.raycastTarget = true;
        }

        private static void PositionMenu(CustomUIWindow window, Vector2 screenPosition, Camera eventCamera)
        {
            RectTransform panel = GetNativeWindowPanel(window);
            RectTransform contentRoot = window != null ? window.ContentRoot : null;
            RectTransform panelParent = panel != null ? panel.parent as RectTransform : null;
            if (window == null || window.Rect == null || panel == null || contentRoot == null || panelParent == null)
                throw new InvalidOperationException("The native text context menu has no viewport or panel.");

            Rect bounds = panelParent.rect;
            float width = Mathf.Min(MenuWidth, bounds.width);
            float height = Mathf.Min(MenuHeight, bounds.height);

            panel.anchorMin = panelParent.pivot;
            panel.anchorMax = panelParent.pivot;
            panel.pivot = new Vector2(0f, 1f);
            panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);

            contentRoot.anchorMin = Vector2.zero;
            contentRoot.anchorMax = Vector2.one;
            contentRoot.offsetMin = new Vector2(MenuInset, MenuInset);
            contentRoot.offsetMax = new Vector2(-MenuInset, -MenuHeaderHeight);
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRoot);

            Vector2 localPosition;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(panelParent, screenPosition, eventCamera, out localPosition))
                localPosition = Vector2.zero;

            float x = Mathf.Clamp(localPosition.x + MenuInset, bounds.xMin, bounds.xMax - width);
            float y = Mathf.Clamp(localPosition.y - MenuInset, bounds.yMin + height, bounds.yMax);
            panel.anchoredPosition = new Vector2(x, y);
        }

        private static bool IsActionEnabled(MenuSession session, int action)
        {
            switch (action)
            {
                case MenuAction.SelectAll:
                    return session.Binding.Input.text.Length > 0;
                case MenuAction.Copy:
                    return HasSelection(session);
                case MenuAction.Paste:
                    return CanPaste(session);
                case MenuAction.Undo:
                    return CanUndo(session);
                case MenuAction.Redo:
                    return CanRedo(session);
                default:
                    return false;
            }
        }

        private static bool HasSelection(MenuSession session)
        {
            return session.AnchorPosition != session.FocusPosition;
        }

        private static bool CanPaste(MenuSession session)
        {
            return session.Binding.CanEdit && !string.IsNullOrEmpty(GUIUtility.systemCopyBuffer);
        }

        private static bool CanUndo(MenuSession session)
        {
            return session.Binding.CanEdit && session.Binding.CanUndo;
        }

        private static bool CanRedo(MenuSession session)
        {
            return session.Binding.CanEdit && session.Binding.CanRedo;
        }

        private static bool IsTargetUsable(TMP_InputField input)
        {
            return input != null
                && input.isActiveAndEnabled
                && input.gameObject.activeInHierarchy
                && input.IsInteractable();
        }

        private static bool IsSessionUsable(MenuSession session)
        {
            TMP_InputField input = session.Binding.Input;
            if (!IsTargetUsable(input))
                return false;
            if (!Equals(session.LogicalTarget, session.Binding.LogicalTarget))
                return false;

            return string.Equals(input.text, session.Binding.CurrentSnapshot.Text, StringComparison.Ordinal);
        }

        private static void CopySelection(MenuSession session)
        {
            TMP_InputField input = session.Binding.Input;
            string text = input.text ?? string.Empty;
            int start = Mathf.Clamp(Mathf.Min(session.AnchorPosition, session.FocusPosition), 0, text.Length);
            int end = Mathf.Clamp(Mathf.Max(session.AnchorPosition, session.FocusPosition), start, text.Length);
            if (end > start)
                GUIUtility.systemCopyBuffer = text.Substring(start, end - start);
        }

        private static void PasteSelection(MenuSession session)
        {
            TMP_InputField input = session.Binding.Input;
            string current = input.text ?? string.Empty;
            string clipboard = GUIUtility.systemCopyBuffer ?? string.Empty;
            int start = Mathf.Clamp(Mathf.Min(session.AnchorPosition, session.FocusPosition), 0, current.Length);
            int end = Mathf.Clamp(Mathf.Max(session.AnchorPosition, session.FocusPosition), start, current.Length);
            StringBuilder replacement = new StringBuilder(current.Length - (end - start) + clipboard.Length);
            replacement.Append(current, 0, start);
            replacement.Append(clipboard);
            replacement.Append(current, end, current.Length - end);
            int caret = start + clipboard.Length;

            session.Binding.ApplyContextEdit(replacement.ToString(), caret, caret);
            session.SetSelection(caret, caret);
        }

        private static void IgnoreNativeButtonClick()
        {
        }

        private static void OnMenuWindowClosed()
        {
            MenuSession session = _activeSession;
            if (session != null)
                FinishSession(session);
        }

        private static void FinishSession(MenuSession session)
        {
            if (_activeSession == session)
                _activeSession = null;

            if (IsSessionUsable(session))
                session.RestoreSelection();
        }

        private static void RemoveBinding(InputBinding binding)
        {
            if (_activeSession != null && _activeSession.Binding == binding)
                CloseAll();

            binding.Dispose();
        }

        private static class MenuAction
        {
            public const int SelectAll = 0;
            public const int Copy = 1;
            public const int Paste = 2;
            public const int Undo = 3;
            public const int Redo = 4;
        }

        private sealed class InputBinding : IDisposable
        {
            private bool _hasTarget;
            private bool _historyInitialized;
            private bool _applyingHistory;
            private bool _applyingContextEdit;
            private bool _compositionPending;
            private long _compositionTargetVersion;
            private int _selectionReplacementFrame = -1;
            private readonly TextInputEditTransaction _transactions;
            private readonly TMP_InputField.OnValidateInput previousValidation;
            private readonly TMP_InputField.OnValidateInput historyValidation;

            public InputBinding(TMP_InputField input)
            {
                Input = input;
                _transactions = new TextInputEditTransaction(GetUnscaledTime);
                previousValidation = Input.onValidateInput;
                historyValidation = ValidateCharacter;
                Input.onValidateInput = historyValidation;
                Input.onValueChanged.AddListener(OnValueChanged);
            }

            public TMP_InputField Input { get; }
            public TextEditSnapshot CurrentSnapshot => _transactions.Current;
            public bool CanUndo => _transactions.CanUndo;
            public bool CanRedo => _transactions.CanRedo;
            public TextInputContextMenuInputRelay Relay { get; set; }
            public object LogicalTarget { get; private set; }
            public bool ConfiguredReadOnly { get; private set; }
            public bool IsReadOnly => ConfiguredReadOnly || Input == null || Input.readOnly;
            public bool CanEdit => !IsReadOnly && Input != null && Input.IsInteractable();

            public void ConfigureTarget(object logicalTarget, bool readOnly)
            {
                bool targetChanged = !_hasTarget || !Equals(LogicalTarget, logicalTarget);
                string text = Input.text ?? string.Empty;
                bool textChanged = !_historyInitialized || !string.Equals(CurrentSnapshot.Text, text, StringComparison.Ordinal);
                if (targetChanged)
                    LogicalTarget = logicalTarget;

                if (targetChanged || textChanged)
                {
                    bool compositionPending = _compositionPending || !string.IsNullOrEmpty(UnityEngine.Input.compositionString);
                    long compositionTargetVersion = _compositionPending ? _compositionTargetVersion : _transactions.TargetVersion;
                    if (_activeSession != null && _activeSession.Binding == this)
                        CloseAll();

                    Relay?.CancelPendingRestore();
                    _selectionReplacementFrame = -1;
                    _transactions.Reset(text, Input.selectionStringAnchorPosition, Input.selectionStringFocusPosition);
                    _compositionPending = compositionPending;
                    _compositionTargetVersion = !_hasTarget ? _transactions.TargetVersion : compositionTargetVersion;
                    _historyInitialized = true;
                }

                LogicalTarget = logicalTarget;
                ConfiguredReadOnly = readOnly;
                _hasTarget = true;
            }

            public IDisposable BeginAtomicEdit()
            {
                return _transactions.BeginAtomicEdit();
            }

            public void FlushTypingGroup()
            {
                _transactions.FlushTypingGroup();
            }

            public void ObserveInputState()
            {
                if (Input == null || !_historyInitialized)
                    return;

                FinalizePendingAtomicEdit();
                if (_compositionPending && string.IsNullOrEmpty(UnityEngine.Input.compositionString))
                {
                    long targetVersion = _compositionTargetVersion;
                    _compositionPending = false;
                    _selectionReplacementFrame = -1;
                    DeferAtomicInputEdit(Input.text, Input.selectionStringAnchorPosition, Input.selectionStringFocusPosition, targetVersion);
                    return;
                }

                if (_compositionPending || !Input.isFocused)
                    return;

                int anchor = Input.selectionStringAnchorPosition;
                int focus = Input.selectionStringFocusPosition;
                TextEditSnapshot current = _transactions.Current;
                if (current.AnchorPosition != anchor || current.FocusPosition != focus)
                    _transactions.UpdateCurrentSelection(anchor, focus);
            }

            public void CaptureSelection()
            {
                if (Input == null || !_historyInitialized || _compositionPending)
                    return;

                FinalizePendingAtomicEdit();
                _transactions.UpdateCurrentSelection(Input.selectionStringAnchorPosition, Input.selectionStringFocusPosition);
            }

            public void SetSelection(int anchor, int focus)
            {
                if (Input == null)
                    return;

                int length = (Input.text ?? string.Empty).Length;
                anchor = Mathf.Clamp(anchor, 0, length);
                focus = Mathf.Clamp(focus, 0, length);
                Input.stringPosition = focus;
                Input.selectionStringAnchorPosition = anchor;
                Input.selectionStringFocusPosition = focus;
                _transactions.UpdateCurrentSelection(anchor, focus);
            }

            public void ApplyContextEdit(string text, int anchor, int focus)
            {
                if (Input == null)
                    return;

                using (TextInputEditTransaction.Scope edit = _transactions.BeginAtomicEdit())
                {
                    _applyingContextEdit = true;
                    try
                    {
                        if (!string.Equals(Input.text, text, StringComparison.Ordinal))
                            Input.text = text;
                    }
                    finally
                    {
                        _applyingContextEdit = false;
                    }

                    SetSelection(anchor, focus);
                    edit.RecordAtomic(Input.text, anchor, focus);
                }
            }

            public void Undo()
            {
                if (!_transactions.TryUndo(out TextEditSnapshot snapshot))
                    return;

                ApplyHistorySnapshot(snapshot);
            }

            public void Redo()
            {
                if (!_transactions.TryRedo(out TextEditSnapshot snapshot))
                    return;

                ApplyHistorySnapshot(snapshot);
            }

            public void Dispose()
            {
                if (Input != null)
                {
                    Input.onValueChanged.RemoveListener(OnValueChanged);
                    if (Input.onValidateInput == historyValidation)
                        Input.onValidateInput = previousValidation;
                }
                Relay?.CancelPendingRestore();
                Relay = null;
            }

            private char ValidateCharacter(string text, int position, char character)
            {
                // TMP may forward a shortcut's nonzero character after its key switch.
                if (CanEdit && Input.isFocused && IsTargetUsable(Input)
                    && TextHistoryShortcut.BlocksCharacter(GetCurrentHistoryCommand(), character))
                    return '\0';

                char validated = previousValidation != null ? previousValidation(text, position, character) : character;
                if (validated != '\0' && Input.selectionStringAnchorPosition != Input.selectionStringFocusPosition)
                    _selectionReplacementFrame = Time.frameCount;
                return validated;
            }

            private void OnValueChanged(string text)
            {
                if (_applyingHistory || Input == null)
                    return;

                _transactions.FinalizePendingAtomicEdit(Time.frameCount);
                if (!string.IsNullOrEmpty(UnityEngine.Input.compositionString))
                {
                    if (!_compositionPending)
                        _compositionTargetVersion = _transactions.TargetVersion;
                    _compositionPending = true;
                    _selectionReplacementFrame = -1;
                    _transactions.BreakTypingGroup();
                    CloseMenuForValueChange();
                    return;
                }

                if (_compositionPending)
                {
                    long targetVersion = _compositionTargetVersion;
                    _compositionPending = false;
                    DeferAtomicInputEdit(text, Input.selectionStringAnchorPosition, Input.selectionStringFocusPosition, targetVersion);
                    CloseMenuForValueChange();
                    return;
                }

                bool isNativeClipboardEdit = !_transactions.IsAtomicEditActive
                    && (_transactions.HasPendingAtomicEdit || IsNativePasteOrCutGesture());
                if (isNativeClipboardEdit)
                {
                    DeferAtomicInputEdit(text, Input.selectionStringAnchorPosition, Input.selectionStringFocusPosition, _transactions.TargetVersion);
                    CloseMenuForValueChange();
                    return;
                }

                bool isAtomic = _transactions.IsAtomicEditActive
                    || _selectionReplacementFrame == Time.frameCount;
                _selectionReplacementFrame = -1;

                CloseMenuForValueChange();
                if (isAtomic)
                    _transactions.RecordAtomic(text, Input.selectionStringAnchorPosition, Input.selectionStringFocusPosition);
                else
                    _transactions.Record(text, Input.selectionStringAnchorPosition, Input.selectionStringFocusPosition);
            }

            private void DeferAtomicInputEdit(string text, int anchor, int focus, long targetVersion)
            {
                _transactions.QueueAtomicEdit(targetVersion, text, anchor, focus, Time.frameCount);
                _selectionReplacementFrame = -1;
                _compositionPending = false;
            }

            public void FinalizePendingAtomicEdit()
            {
                _transactions.FinalizePendingAtomicEdit();
            }

            private void CloseMenuForValueChange()
            {
                Relay?.CancelPendingRestore();
                if (!_applyingContextEdit && _activeSession != null && _activeSession.Binding == this)
                    CloseAll();
            }


            private void ApplyHistorySnapshot(TextEditSnapshot snapshot)
            {
                _applyingHistory = true;
                try
                {
                    if (!string.Equals(Input.text, snapshot.Text, StringComparison.Ordinal))
                        Input.text = snapshot.Text;
                }
                finally
                {
                    _applyingHistory = false;
                }

                SetSelection(snapshot.AnchorPosition, snapshot.FocusPosition);
            }
        }

        private sealed class MenuSession
        {
            private int _anchorPosition;
            private int _focusPosition;

            public MenuSession(InputBinding binding, Vector2 screenPosition, Camera eventCamera)
            {
                Binding = binding;
                LogicalTarget = binding.LogicalTarget;
                ScreenPosition = screenPosition;
                EventCamera = eventCamera;
                WasFocused = binding.Input.isFocused;
                _anchorPosition = binding.Input.selectionStringAnchorPosition;
                _focusPosition = binding.Input.selectionStringFocusPosition;
                Generation = ++_nextGeneration;
            }

            public InputBinding Binding { get; }
            public object LogicalTarget { get; }
            public Vector2 ScreenPosition { get; }
            public Camera EventCamera { get; }
            public bool WasFocused { get; }
            public int Generation { get; }
            public CustomUIWindow Window { get; set; }
            public int AnchorPosition => _anchorPosition;
            public int FocusPosition => _focusPosition;

            public void SetSelection(int anchor, int focus)
            {
                _anchorPosition = anchor;
                _focusPosition = focus;
            }

            public void CaptureSelection()
            {
                if (Binding.Input != null)
                {
                    _anchorPosition = Binding.Input.selectionStringAnchorPosition;
                    _focusPosition = Binding.Input.selectionStringFocusPosition;
                }
            }

            public void RestoreSelection()
            {
                TMP_InputField input = Binding.Input;
                if (input == null || !input.isActiveAndEnabled || !input.gameObject.activeInHierarchy)
                    return;

                TextInputContextMenuInputRelay relay = Binding.Relay;
                if (relay != null)
                {
                    relay.RestoreSelectionAfterActivation(WasFocused, _anchorPosition, _focusPosition);
                    return;
                }

                if (WasFocused && !input.isFocused)
                    input.ActivateInputField();
                Binding.SetSelection(_anchorPosition, _focusPosition);
            }
        }
    }

    internal sealed class TextInputContextMenuInputRelay : MonoBehaviour, IPointerClickHandler, IPointerDownHandler, IDeselectHandler
    {
        private static readonly WaitForEndOfFrame EndOfFrame = new WaitForEndOfFrame();

        private TMP_InputField _input;
        private int _instanceId;
        private int _restoreGeneration;
        private int _lastShortcutFrame = -1;

        public void Configure(TMP_InputField input, int instanceId)
        {
            _input = input;
            _instanceId = instanceId;
        }

        public void CancelPendingRestore()
        {
            _restoreGeneration++;
        }

        public void RestoreSelectionAfterActivation(bool wasFocused, int anchor, int focus)
        {
            int generation = ++_restoreGeneration;
            if (!isActiveAndEnabled || _input == null || !_input.isActiveAndEnabled || !_input.gameObject.activeInHierarchy)
                return;

            if (wasFocused && !_input.isFocused)
            {
                _input.ActivateInputField();
                StartCoroutine(RestoreAfterActivation(generation, anchor, focus));
                return;
            }

            TextInputContextMenu.RestoreSelection(_input, anchor, focus);
        }

        private void Update()
        {
            TextInputContextMenu.ObserveInput(_input);
            if (_lastShortcutFrame != Time.frameCount && TextInputContextMenu.TryHandleHistoryShortcut(_input))
                _lastShortcutFrame = Time.frameCount;
        }

        private void LateUpdate()
        {
            // TMP can emit several callbacks for one paste or IME commit; commit after input processing.
            TextInputContextMenu.FinalizePendingInput(_input);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            CancelPendingRestore();
        }

        public void OnDeselect(BaseEventData eventData)
        {
            TextInputContextMenu.InputDeselected(_input);
        }

        private void OnDisable()
        {
            TextInputContextMenu.InputDeselected(_input);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Right)
                TextInputContextMenu.Open(_input, eventData);
        }

        private IEnumerator RestoreAfterActivation(int generation, int anchor, int focus)
        {
            // TMP completes deferred input activation during LateUpdate.
            yield return null;
            yield return EndOfFrame;

            if (generation != _restoreGeneration || _input == null || !_input.isActiveAndEnabled || !_input.gameObject.activeInHierarchy)
                yield break;

            TextInputContextMenu.RestoreSelection(_input, anchor, focus);
        }

        private void OnDestroy()
        {
            CancelPendingRestore();
            TextInputContextMenu.RemoveInput(_instanceId, this);
        }
    }

    internal sealed class TextInputContextMenuActionRelay : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        private Graphic _targetGraphic;
        private Color _normalColor;
        private int _generation;
        private int _action;
        private bool _enabled;
        private bool _hasColor;

        public void Configure(int generation, int action, bool enabled)
        {
            _generation = generation;
            _action = action;
            _enabled = enabled;

            Button button = GetComponent<Button>();
            _targetGraphic = button != null ? button.targetGraphic : null;
            if (_targetGraphic != null)
            {
                _normalColor = _targetGraphic.color;
                _hasColor = true;
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_enabled && eventData.button == PointerEventData.InputButton.Left)
                TextInputContextMenu.InvokeAction(_generation, _action);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_enabled && _hasColor && _targetGraphic != null)
                _targetGraphic.color = Color.Lerp(_normalColor, Color.white, 0.12f);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_hasColor && _targetGraphic != null)
                _targetGraphic.color = _normalColor;
        }
    }

    internal sealed class TextInputContextMenuDismissRelay : MonoBehaviour, IPointerClickHandler
    {
        public void OnPointerClick(PointerEventData eventData)
        {
            TextInputContextMenu.CloseAll();
        }
    }

    internal sealed class TextInputContextMenuWindowRelay : MonoBehaviour
    {
        private CustomUIWindow _window;

        public void Configure(CustomUIWindow window)
        {
            _window = window;
        }

        private void Update()
        {
            TextInputContextMenu.CheckTarget(_window);
        }

        private void OnDestroy()
        {
            TextInputContextMenu.MenuRootDestroyed(_window);
        }
    }
}