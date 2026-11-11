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

        private static readonly List<string> ModifiedOptions = new List<string>
        {
            "全部文本",
            "仅已修改",
            "仅未修改"
        };

        private static readonly List<string> ConflictOptions = new List<string>
        {
            "冲突时保留本地",
            "冲突时使用导入值"
        };

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
        private int _modifiedFilter;
        private int _page;
        private TextEntry _selectedEntry;
        private TextEntry _pendingEntry;
        private string _pendingDraft;
        private Action _pendingTransition;
        private string _pendingTransitionName;
        private RectTransform _resultRowsHost;
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
                throw new InvalidOperationException("无法获取原生窗口视口尺寸。");
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
            CustomUIBuilder builder = CreateNativeSettingsWindow(WindowId, "文本工作台")
                .UseMetaPrefabs()
                .SetCloseOnEscape(false)
                .OnClose(OnMainWindowClosed);
            CustomUIWindow window = builder.Handle;
            Vector2 viewportSize = GetNativeViewportSize(window);
            RectTransform panel = GetNativeWindowPanel(window);
            if (panel == null)
            {
                throw new InvalidOperationException("无法找到原生窗口面板。");
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
            builder.AddLabel("语言");
            builder.Last.SetFixedWidth(72f);
            builder.AddDropdown(BuildLocaleOptions(), GetSelectedLocaleIndex(), OnLocaleChanged, "locale");
            _localeDropdown = builder.Last;
            _localeDropdown.SetFixedWidth(300f);
            builder.End();

            builder.BeginRow(8f, TextAnchor.MiddleLeft);
            builder.AddLabel("来源");
            builder.Last.SetFixedWidth(72f);
            builder.AddDropdown(BuildSourceOptions(), 0, OnSourceChanged, "source");
            _sourceDropdown = builder.Last;
            _sourceDropdown.SetFixedWidth(190f);
            builder.AddDropdown(ModifiedOptions, _modifiedFilter, OnModifiedChanged, "modified");
            _modifiedDropdown = builder.Last;
            _modifiedDropdown.SetFixedWidth(128f);
            builder.AddInput("搜索文本或键…", _searchQuery, null, "search", OnSearchTyping);
            _searchInput = builder.Last;
            _searchInput.SetFlexible(1f, 0f);
            TextInputContextMenu.Attach(_searchInput, "search");
            builder.End();

            builder.BeginRow(8f, TextAnchor.UpperLeft);
            builder.BeginPanel(resultsPanelWidth, -1f, TextAnchor.UpperLeft, 8f, 4f, "results_panel");
            builder.AddHeader("文本条目");
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
            builder.BeginRow(6f, TextAnchor.MiddleCenter);
            builder.AddButton("上一页", PreviousPage, "previous_page");
            _previousPageButton = builder.Last;
            builder.AddLabel("第 1 / 1 页 · 0 条");
            _pageLabel = builder.Last;
            builder.AddButton("下一页", NextPage, "next_page");
            _nextPageButton = builder.Last;
            builder.End();
            builder.End();

            builder.BeginPanel(-1f, -1f, TextAnchor.UpperLeft, 8f, 4f, "detail_panel");
            builder.BeginScrollFlexible(0f);
            builder.BeginRow(8f, TextAnchor.MiddleLeft);
            _entryTitle = AddDetailLabel(builder, "");
            _entryTitle.SetFlexible(1f, 0f);
            builder.AddButton("详细信息", OpenEntryDetailsWindow, "entry_details");
            _entryDetailsButton = builder.Last;
            builder.End();
            _entrySource = AddDetailLabel(builder, "");
            _entryCategory = AddDetailLabel(builder, "");
            builder.AddLabel("原文基线（只读）");
            _originalInput = AddReadOnlyInput(builder, "原文基线", editorInputWidth, 116f, "original_text");
            builder.AddLabel("替换文本（可多行，空内容也会保留）");
            builder.AddInput("在此输入替换文本…", string.Empty, UpdateReplacementDraft, "replacement", UpdateReplacementDraft);
            _replacementInput = builder.Last;
            ConfigureMultiline(_replacementInput, editorInputWidth, 164f, false);
            _draftLabel = AddDetailLabel(builder, "");
            builder.End();
            builder.BeginRow(8f, TextAnchor.MiddleLeft);
            builder.AddButton("暂存此条", StageCurrentDraft, "stage_entry");
            _stageButton = builder.Last;
            builder.AddButton("恢复原文", RestoreCurrentEntry, "restore_entry");
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
            builder.AddButton("覆盖范围", OpenCoverageWindow, "coverage_summary");
            _openCoverageButton = builder.Last;
            _openCoverageButton.SetTooltip("查看本编辑器可枚举的文本范围与明确的覆盖缺口。");
            builder.AddButton("导入", OpenImportWindow, "open_import");
            _openImportButton = builder.Last;
            builder.AddButton("导出", OpenExportWindow, "open_export");
            _openExportButton = builder.Last;
            builder.AddButton("保存并应用", SaveAndApply, "save_apply");
            _saveButton = builder.Last;
            builder.AddButton("关闭", RequestClose, "close_editor");
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
            OpenInformationWindow("文本覆盖范围", ValueOrFallback(_controller.CoverageSummary, "覆盖统计尚未就绪。"),
                "完整读取游戏字符串表与模组官方字典；硬编码和自建文本系统不可通用枚举。");
        }


        private void OpenEntryDetailsWindow()
        {
            if (_selectedEntry == null)
            {
                return;
            }

            OpenInformationWindow("详细信息", BuildEntryDetails(_selectedEntry), null);
        }

        private static string BuildEntryDetails(TextEntry entry)
        {
            StringBuilder details = new StringBuilder(512);
            details.Append("来源：").Append(ValueOrFallback(entry.SourceName, "未知来源"))
                .Append("\n来源版本：").Append(ValueOrFallback(entry.SourceVersion, "未知"))
                .Append("\n类别：").Append(string.IsNullOrWhiteSpace(entry.Category) ? "未分类" : entry.Category)
                .Append("\n表名：").Append(ValueOrFallback(entry.TableName, "未知表"))
                .Append("\n键名：").Append(ValueOrFallback(entry.KeyName, "未命名文本"))
                .Append("\n原始标识：").Append(FormatTextId(entry.Id))
                .Append("\n语言取值：").Append(entry.IsFallback ? "回退值" : "当前语言值")
                .Append("\n格式器：").Append(entry.IsSmart ? "智能格式文本" : "普通文本");
            if (!string.IsNullOrWhiteSpace(entry.Description))
            {
                details.Append("\n用途/格式说明：").Append(entry.Description);
            }

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
            builder.AddButton("关闭", () => _coverageWindow?.Close(), "close_information");
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

            TryCommitCurrentDraft(null, "关闭编辑器");
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
                _localeNames.Add("未载入语言");
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
                    ShowError("切换语言失败", "无法载入所选语言。\n\n" + exception.Message);
                }
            }, "切换语言");
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
            _categoryLabels = new List<string>(sorted.Count + 1) { "全部类别" };
            for (int i = 0; i < sorted.Count; i++)
            {
                _categoryValues.Add(sorted[i]);
                _categoryLabels.Add(string.IsNullOrEmpty(sorted[i]) ? "未分类" : sorted[i]);
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
                if (entry == null)
                {
                    continue;
                }

                string sourceId = entry.Id.SourceId ?? string.Empty;
                sourceIds.Add(sourceId);
                if (!sourceNames.ContainsKey(sourceId))
                {
                    sourceNames.Add(sourceId, entry.SourceName ?? string.Empty);
                }
            }

            List<string> sorted = new List<string>(sourceIds);
            sorted.Sort(StringComparer.OrdinalIgnoreCase);
            _sourceValues = new List<string>(sorted.Count + 1) { null };
            _sourceLabels = new List<string>(sorted.Count + 1) { "全部来源" };
            for (int i = 0; i < sorted.Count; i++)
            {
                string sourceId = sorted[i];
                string sourceName = sourceNames[sourceId];
                _sourceValues.Add(sourceId);
                _sourceLabels.Add(string.IsNullOrWhiteSpace(sourceName) || string.Equals(sourceName, sourceId, StringComparison.Ordinal)
                    ? "未知来源"
                    : sourceName);
            }

            if (_selectedSource != null && !sourceIds.Contains(_selectedSource))
            {
                _selectedSource = null;
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
            _page = 0;
            RefreshRows();
        }

        private void OnModifiedChanged(int index)
        {
            if (index < 0 || index >= ModifiedOptions.Count)
            {
                return;
            }

            _modifiedFilter = index;
            _page = 0;
            RefreshRows();
        }

        private void OnSearchTyping(string value)
        {
            _searchQuery = value ?? string.Empty;
            _page = 0;
            RefreshRows();
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
                    builder.AddLabel("没有匹配的文本。");
                    _emptyResultsLabel = builder.Last;
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
            _pageLabel?.SetText("第 " + (_page + 1) + " / " + pageCount + " 页 · " + filteredCount + " 条");
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
                || ContainsIgnoreCase(entry.SourceName, query)
                || ContainsIgnoreCase(entry.SourceVersion, query)
                || ContainsIgnoreCase(entry.TableName, query)
                || ContainsIgnoreCase(entry.KeyName, query)
                || ContainsIgnoreCase(entry.Category, query)
                || ContainsIgnoreCase(entry.Description, query)
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
                text.Append("  · 已改");
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
                return "（空文本）";
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

            TryCommitCurrentDraft(() => SelectEntry(entry), "切换文本");
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
                _entryTitle?.SetText("请选择一条文本。");
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
            _entryTitle?.SetText("文本条目");
            _entrySource?.SetText("来源：" + ValueOrFallback(entry.SourceName, "未知来源"));
            _entryCategory?.SetText("类别：" + (string.IsNullOrWhiteSpace(entry.Category) ? "未分类" : entry.Category));
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
                message = "有未暂存输入；离开此条前会先检查并保留。";
            }
            else if (_controller.State.IsModified(_selectedEntry))
            {
                message = "此条已暂存；保存并应用后才会持久化。";
            }
            else
            {
                message = "此条与原文一致。";
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
            TryCommitCurrentDraft(null, "暂存文本");
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
                    ShowError("恢复原文失败", "无法暂存原文恢复操作。\n\n" + exception.Message);
                }
            }, "恢复原文");
        }

        private void SaveAndApply()
        {
            if (_controller.IsLoading || _saveApplyPending)
            {
                return;
            }

            TryCommitCurrentDraft(BeginSaveAndApply, "保存并应用");
        }

        private void BeginSaveAndApply()
        {
            if (!_controller.HasUnsavedChanges && _controller.SelectedPresetId == _controller.ActivePresetId)
            {
                RefreshMainStatus("工作区没有未保存修改，所选预设也已应用。");
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
                ShowError("保存并应用失败", "修改仍保留在本次编辑状态中。\n\n" + exception.Message);
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
                ShowError("保存或应用未完成", ValueOrFallback(_controller.Status, "工作区修改或预设切换仍未完成，请检查错误信息后重试。"));
                return;
            }

            RefreshMainStatus("保存并应用完成。");
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
            }, "关闭编辑器");
        }

        private void OpenImportWindow()
        {
            TryCommitCurrentDraft(BuildImportWindow, "打开导入窗口");
        }

        private void OpenExportWindow()
        {
            TryCommitCurrentDraft(BuildExportWindow, "打开导出窗口");
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
                ShowError("文本检查失败", "当前输入仍保留在编辑器中，尚未暂存。\n\n" + exception.Message);
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

            CustomUIBuilder builder = CreateNativeSettingsWindow(ValidationWindowId, "文本检查提示")
                .UseMetaPrefabs()
                .Center()
                .SetCloseOnEscape(true)
                .OnClose(OnValidationWindowClosed);

            builder.AddHeader("暂存前需要确认");
            builder.AddWarning("格式检查发现以下内容。确认前不会更改暂存状态；检查提示可能涉及占位符或富文本标记。");
            builder.AddLabel("操作：" + transitionName);
            builder.AddInput("检查提示", JoinLines(warnings), null, "validation_details");
            ConfigureMultiline(builder.Last, 590f, 220f, true);
            builder.BeginRow(8f, TextAnchor.MiddleRight);
            builder.AddButton("返回编辑", ReturnFromValidation, "validation_return");
            builder.AddButton("确认仍然暂存", ApproveValidation, "validation_accept");
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
                ShowError("暂存文本失败", "当前输入仍保留在编辑器中。\n\n" + exception.Message);
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
                ShowError(transitionName + "失败", exception.Message);
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
            {
                status = _controller.IsLoading ? "正在载入语言并保存…" : "正在保存并应用…";
            }

            if (string.IsNullOrWhiteSpace(status))
            {
                if (_controller.IsLoading)
                {
                    status = "正在载入文本目录…";
                }
                else if (string.IsNullOrWhiteSpace(_controller.Status))
                {
                    status = "就绪。";
                }
                else if (_controller.Status.StartsWith("已读取 ", StringComparison.Ordinal))
                {
                    status = "文本目录已就绪。";
                }
                else if (_controller.Status.StartsWith("已选择", StringComparison.Ordinal))
                {
                    status = "编辑目标已切换。";
                }
                else
                {
                    status = _controller.Status;
                }
            }

            _statusLabel?.SetText(status);
            if (_dirtyLabel != null)
            {
                string state = _controller.HasUnsavedChanges ? "工作区未保存" : "工作区已保存";
                state += " · " + _controller.State.OverrideCount + " 项覆盖";
                if (_controller.State.UnavailableOverrideCount > 0)
                {
                    state += " · " + _controller.State.UnavailableOverrideCount + " 项不可用";
                }

                _dirtyLabel.SetText(state);
            }

            if (_saveButton != null)
            {
                _saveButton.SetInteractable(!_controller.IsLoading && !_saveApplyPending && _pendingEntry == null && CanSave());
            }
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
            builder.AddInput("详细信息", message ?? string.Empty, null, "error_details");
            ConfigureMultiline(builder.Last, 570f, 230f, true);
            builder.BeginRow(8f, TextAnchor.MiddleRight);
            builder.AddButton("重新打开编辑器", () =>
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
            builder.AddButton("关闭提示", () =>
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
            CustomUIBuilder builder = CreateNativeSettingsWindow(ImportWindowId, "合并导入")
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

            builder.AddHeader("导入一个预设文件");
            builder.AddLabel("数据目录：" + ValueOrFallback(_controller.DataFolder, "不可用"));
            builder.AddLabel("选择一个文件和一个目标预设；预览只检查合并结果，确认后仍需保存并应用。");
            builder.BeginRow(8f, TextAnchor.MiddleLeft);
            builder.AddDropdown(BuildImportFileOptions(), GetImportFileIndex(), OnImportFileChanged, "import_file");
            _importFilesDropdown = builder.Last;
            _importFilesDropdown.SetFlexible(1f, 0f);
            builder.AddButton("刷新列表", RefreshImportFileList, "refresh_import_files");
            builder.AddButton("预览", PreviewImport, "preview_import");
            _importPreviewButton = builder.Last;
            builder.End();

            builder.BeginRow(8f, TextAnchor.MiddleLeft);
            BuildImportTargetControl(builder);
            builder.End();

            _importFileLabel = AddDetailLabel(builder, "");
            builder.AddLabel("合并预览");
            builder.AddInput("预览结果", string.Empty, null, "import_preview");
            _importPreviewText = builder.Last;
            ConfigureMultiline(_importPreviewText, 710f, 245f, true);

            builder.BeginRow(8f, TextAnchor.MiddleLeft);
            builder.AddLabel("冲突处理");
            builder.Last.SetFixedWidth(112f);
            builder.AddDropdown(ConflictOptions, 0, OnImportConflictChanged, "import_conflict");
            builder.Last.SetFixedWidth(210f);
            builder.AddButton("合并导入并暂存", ConfirmImport, "confirm_import");
            _importConfirmButton = builder.Last;
            builder.End();
            builder.AddButton("关闭", () => _importWindow?.Close(), "close_import");

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
                ShowError("读取导入文件失败", "无法读取模组管理目录中的 JSON 文件列表。\n\n" + exception.Message);
            }
        }

        private List<string> BuildImportFileOptions()
        {
            if (_importFiles.Count == 0)
            {
                return new List<string> { "（没有可导入文件）" };
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
                ? "尚未发现 JSON 文件。将单个文件复制到模组数据目录后刷新列表。"
                : "文件：" + _importFiles[GetImportFileIndex()]);
            SetReadOnlyText(_importPreviewText, FormatImportPreview(_importPreview));
            _importFilesDropdown?.SetInteractable(!_controller.IsLoading && !_saveApplyPending);
            _importPreviewButton?.SetInteractable(_importFiles.Count > 0 && !_controller.IsLoading && !_saveApplyPending);
            _importConfirmButton?.SetInteractable(_importPreview != null && !_controller.IsLoading && !_saveApplyPending);
        }
        private string FormatImportPreview(PresetImportPreview preview)
        {
            if (preview == null)
            {
                return "选择文件和目标预设后点击“预览”。";
            }

            StringBuilder summary = new StringBuilder();
            summary.Append("目标：").Append(preview.CreatesPreset
                ? "新建预设 · " + preview.PresetName
                : GetPresetLabel(preview.TargetPresetId));
            if (preview.CreatesPreset)
            {
                summary.Append("（确认后创建）");
            }

            summary.Append('\n');
            AppendPresetStatistics(summary, "导入内容", preview.Incoming);
            summary.Append('\n');
            AppendPresetStatistics(summary, "目标已有", preview.Existing);
            summary.Append("\n冲突：").Append(preview.ConflictCount)
                .Append("\n当前已知语言/目录中不可用：").Append(preview.UnavailableCount)
                .Append("\n\n检查信息：");
            if (preview.Warnings == null || preview.Warnings.Count == 0)
            {
                summary.Append("无");
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
                ShowError("导入预览失败", "所选文件未被暂存；请检查目标预设、格式、版本和文件内容。\n\n" + exception.Message);
            }
        }
        private void ConfirmImport()
        {
            if (_importPreview == null)
            {
                ShowError("尚未预览文件", "请先选择一个 JSON 文件和目标预设，查看完整预览后再合并导入。");
                return;
            }

            bool hadDraft = HasDraftChanges();
            TryCommitCurrentDraft(() =>
            {
                if (hadDraft)
                {
                    _importPreview = null;
                    PreviewImport();
                    RefreshMainStatus("编辑内容已暂存，导入预览已更新；请核对后再次确认合并。");
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
                    RefreshMainStatus("合并导入已暂存；保存并应用后才会持久化并切换游戏文本。");
                }
                catch (Exception exception)
                {
                    _importPreview = null;
                    UpdateImportWindow();
                    ShowError("暂存导入失败", "合并未完成；本地修改应保持不变。请重新预览后重试。\n\n" + exception.Message);
                }
            }, "合并导入");
        }
        private void BuildExportWindow()
        {
            if (_exportWindow != null && _exportWindow.IsAlive)
            {
                _exportWindow.BringToFront();
                return;
            }

            _exportPresetId = _controller.SelectedPresetId;
            CustomUIBuilder builder = CreateNativeSettingsWindow(ExportWindowId, "导出一个预设")
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

            builder.AddHeader("导出单个预设");
            BuildExportPresetControls(builder);
            builder.AddLabel("文件名（不含 .json）：");
            builder.AddInput("例如 zh_patch", _exportName, OnExportNameChanged, "export_name", OnExportNameChanged);
            TextInputContextMenu.Attach(builder.Last, "export_name");
            builder.AddLabel("结果 / 输出路径");
            builder.AddInput("导出后显示控制器返回的完整路径", string.Empty, null, "export_result");
            _exportResultInput = builder.Last;
            ConfigureMultiline(_exportResultInput, 600f, 76f, true);
            builder.AddLabel("只导出所选预设的覆盖，不包含原文基线或其他预设。");
            builder.BeginRow(8f, TextAnchor.MiddleRight);
            builder.AddButton("导出 JSON", ExportPatch, "export_patch");
            _exportButton = builder.Last;
            builder.AddButton("关闭", () => _exportWindow?.Close(), "close_export");
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
            TryCommitCurrentDraft(ExportSelectedPreset, "导出预设");
        }
        private void ExportSelectedPreset()
        {
            try
            {
                _controller.Export(_exportName, _exportPresetId);
                UpdateExportStatistics();
                SetReadOnlyText(_exportResultInput, ValueOrFallback(_controller.Status, "导出完成。"));
                RefreshMainStatus();
            }
            catch (Exception exception)
            {
                ShowError("导出失败", "未能导出所选预设；请检查文件名和模组数据目录。\n\n" + exception.Message);
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
