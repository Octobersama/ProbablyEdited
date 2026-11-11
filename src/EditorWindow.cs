using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

namespace ProbablyEdited
{
    public sealed partial class EditorWindow
    {
        private const string WindowId = "probably_edited.editor";
        private const string ValidationWindowId = "probably_edited.editor.validation";
        private const string ErrorWindowId = "probably_edited.editor.error";
        private const string ImportWindowId = "probably_edited.editor.import";
        private const string ExportWindowId = "probably_edited.editor.export";
        private const string CoverageWindowId = "probably_edited.editor.coverage";
        private const string CategoryHelpLinkId = "probably_edited.editor:category_help";
        private const float MainWindowWidthFraction = 0.84f;
        private const float MainWindowHeightFraction = 0.90f;
        private const float MainWindowHorizontalGutter = 48f;
        private const float MainWindowVerticalGutter = 24f;
        private const float MainWindowScreenMargin = 16f;
        private const float ResultRowHeight = 48f;
        private const float ResultRowFontScale = 0.88f;

        private const float SearchClearButtonWidth = 28f;
        private const float SearchClearButtonInset = 2f;
        private const float SearchClearTextInset = 32f;
        private static List<string> BuildModifiedOptions()
        {
            return new List<string>
            {
                UiText.Get("editor.filter.modified.all"),
                UiText.Get("editor.filter.modified.changed"),
                UiText.Get("editor.filter.modified.unchanged")
            };
        }

        private static List<string> BuildConflictOptions()
        {
            return new List<string>
            {
                UiText.Get("editor.filter.conflict.keep_local"),
                UiText.Get("editor.filter.conflict.use_imported")
            };
        }

        private readonly EditorController _controller;
        private CustomUIWindow _window;
        private CustomUIWindow _validationWindow;
        private CustomUIWindow _errorWindow;
        private CustomUIWindow _importWindow;
        private CustomUIWindow _exportWindow;
        private CustomUIWindow _coverageWindow;
        private bool _subscribed;
        private bool _wasLoading;
        private bool _awaitingLocale;
        private bool _saveApplyPending;
        private bool _saveApplyCallActive;
        private string _displayedPresetId;
        private string _displayedLocale;
        private bool _hasDisplayedContext;
        private string _preservedSourceId;
        private string _preservedTableId;
        private string _preservedKey;
        private bool _validationApproved;
        private string _searchQuery = string.Empty;
        private string _replacementDraft = string.Empty;
        private string _selectedCategory;
        private string _selectedSource;
        private bool _allSourcesExplicitlySelected;
        private int _modifiedFilter;
        private int _page;
        private TextEntry _selectedEntry;
        private TextEntry _pendingEntry;
        private string _pendingDraft;
        private Action _pendingTransition;
        private string _pendingTransitionName;
        private RectTransform _resultRowsHost;
        private UnityEngine.UI.ScrollRect _resultRowsScrollRect;
        private float _resultRowWidth;
        private readonly List<float> _rowBaseFontSizes = new List<float>();
        private readonly List<TextEntry> _rowEntries = new List<TextEntry>();
        private readonly List<CustomUIElement> _rowElements = new List<CustomUIElement>();
        private readonly List<ResultRowSelection> _rowSelections = new List<ResultRowSelection>();
        private readonly List<TextMeshProUGUI> _rowTextCandidates = new List<TextMeshProUGUI>();
        private CustomUIElement _emptyResultsLabel;
        private List<string> _localeCodes = new List<string>();
        private List<string> _localeNames = new List<string>();
        private List<string> _categoryValues = new List<string>();
        private List<string> _categoryLabels = new List<string>();
        private List<string> _sourceValues = new List<string>();
        private List<string> _sourceLabels = new List<string>();
        private CustomUIElement _localeDropdown;
        private CustomUIElement _categoryDropdown;
        private CustomUIElement _sourceDropdown;
        private CustomUIElement _modifiedDropdown;
        private CustomUIElement _searchInput;
        private CustomUIElement _clearSearchButton;
        private CustomUIElement _pageLabel;
        private CustomUIElement _previousPageButton;
        private CustomUIElement _nextPageButton;
        private CustomUIElement _entryTitle;
        private CustomUIElement _entrySource;
        private CustomUIElement _entryCategory;
        private CustomUIElement _entryDetailsButton;
        private CustomUIElement _originalInput;
        private CustomUIElement _replacementInput;
        private TextDiffHighlighter _diffHighlighter;
        private CustomUIElement _draftLabel;
        private CustomUIElement _stageButton;
        private CustomUIElement _restoreButton;
        private CustomUIElement _statusLabel;
        private CustomUIElement _dirtyLabel;
        private CustomUIElement _saveButton;
        private CustomUIElement _closeButton;
        private CustomUIElement _openImportButton;
        private CustomUIElement _openExportButton;
        private CustomUIElement _openCoverageButton;
        private CustomUIElement _importFilesDropdown;
        private CustomUIElement _importFileLabel;
        private CustomUIElement _importPreviewText;
        private CustomUIElement _importPreviewButton;
        private CustomUIElement _importConfirmButton;
        private CustomUIElement _exportResultInput;
        private string _exportName = string.Empty;
        private List<string> _importFiles = new List<string>();
        private int _importFileIndex;
        private bool _overwriteImportConflicts;

        public bool IsOpen => _window != null && _window.IsAlive;

        public EditorWindow(EditorController controller)
        {
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        }

        public CustomUIWindow Open()
        {
            if (_window != null && _window.IsAlive)
            {
                Subscribe();
                _window.Show();
                _window.BringToFront();
                return _window;
            }

            Subscribe();
            BuildMainWindow();
            _wasLoading = _controller.IsLoading;
            SetMainControlsInteractable(!_controller.IsLoading);
            RefreshPresetControls();
            RefreshLocaleOptions();
            RefreshFilterOptions();
            if (IsDisplayContextCurrent())
            {
                SelectInitialEntry();
            }
            else
            {
                RebindSelectedEntryToCurrentState();
            }
            RefreshDetail();
            RefreshRows();
            RefreshMainStatus();
            return _window;
        }

        public void Close()
        {
            CloseAuxiliaryWindows();
            if (_window != null && _window.IsAlive)
            {
                _window.Close();
            }

            Unsubscribe();
        }

        private static CustomUIBuilder CreateNativeSettingsWindow(string id, string title)
        {
            CustomUIManager manager = CustomUIManager.Instance;
            string layer = StoreUIManager.Instance != null
                ? CustomUIManager.LAYER_META
                : CustomUIManager.LAYER_MENU;

            return manager.CreateWindow(id, title, layer, manager.optionUIPrefab, CustomUIManager.Style.Auto)
                .SetDraggable(false);
        }

        private static Vector2 GetNativeViewportSize(CustomUIWindow window)
        {
            RectTransform root = window != null ? window.Rect : null;
            RectTransform viewport = root != null ? root.parent as RectTransform : null;
            if (viewport == null || viewport.rect.width <= 0f || viewport.rect.height <= 0f)
            {
                throw new InvalidOperationException(UiText.Get("editor.window.viewport_missing"));
            }

            return viewport.rect.size;
        }

        private static RectTransform GetNativeWindowPanel(CustomUIWindow window)
        {
            if (window == null || window.frame == null || window.frame.titleText == null)
            {
                return null;
            }

            // The native overlay root is full-screen; the visible plate is shared by its content and title.
            RectTransform contentRoot = window.ContentRoot;
            RectTransform titleText = window.frame.titleText.rectTransform;
            Transform panel = contentRoot != null ? contentRoot.parent : null;
            return panel != null && panel == titleText.parent ? panel as RectTransform : null;
        }

        private void BuildMainWindow()
        {
            CustomUIBuilder builder = CreateNativeSettingsWindow(WindowId, UiText.Get("editor.window.title"))
                .UseMetaPrefabs()
                .SetCloseOnEscape(false)
                .OnClose(OnMainWindowClosed);
            CustomUIWindow window = builder.Handle;
            Vector2 viewportSize = GetNativeViewportSize(window);
            RectTransform panel = GetNativeWindowPanel(window);
            if (panel == null)
            {
                throw new InvalidOperationException(UiText.Get("editor.window.panel_missing"));
            }

            Vector2 panelSize = new Vector2(viewportSize.x * MainWindowWidthFraction,
                Mathf.Min(panel.rect.height, viewportSize.y * MainWindowHeightFraction));
            panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, panelSize.x);
            panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, panelSize.y);
            panel.anchoredPosition = Vector2.zero;
            builder.Center();

            RectTransform contentRoot = window.ContentRoot;
            Vector2 contentSize = contentRoot.rect.size;
            Vector2 contentPosition = contentRoot.anchoredPosition;
            Vector2 maxPanelSize = new Vector2(
                Mathf.Max(0f, viewportSize.x - MainWindowScreenMargin * 2f),
                Mathf.Max(0f, viewportSize.y - MainWindowScreenMargin * 2f));
            panelSize = Vector2.Min(panelSize + new Vector2(
                MainWindowHorizontalGutter * 2f, MainWindowVerticalGutter * 2f), maxPanelSize);
            panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, panelSize.x);
            panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, panelSize.y);
            contentRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, contentSize.x);
            contentRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, contentSize.y);
            contentRoot.anchoredPosition = contentPosition;

            float contentWidth = window.ContentRoot.rect.width;
            float resultsPanelWidth = Mathf.Min(Mathf.Max(contentWidth * 0.34f, 360f), contentWidth * 0.46f);
            float editorInputWidth = Mathf.Max(0f, contentWidth - resultsPanelWidth - 80f);
            _resultRowWidth = Mathf.Max(0f, resultsPanelWidth - 30f);
            TMPIconHover.CustomLinks[CategoryHelpLinkId] = GetCategoryHelpTooltip;

            BuildPresetControls(builder);

            builder.BeginRow(8f, TextAnchor.MiddleLeft);
            builder.AddLabel(UiText.Get("editor.filter.language"));
            builder.Last.SetFixedWidth(72f);
            builder.AddDropdown(BuildLocaleOptions(), GetSelectedLocaleIndex(), OnLocaleChanged, "locale");
            _localeDropdown = builder.Last;
            _localeDropdown.SetFixedWidth(300f);
            builder.End();

            builder.BeginRow(8f, TextAnchor.MiddleLeft);
            builder.AddLabel(UiText.Get("editor.filter.source"));
            builder.Last.SetFixedWidth(72f);
            builder.AddDropdown(BuildSourceOptions(), FindValueIndex(_sourceValues, _selectedSource), OnSourceChanged, "source");
            _sourceDropdown = builder.Last;
            _sourceDropdown.SetFixedWidth(190f);
            builder.AddDropdown(BuildModifiedOptions(), _modifiedFilter, OnModifiedChanged, "modified");
            _modifiedDropdown = builder.Last;
            _modifiedDropdown.SetFixedWidth(128f);
            builder.AddInput(UiText.Get("editor.filter.search_placeholder"), _searchQuery, null, "search", OnSearchTyping);
            _searchInput = builder.Last;
            _searchInput.SetFlexible(1f, 0f);
            TextInputContextMenu.Attach(_searchInput, "search");
            builder.AddButton("×", ClearSearch, "clear_search");
            _clearSearchButton = builder.Last;
            _clearSearchButton.SetTooltip(UiText.Get("search.clear.tooltip"));
            InstallSearchClearButton();
            builder.End();

            builder.BeginRow(8f, TextAnchor.UpperLeft);
            builder.BeginPanel(resultsPanelWidth, -1f, TextAnchor.UpperLeft, 8f, 4f, "results_panel");
            builder.AddHeader(UiText.Get("editor.results.heading"));
            builder.BeginRow(6f, TextAnchor.MiddleLeft);
            builder.AddDropdown(BuildCategoryOptions(), 0, OnCategoryChanged, "category");
            _categoryDropdown = builder.Last;
            _categoryDropdown.SetFlexible(1f, 0f);
            builder.AddLabel($"<link=\"{CategoryHelpLinkId}\"><sprite name=\"info\" tint=1></link>", "category_help");
            builder.Last.SetFixedWidth(24f);
            BuildPageSizeControl(builder);
            builder.End();
            builder.BeginScrollFlexible(0f);
            _resultRowsHost = builder.CurrentContainer;
            builder.End();
            _resultRowsScrollRect = _resultRowsHost.GetComponentInParent<UnityEngine.UI.ScrollRect>();
            builder.BeginRow(6f, TextAnchor.MiddleCenter);
            builder.AddButton(UiText.Get("editor.action.previous_page"), PreviousPage, "previous_page");
            _previousPageButton = builder.Last;
            builder.AddLabel(UiText.Format("editor.page.label", 1, 1, 0));
            _pageLabel = builder.Last;
            builder.AddButton(UiText.Get("editor.action.next_page"), NextPage, "next_page");
            _nextPageButton = builder.Last;
            builder.End();
            builder.End();

            builder.BeginPanel(-1f, -1f, TextAnchor.UpperLeft, 8f, 4f, "detail_panel");
            builder.BeginScrollFlexible(0f);
            builder.BeginRow(8f, TextAnchor.MiddleLeft);
            _entryTitle = AddDetailLabel(builder, "");
            _entryTitle.SetFlexible(1f, 0f);
            builder.AddButton(UiText.Get("editor.action.show_details"), OpenEntryDetailsWindow, "entry_details");
            _entryDetailsButton = builder.Last;
            builder.End();
            _entrySource = AddDetailLabel(builder, "");
            _entryCategory = AddDetailLabel(builder, "");
            builder.AddLabel(UiText.Get("editor.detail.original_label"));
            _originalInput = AddReadOnlyInput(builder, UiText.Get("editor.detail.original_placeholder"), editorInputWidth, 116f, "original_text");
            builder.AddLabel(UiText.Get("editor.detail.replacement_label"));
            builder.AddInput(UiText.Get("editor.detail.replacement_placeholder"), string.Empty, UpdateReplacementDraft, "replacement", UpdateReplacementDraft);
            _replacementInput = builder.Last;
            ConfigureMultiline(_replacementInput, editorInputWidth, 164f, false);
            _draftLabel = AddDetailLabel(builder, "");
            builder.End();
            builder.BeginRow(8f, TextAnchor.MiddleLeft);
            builder.AddButton(UiText.Get("editor.action.stage_entry"), StageCurrentDraft, "stage_entry");
            _stageButton = builder.Last;
            builder.AddButton(UiText.Get("editor.action.restore_original"), RestoreCurrentEntry, "restore_entry");
            _restoreButton = builder.Last;
            builder.End();
            builder.End();
            builder.End();

            builder.BeginRow(8f, TextAnchor.MiddleLeft);
            _statusLabel = AddDetailLabel(builder, "");
            if (_statusLabel.text != null)
            {
                _statusLabel.text.richText = false;
            }
            _statusLabel.SetFlexible(3f, 0f);
            _dirtyLabel = AddDetailLabel(builder, "");
            _dirtyLabel.SetFlexible(1f, 0f);
            builder.End();

            builder.BeginRow(8f, TextAnchor.MiddleRight);
            builder.AddButton(UiText.Get("editor.action.coverage"), OpenCoverageWindow, "coverage_summary");
            _openCoverageButton = builder.Last;
            _openCoverageButton.SetTooltip(UiText.Get("editor.action.coverage_tooltip"));
            builder.AddButton(UiText.Get("editor.action.import"), OpenImportWindow, "open_import");
            _openImportButton = builder.Last;
            builder.AddButton(UiText.Get("editor.action.export"), OpenExportWindow, "open_export");
            _openExportButton = builder.Last;
            builder.AddButton(UiText.Get("editor.action.save_apply"), SaveAndApply, "save_apply");
            _saveButton = builder.Last;
            builder.AddButton(UiText.Get("editor.action.close"), RequestClose, "close_editor");
            _closeButton = builder.Last;
            builder.End();

            _window = builder.Show();
            NativeFontSupport.Apply(_window);
            if (_originalInput?.input?.textComponent != null && _replacementInput?.input?.textComponent != null)
            {
                _diffHighlighter?.Dispose();
                _diffHighlighter = TextDiffHighlighter.Attach(_originalInput.input, _replacementInput.input);
            }

        }

        private string GetCategoryHelpTooltip()
        {
            return CatalogPresentation.CategoryDescription(_selectedCategory);
        }

        private void OpenCoverageWindow()
        {
            OpenInformationWindow(UiText.Get("editor.coverage.title"), ValueOrFallback(_controller.CoverageSummary, UiText.Get("editor.coverage.not_ready")),
                UiText.Get("editor.coverage.warning"));
        }


        private void OpenEntryDetailsWindow()
        {
            if (_selectedEntry == null)
            {
                return;
            }

            OpenInformationWindow(UiText.Get("editor.details.title"), BuildEntryDetails(_selectedEntry), null);
        }

        private static string BuildEntryDetails(TextEntry entry)
        {
            StringBuilder details = new StringBuilder(512);
            details.Append(UiText.Format("editor.details.source",
                    ValueOrFallback(CatalogPresentation.SourceName(entry), UiText.Get("editor.detail.unknown_source"))))
                .Append('\n').Append(UiText.Format("editor.details.source_version",
                    ValueOrFallback(entry.SourceVersion, UiText.Get("editor.details.unknown"))))
                .Append('\n').Append(UiText.Format("editor.details.category", CatalogPresentation.CategoryLabel(entry.Category)))
                .Append('\n').Append(UiText.Format("editor.details.table",
                    ValueOrFallback(entry.TableName, UiText.Get("editor.details.unknown"))))
                .Append('\n').Append(UiText.Format("editor.details.key",
                    ValueOrFallback(entry.KeyName, UiText.Get("editor.details.unknown"))))
                .Append('\n').Append(UiText.Get("editor.details.raw_identifier")).Append(FormatTextId(entry.Id))
                .Append('\n').Append(UiText.Get("editor.details.language_value"))
                .Append(entry.IsFallback ? UiText.Get("editor.details.language_fallback") : UiText.Get("editor.details.language_current"))
                .Append('\n').Append(UiText.Get("editor.details.formatter"))
                .Append(entry.IsSmart ? UiText.Get("editor.details.formatter_smart") : UiText.Get("editor.details.formatter_plain"));
            string description = CatalogPresentation.EntryDescription(entry);
            if (!string.IsNullOrWhiteSpace(description))
                details.Append('\n').Append(UiText.Get("editor.details.description")).Append(description);

            return details.ToString();
        }


        private void OpenInformationWindow(string title, string text, string warning)
        {
            if (_coverageWindow != null && _coverageWindow.IsAlive) _coverageWindow.Close();
            CustomUIBuilder builder = CreateNativeSettingsWindow(CoverageWindowId, title)
                .UseMetaPrefabs()
                .Center()
                .SetCloseOnEscape(true)
                .OnClose(() => _coverageWindow = null);
            if (!string.IsNullOrEmpty(warning)) builder.AddWarning(warning);
            builder.AddInput(title, text, null, "information_details");
            ConfigureMultiline(builder.Last, 630f, 290f, true);
            builder.AddButton(UiText.Get("editor.action.close_information"), () => _coverageWindow?.Close(), "close_information");
            _coverageWindow = builder.Show();
            NativeFontSupport.Apply(_coverageWindow);
        }

        private static CustomUIElement AddDetailLabel(CustomUIBuilder builder, string text)
        {
            builder.AddLabel(text);
            return builder.Last;
        }

        private static CustomUIElement AddReadOnlyInput(CustomUIBuilder builder, string placeholder, float width, float height, string id)
        {
            builder.AddInput(placeholder, string.Empty, null, id);
            CustomUIElement element = builder.Last;
            ConfigureMultiline(element, width, height, true);
            return element;
        }

        private static void ConfigureMultiline(CustomUIElement element, float width, float height, bool readOnly)
        {
            if (element == null || element.input == null)
            {
                return;
            }

            element.input.lineType = TMP_InputField.LineType.MultiLineNewline;
            element.input.richText = false;
            element.input.readOnly = readOnly;
            if (element.input.textComponent != null)
            {
                element.input.textComponent.alignment = TextAlignmentOptions.TopLeft;
                element.input.textComponent.enableWordWrapping = true;
                element.input.textComponent.overflowMode = TextOverflowModes.Masking;
            }

            element.SetPreferredSize(width, height);
            ResetInputToStart(element);
            TextInputContextMenu.Attach(element, element.input, readOnly);
        }

        private static void ResetInputToStart(CustomUIElement element)
        {
            if (element == null || element.input == null)
            {
                return;
            }

            element.input.caretPosition = 0;
            element.input.selectionAnchorPosition = 0;
            element.input.selectionFocusPosition = 0;
        }

        private static void SetReadOnlyText(CustomUIElement element, string value)
        {
            if (element == null)
            {
                return;
            }

            element.SetString(value ?? string.Empty, false);
            ResetInputToStart(element);
            NativeFontSupport.Apply(element);
            TextInputContextMenu.ResetHistory(element, element.input);
        }

        private void OnMainWindowClosed()
        {
            TMPIconHover.CustomLinks.Remove(CategoryHelpLinkId);
            // The controller owns an ongoing save; a reopened view must not stay locked.
            _saveApplyPending = false;
            _saveApplyCallActive = false;
            _diffHighlighter?.Dispose();
            _diffHighlighter = null;
            ClearMainElementReferences();
            Unsubscribe();
            CloseAuxiliaryWindows();

            if (_pendingEntry != null)
            {
                return;
            }

            TryCommitCurrentDraft(null, UiText.Get("editor.transition.close_editor"));
        }

        private void CloseAuxiliaryWindows()
        {
            TextInputContextMenu.CloseAll();
            ClosePresetRenameWindow();
            ClosePresetDeletionWindow();
            if (_importWindow != null && _importWindow.IsAlive) _importWindow.Close();
            if (_exportWindow != null && _exportWindow.IsAlive) _exportWindow.Close();
            if (_coverageWindow != null && _coverageWindow.IsAlive) _coverageWindow.Close();
            if (_errorWindow != null && _errorWindow.IsAlive) _errorWindow.Close();
        }

        private void ClearMainElementReferences()
        {
            _resultRowsHost = null;
            _resultRowsScrollRect = null;
            _rowEntries.Clear();
            _rowElements.Clear();
            _rowSelections.Clear();
            _rowBaseFontSizes.Clear();
            _emptyResultsLabel = null;
            _localeDropdown = null;
            _categoryDropdown = null;
            _sourceDropdown = null;
            _modifiedDropdown = null;
            _searchInput = null;
            _clearSearchButton = null;
            _pageLabel = null;
            _previousPageButton = null;
            _nextPageButton = null;
            _entryTitle = null;
            _entrySource = null;
            _entryCategory = null;
            _entryDetailsButton = null;
            _originalInput = null;
            _replacementInput = null;
            _draftLabel = null;
            _stageButton = null;
            _restoreButton = null;
            _statusLabel = null;
            _dirtyLabel = null;
            _saveButton = null;
            _openImportButton = null;
            _openExportButton = null;
            _openCoverageButton = null;
            _closeButton = null;
            ClearPresetControlReferences();
        }

        private void Subscribe()
        {
            if (_subscribed)
            {
                return;
            }

            _controller.Changed += OnControllerChanged;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed)
            {
                return;
            }

            _controller.Changed -= OnControllerChanged;
            _subscribed = false;
        }

        private void OnControllerChanged()
        {
            RefreshPresetControls();
            RefreshMainStatus();
            bool wasLoading = _wasLoading;
            bool loading = _controller.IsLoading;
            bool finishedLoading = wasLoading && !loading;
            if (wasLoading != loading)
            {
                _wasLoading = loading;
                SetMainControlsInteractable(!loading && !_saveApplyPending);
                UpdateImportWindow();
            }

            if (loading)
            {
                return;
            }

            if (_saveApplyPending)
            {
                if (!_saveApplyCallActive)
                {
                    CompletePendingSaveAndApply();
                }

                return;
            }

            if (finishedLoading)
            {
                bool preserveDraft = !_awaitingLocale && IsDisplayContextCurrent() && _selectedEntry != null;
                RefreshLocaleOptions();
                RefreshFilterOptions();
                if (_awaitingLocale)
                {
                    _awaitingLocale = false;
                    _selectedEntry = FindEntryByIdentity(
                        _preservedSourceId,
                        _preservedTableId,
                        _preservedKey,
                        _controller.SelectedLocale);
                    ClearPreservedSelection();
                }

                if (_selectedEntry == null)
                {
                    SelectInitialEntry();
                }
                else if (!preserveDraft)
                {
                    _replacementDraft = NullFallback(_controller.State.GetText(_selectedEntry), string.Empty);
                }

                if (!preserveDraft) RefreshDetail();
            }
            else if (!IsDisplayContextCurrent())
            {
                if (!string.Equals(_displayedLocale, _controller.SelectedLocale, StringComparison.Ordinal))
                {
                    RefreshLocaleOptions();
                }

                RebindSelectedEntryToCurrentState();
                RefreshFilterOptions();
                RefreshDetail();
            }

            RefreshRows();
        }

        private void SetMainControlsInteractable(bool interactable)
        {
            bool enabled = interactable && !_saveApplyPending;
            bool textInputsInteractable = (interactable || !_awaitingLocale) && !_saveApplyPending;
            _localeDropdown?.SetInteractable(enabled && _localeCodes.Count > 1);
            _categoryDropdown?.SetInteractable(enabled);
            _sourceDropdown?.SetInteractable(enabled);
            _modifiedDropdown?.SetInteractable(enabled);
            _searchInput?.SetInteractable(textInputsInteractable);
            _replacementInput?.SetInteractable(textInputsInteractable && _pendingEntry == null);
            _stageButton?.SetInteractable(enabled && _pendingEntry == null && HasDraftChanges());
            _restoreButton?.SetInteractable(enabled && _pendingEntry == null && CanRestoreCurrentEntry());
            _saveButton?.SetInteractable(enabled && _pendingEntry == null && CanSave());
            _closeButton?.SetInteractable(enabled);
            _openImportButton?.SetInteractable(enabled && _pendingEntry == null);
            _openExportButton?.SetInteractable(enabled && _pendingEntry == null);
            _openCoverageButton?.SetInteractable(enabled && _pendingEntry == null);
            _previousPageButton?.SetInteractable(enabled && _page > 0);
            _nextPageButton?.SetInteractable(enabled && HasNextPage());
            for (int i = 0; i < _rowElements.Count; i++)
            {
                _rowElements[i].SetInteractable(enabled);
            }
        }

        private void RefreshLocaleOptions()
        {
            RefreshLocaleCache();
            if (_localeDropdown == null)
            {
                return;
            }

            _localeDropdown.SetOptions(BuildLocaleCaptions());
            _localeDropdown.SetIndex(GetSelectedLocaleIndex(), false);
            NativeFontSupport.Apply(_localeDropdown);
            _localeDropdown.SetInteractable(!_controller.IsLoading && _localeCodes.Count > 1);
        }

        private List<string> BuildLocaleOptions()
        {
            RefreshLocaleCache();
            return BuildLocaleCaptions();
        }

        private List<string> BuildLocaleCaptions()
        {
            return new List<string>(_localeNames);
        }



        private void RefreshLocaleCache()
        {
            _localeCodes = CopyStrings(_controller.LocaleCodes);
            _localeNames = CopyStrings(_controller.LocaleNames);
            if (_localeCodes.Count == 0 || _localeNames.Count == 0)
            {
                _localeCodes.Clear();
                _localeNames.Clear();
                _localeCodes.Add(string.Empty);
                _localeNames.Add(UiText.Get("editor.locale.not_loaded"));
            }

            int count = Math.Min(_localeCodes.Count, _localeNames.Count);
            if (_localeCodes.Count > count) _localeCodes.RemoveRange(count, _localeCodes.Count - count);
            if (_localeNames.Count > count) _localeNames.RemoveRange(count, _localeNames.Count - count);
        }

        private static List<string> CopyStrings(IReadOnlyList<string> values)
        {
            List<string> copy = new List<string>();
            if (values == null)
            {
                return copy;
            }

            for (int i = 0; i < values.Count; i++)
            {
                copy.Add(values[i] ?? string.Empty);
            }

            return copy;
        }

        private int GetSelectedLocaleIndex()
        {
            if (_localeCodes.Count == 0)
            {
                return 0;
            }

            string selected = _controller.SelectedLocale;
            for (int i = 0; i < _localeCodes.Count; i++)
            {
                if (string.Equals(_localeCodes[i], selected, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return 0;
        }

        private void OnLocaleChanged(int index)
        {
            if (_controller.IsLoading || _saveApplyPending || index < 0 || index >= _localeCodes.Count)
            {
                UpdateLocaleDropdownSelection();
                return;
            }

            string localeCode = _localeCodes[index];
            if (string.IsNullOrEmpty(localeCode) || string.Equals(localeCode, _controller.SelectedLocale, StringComparison.Ordinal))
            {
                return;
            }

            UpdateLocaleDropdownSelection();
            TryCommitCurrentDraft(() =>
            {
                CaptureSelectedEntryKey();
                _awaitingLocale = true;
                try
                {
                    _controller.LoadLocale(localeCode);
                }
                catch (Exception exception)
                {
                    _awaitingLocale = false;
                    ClearPreservedSelection();
                    ShowError(UiText.Get("editor.error.locale_title"), UiText.Format("editor.error.locale_body", exception.Message));
                }
            }, UiText.Get("editor.transition.change_language"));
        }

        private void UpdateLocaleDropdownSelection()
        {
            if (_localeDropdown != null)
            {
                _localeDropdown.SetIndex(GetSelectedLocaleIndex(), false);
        }
        }


        private List<string> BuildCategoryOptions()
        {
            BuildCategoryValues();
            return _categoryLabels;
        }

        private List<string> BuildSourceOptions()
        {
            BuildSourceValues();
            return _sourceLabels;
        }

        private void RefreshFilterOptions()
        {
            BuildCategoryValues();
            BuildSourceValues();
            if (_categoryDropdown != null)
            {
                _categoryDropdown.SetOptions(_categoryLabels);
                _categoryDropdown.SetIndex(FindValueIndex(_categoryValues, _selectedCategory), false);
            }

            if (_sourceDropdown != null)
            {
                _sourceDropdown.SetOptions(_sourceLabels);
                _sourceDropdown.SetIndex(FindValueIndex(_sourceValues, _selectedSource), false);
            }
        }


        private void BuildCategoryValues()
        {
            HashSet<string> categories = new HashSet<string>(StringComparer.Ordinal);
            IReadOnlyList<TextEntry> entries = _controller.State.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                TextEntry entry = entries[i];
                if (entry != null)
                {
                    categories.Add(entry.Category ?? string.Empty);
                }
            }

            List<string> sorted = new List<string>(categories);
            sorted.Sort(StringComparer.OrdinalIgnoreCase);
            _categoryValues = new List<string>(sorted.Count + 1) { null };
            _categoryLabels = new List<string>(sorted.Count + 1) { UiText.Get("category.all.label") };
            for (int i = 0; i < sorted.Count; i++)
            {
                _categoryValues.Add(sorted[i]);
                _categoryLabels.Add(string.IsNullOrEmpty(sorted[i]) ? UiText.Get("editor.detail.uncategorized") : CatalogPresentation.CategoryLabel(sorted[i]));
            }

            if (_selectedCategory != null && !categories.Contains(_selectedCategory))
            {
                _selectedCategory = null;
            }
        }

        private void BuildSourceValues()
        {
            HashSet<string> sourceIds = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<string, string> sourceNames = new Dictionary<string, string>(StringComparer.Ordinal);
            IReadOnlyList<TextEntry> entries = _controller.State.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                TextEntry entry = entries[i];
                if (entry == null) continue;

                string sourceId = entry.Id.SourceId ?? string.Empty;
                sourceIds.Add(sourceId);
                if (!sourceNames.ContainsKey(sourceId))
                    sourceNames.Add(sourceId, CatalogPresentation.SourceName(entry) ?? string.Empty);
            }

            _selectedSource = SourceFilterChoices.ResolveSelection(
                sourceIds, _selectedSource, _allSourcesExplicitlySelected, GameTextCatalog.GameSourceId);
            _sourceValues = SourceFilterChoices.BuildValues(sourceIds, GameTextCatalog.GameSourceId);
            _sourceLabels = new List<string>(_sourceValues.Count);
            foreach (string sourceId in _sourceValues)
            {
                if (sourceId == null)
                {
                    _sourceLabels.Add(UiText.Get("editor.filter.source.all"));
                    continue;
                }

                string sourceName = sourceNames[sourceId];
                _sourceLabels.Add(string.IsNullOrWhiteSpace(sourceName) || string.Equals(sourceName, sourceId, StringComparison.Ordinal)
                    ? UiText.Get("editor.detail.unknown_source")
                    : sourceName);
            }
        }

        private static int FindValueIndex(List<string> values, string value)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (string.Equals(values[i], value, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return 0;
        }

        private void OnCategoryChanged(int index)
        {
            if (index < 0 || index >= _categoryValues.Count)
            {
                return;
            }

            _selectedCategory = _categoryValues[index];
            _page = 0;
            RefreshRows();
        }

        private void OnSourceChanged(int index)
        {
            if (index < 0 || index >= _sourceValues.Count)
            {
                return;
            }

            _selectedSource = _sourceValues[index];
            _allSourcesExplicitlySelected = _selectedSource == null;
            _page = 0;
            RefreshRows();
        }

        private void OnModifiedChanged(int index)
        {
            if (index < 0 || index >= 3)
            {
                return;
            }

            _modifiedFilter = index;
            _page = 0;
            RefreshRows();
        }

        private void OnSearchTyping(string value)
        {
            RefreshSearchClearButton();
            _searchQuery = value ?? string.Empty;
            _page = 0;
            RefreshRows();
        }
        private void InstallSearchClearButton()
        {
            if (_searchInput == null || _searchInput.input == null || _clearSearchButton == null)
                return;

            RectTransform buttonRect = _clearSearchButton.Rect;
            if (buttonRect == null)
                return;

            RectTransform inputPlate = _searchInput.input.targetGraphic != null
                ? _searchInput.input.targetGraphic.rectTransform : _searchInput.Rect;
            buttonRect.SetParent(inputPlate, false);
            buttonRect.anchorMin = new Vector2(1f, 0f);
            buttonRect.anchorMax = Vector2.one;
            buttonRect.pivot = new Vector2(1f, 0.5f);
            buttonRect.sizeDelta = new Vector2(SearchClearButtonWidth, -SearchClearButtonInset * 2f);
            buttonRect.anchoredPosition = new Vector2(-SearchClearButtonInset, 0f);
            buttonRect.SetAsLastSibling();

            UnityEngine.UI.LayoutElement layout = buttonRect.GetComponent<UnityEngine.UI.LayoutElement>();
            if (layout != null)
                layout.ignoreLayout = true;

            RectTransform textViewport = _searchInput.input.textViewport;
            if (textViewport != null)
                textViewport.offsetMax = new Vector2(textViewport.offsetMax.x - SearchClearTextInset, textViewport.offsetMax.y);

            BindVisibleMetaText(_clearSearchButton);
            _clearSearchButton.SetText("×");
            if (_clearSearchButton.text != null)
            {
                _clearSearchButton.text.alignment = TextAlignmentOptions.Center;
                _clearSearchButton.text.margin = Vector4.zero;
                RectTransform labelRect = _clearSearchButton.text.rectTransform;
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = Vector2.zero;
                labelRect.offsetMax = Vector2.zero;
            }
            NativeFontSupport.Apply(_clearSearchButton);
            RefreshSearchClearButton();
        }

        private void RefreshSearchClearButton()
        {
            if (_clearSearchButton == null || _clearSearchButton.root == null)
                return;

            bool hasQuery = _searchInput != null && _searchInput.input != null && !string.IsNullOrEmpty(_searchInput.input.text);
            _clearSearchButton.SetInteractable(hasQuery);
            _clearSearchButton.root.SetActive(hasQuery);
        }

        private void ClearSearch()
        {
            TMP_InputField input = _searchInput != null ? _searchInput.input : null;
            if (input == null || string.IsNullOrEmpty(input.text))
                return;

            using (TextInputContextMenu.BeginAtomicEdit(_searchInput))
            {
                input.text = string.Empty;
            }
            input.Select();
            input.ActivateInputField();
        }


        private void SelectInitialEntry()
        {
            if (_selectedEntry != null && string.Equals(_selectedEntry.Id.Locale, _controller.SelectedLocale, StringComparison.Ordinal))
            {
                return;
            }

            _selectedEntry = null;
            IReadOnlyList<TextEntry> entries = _controller.State.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i] != null)
                {
                    _selectedEntry = entries[i];
                    break;
                }
            }

            _replacementDraft = _selectedEntry == null
                ? string.Empty
                : NullFallback(_controller.State.GetText(_selectedEntry), string.Empty);
        }
        private void CaptureSelectedEntryKey()
        {
            if (_selectedEntry == null)
            {
                ClearPreservedSelection();
                return;
            }

            _preservedSourceId = _selectedEntry.Id.SourceId;
            _preservedTableId = _selectedEntry.Id.TableId;
            _preservedKey = _selectedEntry.Id.Key;
        }

        private void ClearPreservedSelection()
        {
            _preservedSourceId = null;
            _preservedTableId = null;
            _preservedKey = null;
        }

        private TextEntry FindEntryByIdentity(string sourceId, string tableId, string key, string locale)
        {
            if (sourceId == null && tableId == null && key == null)
            {
                return null;
            }

            IReadOnlyList<TextEntry> entries = _controller.State.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                TextEntry entry = entries[i];
                if (entry != null
                    && string.Equals(entry.Id.SourceId, sourceId, StringComparison.Ordinal)
                    && string.Equals(entry.Id.TableId, tableId, StringComparison.Ordinal)
                    && string.Equals(entry.Id.Key, key, StringComparison.Ordinal)
                    && string.Equals(entry.Id.Locale, locale, StringComparison.Ordinal))
                {
                    return entry;
                }
            }

            return null;
        }

        private void RebindSelectedEntryToCurrentState()
        {
            TextEntry previous = _selectedEntry;
            _selectedEntry = previous == null
                ? null
                : FindEntryByIdentity(previous.Id.SourceId, previous.Id.TableId, previous.Id.Key, _controller.SelectedLocale);
            if (_selectedEntry == null)
            {
                SelectInitialEntry();
            }
            else
            {
                _replacementDraft = NullFallback(_controller.State.GetText(_selectedEntry), string.Empty);
            }

            ClearPreservedSelection();
        }
        private bool IsDisplayContextCurrent()
        {
            return _hasDisplayedContext
                && _displayedPresetId == _controller.SelectedPresetId
                && string.Equals(_displayedLocale, _controller.SelectedLocale, StringComparison.Ordinal);
        }

        private void RememberDisplayContext()
        {
            _displayedPresetId = _controller.SelectedPresetId;
            _displayedLocale = _controller.SelectedLocale;
            _hasDisplayedContext = true;
        }

        private void RefreshDiff()
        {
            if (_diffHighlighter == null)
            {
                return;
            }

            string original = _selectedEntry == null
                ? string.Empty
                : NullFallback(_selectedEntry.OriginalText, string.Empty);
            _diffHighlighter.SetTexts(original, _replacementDraft ?? string.Empty);
        }


        private void RefreshRows()
        {
            if (_resultRowsHost == null || _window == null || !_window.IsAlive)
            {
                RefreshMainStatus();
                return;
            }

            int filteredCount = CountFilteredEntries();
            int pageCount = Math.Max(1, (filteredCount + _pageSize - 1) / _pageSize);
            if (_page >= pageCount)
            {
                _page = pageCount - 1;
            }

            int firstIndex = _page * _pageSize;
            int lastIndex = Math.Min(firstIndex + _pageSize, filteredCount);
            int visibleRowCount = 0;
            CustomUIBuilder builder = null;

            if (filteredCount == 0)
            {
                if (_emptyResultsLabel == null)
                {
                    builder = new CustomUIBuilder(CustomUIManager.Instance, _window, _resultRowsHost);
                    builder.UseMetaPrefabs();
                    builder.AddLabel(UiText.Get("search.no_results"));
                    _emptyResultsLabel = builder.Last;
                    BindVisibleMetaText(_emptyResultsLabel);
                    _emptyResultsLabel.SetText(UiText.Get("search.no_results"));
                    _emptyResultsLabel.SetPreferredSize(_resultRowWidth, ResultRowHeight);
                    if (_emptyResultsLabel.text != null)
                    {
                        _emptyResultsLabel.text.alignment = TextAlignmentOptions.MidlineLeft;
                        _emptyResultsLabel.text.enableWordWrapping = false;
                    }
                    NativeFontSupport.Apply(_emptyResultsLabel);
                }

                _emptyResultsLabel.root.SetActive(true);
            }
            else
            {
                if (_emptyResultsLabel != null)
                {
                    _emptyResultsLabel.root.SetActive(false);
                }

                int filteredIndex = 0;
                IReadOnlyList<TextEntry> entries = _controller.State.Entries;
                for (int i = 0; i < entries.Count && filteredIndex < lastIndex; i++)
                {
                    TextEntry entry = entries[i];
                    if (entry == null || !MatchesFilters(entry))
                    {
                        continue;
                    }

                    if (filteredIndex >= firstIndex)
                    {
                        CustomUIElement row;
                        if (visibleRowCount == _rowElements.Count)
                        {
                            if (builder == null)
                            {
                                builder = new CustomUIBuilder(CustomUIManager.Instance, _window, _resultRowsHost);
                                builder.UseMetaPrefabs();
                            }

                            int rowBindingIndex = visibleRowCount;
                            builder.AddButton(string.Empty, () => RequestSelection(_rowEntries[rowBindingIndex]), "result_row_" + rowBindingIndex);
                            row = builder.Last;
                            BindVisibleMetaText(row);
                            row.SetPreferredSize(_resultRowWidth, ResultRowHeight);
                            if (row.text != null)
                            {
                                row.text.alignment = TextAlignmentOptions.MidlineLeft;
                                row.text.enableWordWrapping = false;
                                row.text.overflowMode = TextOverflowModes.Ellipsis;
                            }

                            _rowEntries.Add(null);
                            _rowElements.Add(row);
                            _rowSelections.Add(new ResultRowSelection(row.root));
                            _rowBaseFontSizes.Add(row.text != null ? row.text.fontSize : 0f);
                        }
                        else
                        {
                            row = _rowElements[visibleRowCount];
                        }

                        _rowEntries[visibleRowCount] = entry;
                        row.root.SetActive(true);
                        SetRowText(row, visibleRowCount, entry);
                        row.SetInteractable(!_controller.IsLoading && !_saveApplyPending);
                        visibleRowCount++;
                    }

                    filteredIndex++;
                }
            }

            for (int i = visibleRowCount; i < _rowElements.Count; i++)
            {
                _rowEntries[i] = null;
                _rowElements[i].root.SetActive(false);
            }

            CustomUIBuilder.RebuildLayout(_resultRowsHost);
            _pageLabel?.SetText(UiText.Format("editor.page.label", _page + 1, pageCount, filteredCount));
            if (filteredCount == 0 && _resultRowsScrollRect != null)
            {
                _resultRowsScrollRect.StopMovement();
                _resultRowsScrollRect.verticalNormalizedPosition = 1f;
            }
            _previousPageButton?.SetInteractable(_page > 0 && !_controller.IsLoading);
            _nextPageButton?.SetInteractable(_page + 1 < pageCount && !_controller.IsLoading);
            RefreshMainStatus();
        }

        private void UpdateVisibleSelectedRow()
        {
            for (int i = 0; i < _rowEntries.Count; i++)
            {
                TextEntry entry = _rowEntries[i];
                if (entry != null && IsSelected(entry))
                {
                    SetRowText(_rowElements[i], i, entry);
                    return;
                }
            }
        }

        private int CountFilteredEntries()
        {
            int count = 0;
            IReadOnlyList<TextEntry> entries = _controller.State.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i] != null && MatchesFilters(entries[i]))
                {
                    count++;
                }
            }

            return count;
        }

        private bool MatchesFilters(TextEntry entry)
        {
            if (_selectedCategory != null && !string.Equals(entry.Category ?? string.Empty, _selectedCategory, StringComparison.Ordinal))
            {
                return false;
            }

            if (_selectedSource != null && !string.Equals(entry.Id.SourceId, _selectedSource, StringComparison.Ordinal))
            {
                return false;
            }

            bool modified = _controller.State.IsModified(entry);
            if (_modifiedFilter == 1 && !modified || _modifiedFilter == 2 && modified)
            {
                return false;
            }

            return MatchesSearch(entry, _searchQuery);
        }

        private bool MatchesSearch(TextEntry entry, string query)
        {
            if (string.IsNullOrEmpty(query))
            {
                return true;
            }

            return ContainsIgnoreCase(entry.Id.SourceId, query)
                || ContainsIgnoreCase(entry.Id.Locale, query)
                || ContainsIgnoreCase(entry.Id.TableId, query)
                || ContainsIgnoreCase(entry.Id.Key, query)
                || ContainsIgnoreCase(CatalogPresentation.SourceName(entry), query)
                || ContainsIgnoreCase(entry.SourceVersion, query)
                || ContainsIgnoreCase(entry.TableName, query)
                || ContainsIgnoreCase(entry.KeyName, query)
                || CatalogPresentation.CategoryContains(entry.Category, query)
                || CatalogPresentation.EntryDescriptionContains(entry, query)
                || ContainsIgnoreCase(entry.OriginalText, query)
                || ContainsIgnoreCase(_controller.State.GetText(entry), query);
        }

        private static bool ContainsIgnoreCase(string text, string query)
        {
            return !string.IsNullOrEmpty(text) && text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string BuildRowText(TextEntry entry)
        {
            string preview = PreviewText(GetDisplayedText(entry), 64);
            StringBuilder text = new StringBuilder(preview.Length + 16);
            if (IsSelected(entry) && HasDraftChanges())
            {
                text.Append("* ");
            }

            text.Append(preview);
            if (_controller.State.IsModified(entry))
            {
                text.Append(UiText.Get("editor.row.modified_mark"));
            }

            return text.ToString();
        }

        private void BindVisibleMetaText(CustomUIElement row)
        {
            _rowTextCandidates.Clear();
            row.root.transform.GetComponentsInChildren(false, _rowTextCandidates);
            TextMeshProUGUI label = null;
            float labelWidth = -1f;
            for (int i = 0; i < _rowTextCandidates.Count; i++)
            {
                TextMeshProUGUI candidate = _rowTextCandidates[i];
                if (candidate == null || !candidate.isActiveAndEnabled)
                {
                    continue;
                }

                float width = candidate.rectTransform.rect.width;
                if (label == null || width > labelWidth)
                {
                    label = candidate;
                    labelWidth = width;
                }
            }

            _rowTextCandidates.Clear();
            if (label != null)
            {
                row.text = label;
            }
        }

        private void SetRowText(CustomUIElement row, int rowIndex, TextEntry entry)
        {
            _rowSelections[rowIndex].SetSelected(IsSelected(entry));
            string text = BuildRowText(entry);
            if (row.text == null || !string.Equals(row.text.text, text, StringComparison.Ordinal))
            {
                row.SetText(text);
                NativeFontSupport.Apply(row);
            }

            if (row.text != null)
            {
                row.text.fontSize = _rowBaseFontSizes[rowIndex] * ResultRowFontScale;
            }
        }

        private static string PreviewText(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text))
            {
                return UiText.Get("editor.row.empty_text");
            }

            StringBuilder preview = new StringBuilder(Math.Min(maxLength + 1, text.Length));
            bool previousWasSpace = false;
            for (int i = 0; i < text.Length && preview.Length <= maxLength; i++)
            {
                char value = text[i];
                if (value == '\r' || value == '\n' || value == '\t')
                {
                    value = ' ';
                }

                if (value == ' ' && previousWasSpace)
                {
                    continue;
                }

                preview.Append(value);
                previousWasSpace = value == ' ';
            }

            if (text.Length > preview.Length)
            {
                if (preview.Length > maxLength)
                {
                    preview.Length = maxLength;
                }

                preview.Append('…');
            }

            return preview.ToString();
        }

        private static string FormatTextId(TextId id)
        {
            return "SourceId=" + NullFallback(id.SourceId, string.Empty)
                + " | Locale=" + NullFallback(id.Locale, string.Empty)
                + " | TableId=" + NullFallback(id.TableId, string.Empty)
                + " | Key=" + NullFallback(id.Key, string.Empty);
        }

        private bool IsSelected(TextEntry entry)
        {
            return _selectedEntry != null && _selectedEntry.Id.Equals(entry.Id);
        }

        private void PreviousPage()
        {
            if (_page <= 0 || _controller.IsLoading)
            {
                return;
            }

            _page--;
            RefreshRows();
        }

        private void NextPage()
        {
            if (_controller.IsLoading || !HasNextPage())
            {
                return;
            }

            _page++;
            RefreshRows();
        }

        private bool HasNextPage()
        {
            int filteredCount = CountFilteredEntries();
            int pageCount = Math.Max(1, (filteredCount + _pageSize - 1) / _pageSize);
            return _page + 1 < pageCount;
        }

        private void RequestSelection(TextEntry entry)
        {
            if (entry == null || _controller.IsLoading || _saveApplyPending || IsSelected(entry))
            {
                return;
            }

            TryCommitCurrentDraft(() => SelectEntry(entry), UiText.Get("editor.transition.change_text"));
        }

        private void SelectEntry(TextEntry entry)
        {
            _selectedEntry = entry;
            _replacementDraft = NullFallback(_controller.State.GetText(entry), string.Empty);
            RefreshDetail();
            RefreshRows();
        }

        private void RefreshDetail()
        {
            RememberDisplayContext();
            if (_selectedEntry == null)
            {
                _entryTitle?.SetText(UiText.Get("editor.detail.no_selection"));
                _entrySource?.SetText(string.Empty);
                _entryCategory?.SetText(string.Empty);
                _entryDetailsButton?.SetInteractable(false);
                SetReadOnlyText(_originalInput, string.Empty);
                SetReplacementText(string.Empty);
                _draftLabel?.SetText(string.Empty);
                _stageButton?.SetInteractable(false);
                _restoreButton?.SetInteractable(false);
                return;
            }

            TextEntry entry = _selectedEntry;
            _entryTitle?.SetText(UiText.Get("editor.detail.entry_title"));
            _entrySource?.SetText(UiText.Format("editor.detail.source_label",
                ValueOrFallback(CatalogPresentation.SourceName(entry), UiText.Get("editor.detail.unknown_source"))));
            _entryCategory?.SetText(UiText.Format("editor.detail.category_label", CatalogPresentation.CategoryLabel(entry.Category)));
            _entryDetailsButton?.SetInteractable(true);
            SetReadOnlyText(_originalInput, NullFallback(entry.OriginalText, string.Empty));
            SetReplacementText(_replacementDraft);
            UpdateDraftLabel();
            UpdateDetailButtons();
        }

        private void SetReplacementText(string text)
        {
            _replacementDraft = text ?? string.Empty;
            _replacementInput?.SetString(_replacementDraft, false);
            ResetInputToStart(_replacementInput);
            if (_replacementInput != null)
            {
                NativeFontSupport.Apply(_replacementInput);
                TextInputContextMenu.ResetHistory(_replacementInput,
                    _selectedEntry == null ? null : (object)(_controller.SelectedPresetId, _selectedEntry.Id));
            }
            RefreshDiff();
        }


        private void UpdateReplacementDraft(string value)
        {
            _replacementDraft = value ?? string.Empty;
            if (_replacementInput != null)
            {
                NativeFontSupport.Apply(_replacementInput);
            }
            RefreshDiff();
            UpdateDraftLabel();
            UpdateDetailButtons();
            UpdateVisibleSelectedRow();
            RefreshMainStatus();
        }

        private void UpdateDraftLabel()
        {
            if (_draftLabel == null || _selectedEntry == null)
            {
                return;
            }

            string message;
            if (HasDraftChanges())
            {
                message = UiText.Get("editor.detail.draft.unstaged");
            }
            else if (_controller.State.IsModified(_selectedEntry))
            {
                message = UiText.Get("editor.detail.draft.staged");
            }
            else
            {
                message = UiText.Get("editor.detail.draft.original");
            }

            _draftLabel.SetText(message);
        }

        private bool HasDraftChanges()
        {
            return _selectedEntry != null
                && !string.Equals(_replacementDraft, _controller.State.GetText(_selectedEntry), StringComparison.Ordinal);
        }

        private string GetDisplayedText(TextEntry entry)
        {
            return IsSelected(entry) ? _replacementDraft : _controller.State.GetText(entry);
        }

        private void UpdateDetailButtons()
        {
            _stageButton?.SetInteractable(!_controller.IsLoading && HasDraftChanges() && _pendingEntry == null);
            _restoreButton?.SetInteractable(!_controller.IsLoading && CanRestoreCurrentEntry() && _pendingEntry == null);
        }

        private bool CanRestoreCurrentEntry()
        {
            return _selectedEntry != null && (_controller.State.IsModified(_selectedEntry) || HasDraftChanges());
        }

        private void StageCurrentDraft()
        {
            TryCommitCurrentDraft(null, UiText.Get("editor.transition.stage_text"));
        }

        private void RestoreCurrentEntry()
        {
            TextEntry entry = _selectedEntry;
            if (entry == null || _controller.IsLoading)
            {
                return;
            }

            TryCommitCurrentDraft(() =>
            {
                try
                {
                    _controller.RestoreEntry(entry);
                    if (IsSelected(entry))
                    {
                        _replacementDraft = NullFallback(_controller.State.GetText(entry), string.Empty);
                        RefreshDetail();
                    }

                    RefreshRows();
                    RefreshMainStatus();
                }
                catch (Exception exception)
                {
                    ShowError(UiText.Get("editor.error.restore.title"), UiText.Format("editor.error.restore.message", exception.Message));
                }
            }, UiText.Get("editor.transition.restore_original"));
        }

        private void SaveAndApply()
        {
            if (_controller.IsLoading || _saveApplyPending)
            {
                return;
            }

            TryCommitCurrentDraft(BeginSaveAndApply, UiText.Get("editor.transition.save_apply"));
        }

        private void BeginSaveAndApply()
        {
            if (!_controller.HasUnsavedChanges && _controller.SelectedPresetId == _controller.ActivePresetId)
            {
                RefreshMainStatus(UiText.Get("editor.status.no_changes_to_save"));
                return;
            }

            _saveApplyPending = true;
            _saveApplyCallActive = true;
            RefreshPresetControls();
            SetMainControlsInteractable(false);
            UpdateImportWindow();
            RefreshMainStatus();

            try
            {
                _controller.SaveAndApply();
                _saveApplyCallActive = false;
                if (_controller.IsLoading)
                {
                    RefreshMainStatus();
                    return;
                }

                CompletePendingSaveAndApply();
            }
            catch (Exception exception)
            {
                _saveApplyCallActive = false;
                _saveApplyPending = false;
                RefreshPresetControls();
                SetMainControlsInteractable(!_controller.IsLoading);
                UpdateImportWindow();
                RefreshMainStatus();
                ShowError(UiText.Get("editor.error.save_apply.title"), UiText.Format("editor.error.save_apply.message", exception.Message));
            }
        }

        private void CompletePendingSaveAndApply()
        {
            if (!_saveApplyPending || _saveApplyCallActive || _controller.IsLoading)
            {
                return;
            }

            _saveApplyPending = false;
            _wasLoading = _controller.IsLoading;
            if (!IsDisplayContextCurrent())
            {
                RefreshLocaleOptions();
                RefreshFilterOptions();
                RebindSelectedEntryToCurrentState();
                RefreshDetail();
            }

            bool failed = _controller.HasUnsavedChanges
                || _controller.SelectedPresetId != _controller.ActivePresetId;
            RefreshPresetControls();
            SetMainControlsInteractable(true);
            UpdateImportWindow();
            RefreshRows();

            if (failed)
            {
                RefreshMainStatus();
                ShowError(UiText.Get("editor.error.save_incomplete.title"), ValueOrFallback(_controller.Status, UiText.Get("editor.error.save_incomplete.fallback")));
                return;
            }

            RefreshMainStatus(UiText.Get("editor.status.save_complete"));
        }

        private void RequestClose()
        {
            TryCommitCurrentDraft(() =>
            {
                if (_window != null && _window.IsAlive)
                {
                    _window.Close();
                }
                else
                {
                    Unsubscribe();
                }
            }, UiText.Get("editor.transition.close_editor"));
        }

        private void OpenImportWindow()
        {
            TryCommitCurrentDraft(BuildImportWindow, UiText.Get("editor.transition.open_import"));
        }

        private void OpenExportWindow()
        {
            TryCommitCurrentDraft(BuildExportWindow, UiText.Get("editor.transition.open_export"));
        }

        private void TryCommitCurrentDraft(Action afterCommit, string transitionName)
        {
            if (_saveApplyPending)
            {
                return;
            }

            if (_pendingEntry != null)
            {
                if (_validationWindow != null && _validationWindow.IsAlive)
                {
                    _validationWindow.BringToFront();
                }

                return;
            }

            if (_selectedEntry == null || !HasDraftChanges())
            {
                RunTransition(afterCommit, transitionName);
                return;
            }

            TextEntry entry = _selectedEntry;
            string draft = _replacementDraft ?? string.Empty;
            IReadOnlyList<string> warnings;
            try
            {
                warnings = _controller.ValidateEdit(entry, draft);
            }
            catch (Exception exception)
            {
                ShowError(UiText.Get("editor.error.check.title"), UiText.Format("editor.error.check.message", exception.Message));
                return;
            }

            if (warnings != null && warnings.Count > 0)
            {
                BeginPendingCommit(entry, draft, afterCommit, transitionName);
                ShowValidationWindow(warnings, transitionName);
                return;
            }

            CommitDraft(entry, draft, afterCommit, transitionName);
        }

        private void BeginPendingCommit(TextEntry entry, string draft, Action afterCommit, string transitionName)
        {
            _pendingEntry = entry;
            _pendingDraft = draft;
            _pendingTransition = afterCommit;
            _pendingTransitionName = transitionName;
            _replacementInput?.SetInteractable(false);
            UpdateDetailButtons();
        }

        private void ShowValidationWindow(IReadOnlyList<string> warnings, string transitionName)
        {
            if (_validationWindow != null && _validationWindow.IsAlive)
            {
                _validationWindow.BringToFront();
                return;
            }

            CustomUIBuilder builder = CreateNativeSettingsWindow(ValidationWindowId, UiText.Get("editor.validation.title"))
                .UseMetaPrefabs()
                .Center()
                .SetCloseOnEscape(true)
                .OnClose(OnValidationWindowClosed);

            builder.AddHeader(UiText.Get("editor.validation.heading"));
            builder.AddWarning(UiText.Get("editor.validation.warning"));
            builder.AddLabel(UiText.Format("editor.validation.action", transitionName));
            builder.AddInput(UiText.Get("editor.validation.details_placeholder"), JoinLines(warnings), null, "validation_details");
            ConfigureMultiline(builder.Last, 590f, 220f, true);
            builder.BeginRow(8f, TextAnchor.MiddleRight);
            builder.AddButton(UiText.Get("editor.validation.return"), ReturnFromValidation, "validation_return");
            builder.AddButton(UiText.Get("editor.validation.confirm"), ApproveValidation, "validation_accept");
            builder.End();
            _validationWindow = builder.Show();
            NativeFontSupport.Apply(_validationWindow);
        }

        private void ApproveValidation()
        {
            if (_validationWindow == null || !_validationWindow.IsAlive)
            {
                return;
            }

            _validationApproved = true;
            _validationWindow.Close();
        }

        private void ReturnFromValidation()
        {
            if (_validationWindow == null || !_validationWindow.IsAlive)
            {
                return;
            }

            _validationApproved = false;
            _validationWindow.Close();
        }

        private void OnValidationWindowClosed()
        {
            _validationWindow = null;
            bool approved = _validationApproved;
            _validationApproved = false;
            if (approved)
            {
                CompletePendingCommit();
                return;
            }

            ClearPendingCommit();
            if (_window != null && _window.IsAlive)
            {
                _window.BringToFront();
            }
            else
            {
                Open();
            }
        }

        private void CompletePendingCommit()
        {
            TextEntry entry = _pendingEntry;
            string draft = _pendingDraft;
            Action transition = _pendingTransition;
            string transitionName = _pendingTransitionName;
            ClearPendingCommit();
            if (entry == null)
            {
                return;
            }

            CommitDraft(entry, draft, transition, transitionName);
        }

        private void ClearPendingCommit()
        {
            _pendingEntry = null;
            _pendingDraft = null;
            _pendingTransition = null;
            _pendingTransitionName = null;
            if (_replacementInput != null)
            {
                _replacementInput.SetInteractable(!_controller.IsLoading);
            }

            UpdateDetailButtons();
        }

        private void CommitDraft(TextEntry entry, string draft, Action afterCommit, string transitionName)
        {
            try
            {
                _controller.State.Stage(entry, draft);
                _controller.NotifyEdited();
                _replacementDraft = draft;
                UpdateDraftLabel();
                UpdateDetailButtons();
                RefreshRows();
                RefreshMainStatus();
                RunTransition(afterCommit, transitionName);
            }
            catch (Exception exception)
            {
                ShowError(UiText.Get("editor.error.stage.title"), UiText.Format("editor.error.stage.message", exception.Message));
            }
        }

        private void RunTransition(Action transition, string transitionName)
        {
            if (transition == null)
            {
                RefreshMainStatus();
                return;
            }

            try
            {
                transition();
            }
            catch (Exception exception)
            {
                ShowError(UiText.Format("editor.error.transition_failed", transitionName), exception.Message);
            }
        }

        private void RefreshMainStatus(string localMessage = null)
        {
            if (_statusLabel == null && _dirtyLabel == null && _saveButton == null)
            {
                return;
            }

            string status = localMessage;
            if (_saveApplyPending)
                status = UiText.Get(_controller.IsLoading ? "editor.status.loading_for_apply" : "editor.status.saving");

            if (string.IsNullOrWhiteSpace(status))
            {
                if (_controller.IsLoading)
                    status = UiText.Get("editor.status.loading_catalog");
                else if (string.IsNullOrWhiteSpace(_controller.Status))
                    status = UiText.Get("editor.status.ready");
                else if (_controller.StatusKind == EditorStatusKind.CatalogReady)
                    status = UiText.Get("editor.status.catalog_ready");
                else if (_controller.StatusKind == EditorStatusKind.PresetSelected)
                    status = UiText.Get("editor.status.target_changed");
                else
                    status = _controller.Status;
            }

            _statusLabel?.SetText(status);
            if (_dirtyLabel != null)
            {
                string state = UiText.Get(_controller.HasUnsavedChanges
                    ? "editor.status.workspace_dirty"
                    : "editor.status.workspace_saved");
                state += UiText.Format("editor.status.override_count", _controller.State.OverrideCount);
                if (_controller.State.UnavailableOverrideCount > 0)
                    state += UiText.Format("editor.status.unavailable_count", _controller.State.UnavailableOverrideCount);

                _dirtyLabel.SetText(state);
            }

            if (_saveButton != null)
                _saveButton.SetInteractable(!_controller.IsLoading && !_saveApplyPending && _pendingEntry == null && CanSave());
        }


        private bool CanSave()
        {
            return !_saveApplyPending
                && (_controller.HasUnsavedChanges
                    || _controller.SelectedPresetId != _controller.ActivePresetId
                    || HasDraftChanges());
        }

        private void ShowError(string title, string message)
        {
            if (_errorWindow != null && _errorWindow.IsAlive)
            {
                _errorWindow.Close();
            }

            CustomUIBuilder builder = CreateNativeSettingsWindow(ErrorWindowId, title)
                .UseMetaPrefabs()
                .Center()
                .SetCloseOnEscape(true)
                .OnClose(() => _errorWindow = null);
            builder.AddWarning(title);
            builder.AddInput(UiText.Get("editor.error.details"), message ?? string.Empty, null, "error_details");
            ConfigureMultiline(builder.Last, 570f, 230f, true);
            builder.BeginRow(8f, TextAnchor.MiddleRight);
            builder.AddButton(UiText.Get("editor.error.reopen"), () =>
            {
                if (_window == null || !_window.IsAlive)
                {
                    Open();
                }
                else
                {
                    _window.BringToFront();
                }

                if (_errorWindow != null && _errorWindow.IsAlive)
                {
                    _errorWindow.Close();
                }
            }, "error_return");
            builder.AddButton(UiText.Get("editor.error.close"), () =>
            {
                if (_errorWindow != null && _errorWindow.IsAlive)
                {
                    _errorWindow.Close();
                }
            }, "error_close");
            builder.End();
            _errorWindow = builder.Show();
            NativeFontSupport.Apply(_errorWindow);
        }

        private static string JoinLines(IReadOnlyList<string> values)
        {
            if (values == null || values.Count == 0)
            {
                return string.Empty;
            }

            StringBuilder joined = new StringBuilder();
            for (int i = 0; i < values.Count; i++)
            {
                if (i > 0)
                {
                    joined.Append('\n');
                }

                joined.Append("• ").Append(values[i] ?? string.Empty);
            }

            return joined.ToString();
        }

        private void BuildImportWindow()
        {
            if (_importWindow != null && _importWindow.IsAlive)
            {
                _importWindow.BringToFront();
                return;
            }

            _overwriteImportConflicts = false;
            _importTargetPresetId = _controller.SelectedPresetId;
            _importPreview = null;
            RefreshImportFileList();
            CustomUIBuilder builder = CreateNativeSettingsWindow(ImportWindowId, UiText.Get("editor.import.title"))
                .UseMetaPrefabs()
                .Center()
                .SetCloseOnEscape(true)
                .OnClose(() =>
                {
                    _importWindow = null;
                    _importFilesDropdown = null;
                    _importFileLabel = null;
                    _importPreviewText = null;
                    _importPreviewButton = null;
                    _importConfirmButton = null;
                    _importTargetDropdown = null;
                    _importPreview = null;
                });

            builder.AddHeader(UiText.Get("editor.import.heading"));
            builder.AddLabel(UiText.Format("editor.import.data_directory", ValueOrFallback(_controller.DataFolder, UiText.Get("editor.details.unknown"))));
            builder.AddLabel(UiText.Get("editor.import.instructions"));
            builder.BeginRow(8f, TextAnchor.MiddleLeft);
            builder.AddDropdown(BuildImportFileOptions(), GetImportFileIndex(), OnImportFileChanged, "import_file");
            _importFilesDropdown = builder.Last;
            _importFilesDropdown.SetFlexible(1f, 0f);
            builder.AddButton(UiText.Get("editor.import.refresh"), RefreshImportFileList, "refresh_import_files");
            builder.AddButton(UiText.Get("editor.import.preview"), PreviewImport, "preview_import");
            _importPreviewButton = builder.Last;
            builder.End();

            builder.BeginRow(8f, TextAnchor.MiddleLeft);
            BuildImportTargetControl(builder);
            builder.End();

            _importFileLabel = AddDetailLabel(builder, "");
            builder.AddLabel(UiText.Get("editor.import.preview_heading"));
            builder.AddInput(UiText.Get("editor.import.preview_placeholder"), string.Empty, null, "import_preview");
            _importPreviewText = builder.Last;
            ConfigureMultiline(_importPreviewText, 710f, 245f, true);

            builder.BeginRow(8f, TextAnchor.MiddleLeft);
            builder.AddLabel(UiText.Get("editor.import.conflict_handling"));
            builder.Last.SetFixedWidth(112f);
            builder.AddDropdown(BuildConflictOptions(), 0, OnImportConflictChanged, "import_conflict");
            builder.Last.SetFixedWidth(210f);
            builder.AddButton(UiText.Get("editor.import.confirm"), ConfirmImport, "confirm_import");
            _importConfirmButton = builder.Last;
            builder.End();
            builder.AddButton(UiText.Get("editor.import.close"), () => _importWindow?.Close(), "close_import");

            _importWindow = builder.Show();
            NativeFontSupport.Apply(_importWindow);
            UpdateImportWindow();
        }

        private void RefreshImportFileList()
        {
            try
            {
                string previousFile = _importFiles.Count == 0 ? null : _importFiles[GetImportFileIndex()];
                IReadOnlyList<string> files = _controller.GetImportFiles();
                _importFiles = new List<string>();
                if (files != null)
                {
                    for (int i = 0; i < files.Count; i++)
                    {
                        if (!string.IsNullOrWhiteSpace(files[i]))
                        {
                            _importFiles.Add(files[i]);
                        }
                    }
                }

                _importFileIndex = previousFile == null ? 0 : _importFiles.IndexOf(previousFile);
                if (_importFileIndex < 0)
                {
                    _importFileIndex = 0;
                }

                _importPreview = null;
                UpdateImportWindow();
            }
            catch (Exception exception)
            {
                ShowError(UiText.Get("editor.error.import_read.title"), UiText.Format("editor.error.import_read.message", exception.Message));
            }
        }

        private List<string> BuildImportFileOptions()
        {
            if (_importFiles.Count == 0)
            {
                return new List<string> { UiText.Get("editor.import.no_files_option") };
            }

            return new List<string>(_importFiles);
        }

        private int GetImportFileIndex()
        {
            if (_importFiles.Count == 0)
            {
                return 0;
            }

            return Math.Max(0, Math.Min(_importFileIndex, _importFiles.Count - 1));
        }

        private void OnImportFileChanged(int index)
        {
            if (index < 0 || index >= _importFiles.Count)
            {
                return;
            }

            _importFileIndex = index;
            _importPreview = null;
            UpdateImportWindow();
        }

        private void OnImportConflictChanged(int index)
        {
            _overwriteImportConflicts = index == 1;
        }

        private void UpdateImportWindow()
        {
            if (_importWindow == null || !_importWindow.IsAlive)
            {
                return;
            }

            _importFilesDropdown?.SetOptions(BuildImportFileOptions());
            _importFilesDropdown?.SetIndex(GetImportFileIndex(), false);
            RefreshImportTargetOptions();
            _importFileLabel?.SetText(_importFiles.Count == 0
                ? UiText.Get("editor.import.no_files")
                : UiText.Format("editor.import.file", _importFiles[GetImportFileIndex()]));
            SetReadOnlyText(_importPreviewText, FormatImportPreview(_importPreview));
            _importFilesDropdown?.SetInteractable(!_controller.IsLoading && !_saveApplyPending);
            _importPreviewButton?.SetInteractable(_importFiles.Count > 0 && !_controller.IsLoading && !_saveApplyPending);
            _importConfirmButton?.SetInteractable(_importPreview != null && !_controller.IsLoading && !_saveApplyPending);
        }
        private string FormatImportPreview(PresetImportPreview preview)
        {
            if (preview == null)
            {
                return UiText.Get("editor.import.preview_prompt");
            }

            StringBuilder summary = new StringBuilder();
            string target = preview.CreatesPreset
                ? UiText.Format("editor.import.new_target", preview.PresetName)
                : GetPresetLabel(preview.TargetPresetId);
            summary.Append(UiText.Format("editor.import.target", target));
            if (preview.CreatesPreset)
            {
                summary.Append(UiText.Get("editor.import.create_after_confirm"));
            }

            summary.Append('\n');
            AppendPresetStatistics(summary, UiText.Get("editor.import.stats.incoming"), preview.Incoming);
            summary.Append('\n');
            AppendPresetStatistics(summary, UiText.Get("editor.import.stats.existing"), preview.Existing);
            summary.Append('\n').Append(UiText.Format("editor.import.conflicts", preview.ConflictCount))
                .Append('\n').Append(UiText.Format("editor.import.unavailable", preview.UnavailableCount))
                .Append(UiText.Get("editor.import.checks"));
            if (preview.Warnings == null || preview.Warnings.Count == 0)
            {
                summary.Append(UiText.Get("editor.import.no_warnings"));
            }
            else
            {
                summary.Append('\n').Append(JoinLines(preview.Warnings));
            }

            return summary.ToString();
        }
        private void PreviewImport()
        {
            if (_importFiles.Count == 0)
            {
                UpdateImportWindow();
                return;
            }

            try
            {
                _importPreview = _controller.PreviewImport(_importFiles[GetImportFileIndex()], _importTargetPresetId);
                UpdateImportWindow();
            }
            catch (Exception exception)
            {
                _importPreview = null;
                UpdateImportWindow();
                ShowError(UiText.Get("editor.error.import_preview.title"), UiText.Format("editor.error.import_preview.message", exception.Message));
            }
        }
        private void ConfirmImport()
        {
            if (_importPreview == null)
            {
                ShowError(UiText.Get("editor.error.import_unpreviewed.title"), UiText.Get("editor.error.import_unpreviewed.message"));
                return;
            }

            bool hadDraft = HasDraftChanges();
            TryCommitCurrentDraft(() =>
            {
                if (hadDraft)
                {
                    _importPreview = null;
                    PreviewImport();
                    RefreshMainStatus(UiText.Get("editor.status.import_preview_updated"));
                    return;
                }

                try
                {
                    _controller.ConfirmImport(_importPreview, _overwriteImportConflicts);
                    _importPreview = null;
                    _importTargetPresetId = _controller.SelectedPresetId;
                    RebindSelectedEntryToCurrentState();
                    RefreshFilterOptions();
                    RefreshDetail();
                    RefreshRows();
                    RefreshPresetControls();
                    UpdateImportWindow();
                    RefreshMainStatus(UiText.Get("editor.status.import_staged"));
                }
                catch (Exception exception)
                {
                    _importPreview = null;
                    UpdateImportWindow();
                    ShowError(UiText.Get("editor.error.import_stage.title"), UiText.Format("editor.error.import_stage.message", exception.Message));
                }
            }, UiText.Get("editor.transition.merge_import"));
        }
        private void BuildExportWindow()
        {
            if (_exportWindow != null && _exportWindow.IsAlive)
            {
                _exportWindow.BringToFront();
                return;
            }

            _exportPresetId = _controller.SelectedPresetId;
            CustomUIBuilder builder = CreateNativeSettingsWindow(ExportWindowId, UiText.Get("editor.export.title"))
                .UseMetaPrefabs()
                .Center()
                .SetCloseOnEscape(true)
                .OnClose(() =>
                {
                    _exportWindow = null;
                    _exportPresetDropdown = null;
                    _exportStatisticsLabel = null;
                    _exportResultInput = null;
                    _exportButton = null;
                });

            builder.AddHeader(UiText.Get("editor.export.heading"));
            BuildExportPresetControls(builder);
            builder.AddLabel(UiText.Get("editor.export.filename_label"));
            builder.AddInput(UiText.Get("editor.export.filename_placeholder"), _exportName, OnExportNameChanged, "export_name", OnExportNameChanged);
            TextInputContextMenu.Attach(builder.Last, "export_name");
            builder.AddLabel(UiText.Get("editor.export.result_label"));
            builder.AddInput(UiText.Get("editor.export.result_placeholder"), string.Empty, null, "export_result");
            _exportResultInput = builder.Last;
            ConfigureMultiline(_exportResultInput, 600f, 76f, true);
            builder.AddLabel(UiText.Get("editor.export.help"));
            builder.BeginRow(8f, TextAnchor.MiddleRight);
            builder.AddButton(UiText.Get("editor.export.json"), ExportPatch, "export_patch");
            _exportButton = builder.Last;
            builder.AddButton(UiText.Get("editor.export.close"), () => _exportWindow?.Close(), "close_export");
            builder.End();
            _exportWindow = builder.Show();
            NativeFontSupport.Apply(_exportWindow);
            RefreshExportPresetOptions();
        }





        private void OnExportNameChanged(string value)
        {
            _exportName = value ?? string.Empty;
            SetReadOnlyText(_exportResultInput, string.Empty);
        }

        private void ExportPatch()
        {
            TryCommitCurrentDraft(ExportSelectedPreset, UiText.Get("editor.transition.export_preset"));
        }
        private void ExportSelectedPreset()
        {
            try
            {
                _controller.Export(_exportName, _exportPresetId);
                UpdateExportStatistics();
                SetReadOnlyText(_exportResultInput, ValueOrFallback(_controller.Status, UiText.Get("editor.status.export_complete")));
                RefreshMainStatus();
            }
            catch (Exception exception)
            {
                ShowError(UiText.Get("editor.error.export.title"), UiText.Format("editor.error.export.message", exception.Message));
            }
        }

        private static string NullFallback(string value, string fallback)
        {
            return value ?? fallback;
        }

        private static string ValueOrFallback(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }
    }
}
