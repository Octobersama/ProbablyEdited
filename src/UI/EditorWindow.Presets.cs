using System;
using System.Collections.Generic;
using System.Text;
using System.Globalization;
using TMPro;

namespace ProbablyEdited
{
    public sealed partial class EditorWindow
    {
        private const string RenamePresetWindowId = "probably_edited.editor.rename_preset";
        private readonly List<TMP_Text> _presetTextBuffer = new List<TMP_Text>(8);
        private static readonly List<string> PageSizeOptions = new List<string> { "30", "50", "100", "200", "500" };

        private readonly List<int> _presetSlots = new List<int>();
        private readonly List<string> _presetCaptions = new List<string>();
        private readonly List<int> _importTargetSlots = new List<int>();
        private readonly List<string> _importTargetCaptions = new List<string>();
        private readonly List<int> _exportPresetSlots = new List<int>();
        private readonly List<string> _exportPresetCaptions = new List<string>();
        private CustomUIElement _presetDropdown;
        private CustomUIElement _createPresetButton;
        private CustomUIElement _renamePresetButton;
        private CustomUIWindow _renamePresetWindow;
        private CustomUIElement _renamePresetInput;
        private CustomUIElement _renamePresetErrorLabel;
        private int _renamePresetSlot;
        private string _renamePresetDraft = string.Empty;
        private CustomUIElement _presetStatusLabel;
        private CustomUIElement _pageSizeDropdown;
        private CustomUIElement _importTargetDropdown;
        private CustomUIElement _exportPresetDropdown;
        private CustomUIElement _exportButton;
        private CustomUIElement _exportStatisticsLabel;
        private int _pageSize = 30;
        private int _importTargetSlot;
        private int _exportPresetSlot;
        private PresetImportPreview _importPreview;

        private void BuildPresetControls(CustomUIBuilder builder)
        {
            builder.BeginRow(8f, UnityEngine.TextAnchor.MiddleLeft);
            builder.AddLabel("编辑预设");
            builder.Last.SetFixedWidth(112f);
            builder.AddDropdown(BuildPresetCaptions(), GetPresetIndex(_controller.SelectedPresetSlot), OnPresetChanged, "preset");
            _presetDropdown = builder.Last;
            _presetDropdown.SetFixedWidth(235f);
            DisablePresetNameRichText(_presetDropdown);
            builder.AddButton("新建预设", CreatePreset, "create_preset");
            _createPresetButton = builder.Last;
            builder.AddButton("重命名", OpenPresetRenameWindow, "rename_preset");
            _renamePresetButton = builder.Last;
            builder.AddLabel(BuildPresetStatus());
            _presetStatusLabel = builder.Last;
            DisablePresetNameRichText(_presetStatusLabel);
            _presetStatusLabel.SetFlexible(1f, 0f);
            builder.End();
        }

        private void BuildPageSizeControl(CustomUIBuilder builder)
        {
            builder.AddLabel("每页");
            builder.Last.SetFixedWidth(72f);
            builder.AddDropdown(PageSizeOptions, GetPageSizeIndex(), OnPageSizeChanged, "page_size");
            _pageSizeDropdown = builder.Last;
            _pageSizeDropdown.SetFixedWidth(86f);
        }

        private void BuildImportTargetControl(CustomUIBuilder builder)
        {
            builder.AddLabel("目标预设");
            builder.Last.SetFixedWidth(112f);
            builder.AddDropdown(BuildImportTargetCaptions(), GetImportTargetIndex(), OnImportTargetChanged, "import_target");
            _importTargetDropdown = builder.Last;
            DisablePresetNameRichText(_importTargetDropdown);
            _importTargetDropdown.SetFlexible(1f, 0f);
        }

        private void BuildExportPresetControls(CustomUIBuilder builder)
        {
            builder.BeginRow(8f, UnityEngine.TextAnchor.MiddleLeft);
            builder.AddLabel("导出预设");
            builder.Last.SetFixedWidth(112f);
            builder.AddDropdown(BuildExportPresetCaptions(), GetExportPresetIndex(), OnExportPresetChanged, "export_preset");
            _exportPresetDropdown = builder.Last;
            DisablePresetNameRichText(_exportPresetDropdown);
            _exportPresetDropdown.SetFlexible(1f, 0f);
            builder.End();

            _exportStatisticsLabel = AddDetailLabel(builder, string.Empty);
            DisablePresetNameRichText(_exportStatisticsLabel);
            UpdateExportStatistics();
        }

        private List<string> BuildPresetCaptions()
        {
            _presetSlots.Clear();
            _presetCaptions.Clear();
            IReadOnlyList<PresetInfo> presets = _controller.Presets;
            for (int i = 0; presets != null && i < presets.Count; i++)
            {
                PresetInfo preset = presets[i];
                if (preset == null)
                {
                    continue;
                }

                _presetSlots.Add(preset.Slot);
                string caption = _controller.GetPresetLabel(preset.Slot);
                if (preset.IsDirty)
                {
                    caption += " · 未保存";
                }

                _presetCaptions.Add(caption);
            }

            return _presetCaptions;
        }

        private List<string> BuildImportTargetCaptions()
        {
            _importTargetSlots.Clear();
            _importTargetCaptions.Clear();
            IReadOnlyList<PresetInfo> presets = _controller.Presets;
            for (int slot = 1; slot <= PresetWorkspace.MaxSlots; slot++)
            {
                PresetInfo existing = FindPreset(presets, slot);
                _importTargetSlots.Add(slot);
                string caption = existing == null ? PresetWorkspace.GetPresetName(slot) : _controller.GetPresetLabel(slot);
                if (existing == null)
                {
                    caption += " · 空槽";
                }
                else if (existing.IsActive)
                {
                    caption += " · 当前应用";
                }

                _importTargetCaptions.Add(caption);
            }

            return _importTargetCaptions;
        }

        private List<string> BuildExportPresetCaptions()
        {
            _exportPresetSlots.Clear();
            _exportPresetCaptions.Clear();
            IReadOnlyList<PresetInfo> presets = _controller.Presets;
            for (int i = 0; presets != null && i < presets.Count; i++)
            {
                PresetInfo preset = presets[i];
                if (preset == null)
                {
                    continue;
                }

                _exportPresetSlots.Add(preset.Slot);
                string caption = _controller.GetPresetLabel(preset.Slot);
                if (preset.IsActive)
                {
                    caption += " · 当前应用";
                }

                _exportPresetCaptions.Add(caption);
            }

            return _exportPresetCaptions;
        }

        private void RefreshPresetControls()
        {
            if (_presetDropdown != null)
            {
                _presetDropdown.SetOptions(BuildPresetCaptions());
                _presetDropdown.SetIndex(GetPresetIndex(_controller.SelectedPresetSlot), false);
            }

            _presetStatusLabel?.SetText(BuildPresetStatus());
            _createPresetButton?.SetInteractable(!_controller.IsLoading && !_saveApplyPending && _controller.CanCreatePreset && _pendingEntry == null);
            _renamePresetButton?.SetInteractable(!_controller.IsLoading && !_saveApplyPending && _pendingEntry == null);
            _presetDropdown?.SetInteractable(!_controller.IsLoading && !_saveApplyPending && _presetSlots.Count > 1 && _pendingEntry == null);

            if (_pageSizeDropdown != null)
            {
                _pageSizeDropdown.SetIndex(GetPageSizeIndex(), false);
                _pageSizeDropdown.SetInteractable(!_controller.IsLoading && !_saveApplyPending);
            }

            RefreshImportTargetOptions();
            RefreshExportPresetOptions();
        }

        private void RefreshImportTargetOptions()
        {
            if (_importTargetDropdown == null || _importWindow == null || !_importWindow.IsAlive)
            {
                return;
            }

            _importTargetDropdown.SetOptions(BuildImportTargetCaptions());
            _importTargetDropdown.SetIndex(GetImportTargetIndex(), false);
            _importTargetDropdown.SetInteractable(!_controller.IsLoading && !_saveApplyPending);
        }

        private void RefreshExportPresetOptions()
        {
            if (_exportPresetDropdown == null || _exportWindow == null || !_exportWindow.IsAlive)
            {
                return;
            }

            _exportPresetDropdown.SetOptions(BuildExportPresetCaptions());
            _exportPresetDropdown.SetIndex(GetExportPresetIndex(), false);
            _exportPresetDropdown.SetInteractable(!_controller.IsLoading && !_saveApplyPending && _exportPresetSlots.Count > 0);
            _exportButton?.SetInteractable(!_controller.IsLoading && !_saveApplyPending && _exportPresetSlots.Count > 0);
            UpdateExportStatistics();
        }

        private int GetPresetIndex(int slot)
        {
            return _presetSlots.IndexOf(slot);
        }

        private int GetImportTargetIndex()
        {
            int index = _importTargetSlots.IndexOf(_importTargetSlot);
            return index < 0 ? 0 : index;
        }

        private int GetExportPresetIndex()
        {
            int index = _exportPresetSlots.IndexOf(_exportPresetSlot);
            return index < 0 ? 0 : index;
        }

        private int GetPageSizeIndex()
        {
            return PageSizeOptions.IndexOf(_pageSize.ToString());
        }

        private string BuildPresetStatus()
        {
            return "当前应用：" + _controller.GetPresetLabel(_controller.ActivePresetSlot);
        }

        private string GetPresetName(int slot)
        {
            PresetInfo preset = FindPreset(_controller.Presets, slot);
            return preset == null ? PresetWorkspace.GetPresetName(slot) : preset.Name;
        }

        private string GetPresetLabel(int slot)
        {
            PresetInfo preset = FindPreset(_controller.Presets, slot);
            return preset == null ? PresetWorkspace.GetPresetName(slot) : _controller.GetPresetLabel(slot);
        }

        private static string GetPresetLabel(int slot, string name)
        {
            return StringComparer.Ordinal.Equals(name, PresetWorkspace.GetPresetName(slot))
                ? name
                : slot.ToString("00", CultureInfo.InvariantCulture) + " · " + name;
        }
        private void DisablePresetNameRichText(CustomUIElement element)
        {
            if (element == null)
                return;

            if (element.text != null)
                element.text.richText = false;
            if (element.input != null)
            {
                element.input.richText = false;
                if (element.input.textComponent != null)
                    element.input.textComponent.richText = false;
            }

            TMP_Dropdown dropdown = element.dropdown;
            if (dropdown != null)
            {
                if (dropdown.captionText != null)
                    dropdown.captionText.richText = false;
                if (dropdown.itemText != null)
                    dropdown.itemText.richText = false;
                DisablePresetNameRichText(dropdown.template);
            }

            DisablePresetNameRichText(element.Rect);
        }

        private void DisablePresetNameRichText(UnityEngine.Transform root)
        {
            if (root == null)
                return;

            _presetTextBuffer.Clear();
            try
            {
                root.GetComponentsInChildren(true, _presetTextBuffer);
                for (int i = 0; i < _presetTextBuffer.Count; i++)
                    _presetTextBuffer[i].richText = false;
            }
            finally
            {
                _presetTextBuffer.Clear();
            }
        }


        private static PresetInfo FindPreset(IReadOnlyList<PresetInfo> presets, int slot)
        {
            for (int i = 0; presets != null && i < presets.Count; i++)
            {
                if (presets[i] != null && presets[i].Slot == slot)
                {
                    return presets[i];
                }
            }

            return null;
        }

        private void OnPresetChanged(int index)
        {
            if (_controller.IsLoading || _saveApplyPending || index < 0 || index >= _presetSlots.Count)
            {
                RefreshPresetControls();
                return;
            }

            int slot = _presetSlots[index];
            if (slot == _controller.SelectedPresetSlot)
            {
                return;
            }

            RefreshPresetControls();
            TryCommitCurrentDraft(() => SelectPreset(slot), "切换预设");
        }

        private void SelectPreset(int slot)
        {
            _controller.SelectPreset(slot);
            RebindSelectedEntryToCurrentState();
            RefreshFilterOptions();
            RefreshDetail();
            RefreshRows();
            RefreshPresetControls();
            RefreshMainStatus();
        }

        private void CreatePreset()
        {
            if (_controller.IsLoading || _saveApplyPending || !_controller.CanCreatePreset || _pendingEntry != null)
            {
                return;
            }

            TryCommitCurrentDraft(() =>
            {
                _controller.CreatePreset();
                RebindSelectedEntryToCurrentState();
                RefreshFilterOptions();
                RefreshDetail();
                RefreshRows();
                RefreshPresetControls();
                RefreshMainStatus("已新建空白预设；原有预设与当前应用保持不变。保存并应用后才会切换游戏文本。");
            }, "新建预设");
        }
        private void OpenPresetRenameWindow()
        {
            if (_controller.IsLoading || _saveApplyPending || _pendingEntry != null)
                return;
            if (_renamePresetWindow != null && _renamePresetWindow.IsAlive)
            {
                _renamePresetWindow.BringToFront();
                return;
            }

            _renamePresetSlot = _controller.SelectedPresetSlot;
            _renamePresetDraft = _controller.GetPresetName(_renamePresetSlot);
            CustomUIBuilder builder = CreateNativeSettingsWindow(RenamePresetWindowId, "重命名预设")
                .UseMetaPrefabs()
                .Center()
                .SetCloseOnEscape(true)
                .OnClose(OnPresetRenameWindowClosed);

            builder.AddHeader("重命名所选预设");
            builder.AddLabel("保存并应用后，名称会写入本地预设工作区。");
            builder.AddInput("预设名称", _renamePresetDraft, OnPresetRenameInputChanged, "preset_name", OnPresetRenameInputChanged);
            _renamePresetInput = builder.Last;
            _renamePresetInput.SetFixedWidth(420f);
            DisablePresetNameRichText(_renamePresetInput);
            builder.AddLabel(string.Empty);
            _renamePresetErrorLabel = builder.Last;
            DisablePresetNameRichText(_renamePresetErrorLabel);

            builder.BeginRow(8f, UnityEngine.TextAnchor.MiddleRight);
            builder.AddButton("保存名称", ApplyPresetRename, "save_preset_name");
            builder.AddButton("取消", () => _renamePresetWindow?.Close(), "cancel_preset_name");
            builder.End();

            _renamePresetWindow = builder.Show();
            NativeFontSupport.Apply(_renamePresetWindow);
        }

        private void OnPresetRenameInputChanged(string value)
        {
            _renamePresetDraft = value ?? string.Empty;
            _renamePresetErrorLabel?.SetText(string.Empty);
        }


        private void ApplyPresetRename()
        {
            if (_controller.IsLoading || _saveApplyPending || _pendingEntry != null)
                return;

            try
            {
                _controller.RenamePreset(_renamePresetSlot, _renamePresetDraft);
                RefreshPresetControls();
                RefreshMainStatus();
                UpdateImportWindow();
                UpdateExportStatistics();
                ClosePresetRenameWindow();
            }
            catch (Exception exception)
            {
                _renamePresetErrorLabel?.SetText(exception.Message);
                NativeFontSupport.Apply(_renamePresetErrorLabel);
            }
        }

        private void ClosePresetRenameWindow()
        {
            if (_renamePresetWindow != null && _renamePresetWindow.IsAlive)
                _renamePresetWindow.Close();
            OnPresetRenameWindowClosed();
        }

        private void OnPresetRenameWindowClosed()
        {
            _renamePresetWindow = null;
            _renamePresetInput = null;
            _renamePresetErrorLabel = null;
            _renamePresetSlot = 0;
            _renamePresetDraft = string.Empty;
        }


        private void OnPageSizeChanged(int index)
        {
            if (index < 0 || index >= PageSizeOptions.Count)
            {
                return;
            }

            _pageSize = int.Parse(PageSizeOptions[index]);
            _page = 0;
            RefreshRows();
        }

        private void OnImportTargetChanged(int index)
        {
            if (_saveApplyPending || index < 0 || index >= _importTargetSlots.Count)
            {
                return;
            }

            _importTargetSlot = _importTargetSlots[index];
            _importPreview = null;
            UpdateImportWindow();
        }

        private void OnExportPresetChanged(int index)
        {
            if (_saveApplyPending || index < 0 || index >= _exportPresetSlots.Count)
            {
                return;
            }

            _exportPresetSlot = _exportPresetSlots[index];
            SetReadOnlyText(_exportResultInput, string.Empty);
            UpdateExportStatistics();
        }

        private void UpdateExportStatistics()
        {
            if (_exportStatisticsLabel == null || _exportWindow == null || !_exportWindow.IsAlive || _exportPresetSlot <= 0)
            {
                return;
            }

            PresetStatistics statistics = _controller.GetPresetStatistics(_exportPresetSlot);
            _exportStatisticsLabel.SetText("将导出“" + GetPresetLabel(_exportPresetSlot) + "”：" + statistics.OverrideCount + " 项修改 · "
                + statistics.LanguageCount + " 种语言 · " + statistics.SourceCount + " 个来源");
        }

        private static void AppendPresetStatistics(StringBuilder text, string label, PresetStatistics statistics)
        {
            text.Append(label)
                .Append("：")
                .Append(statistics.OverrideCount)
                .Append(" 项修改 · ")
                .Append(statistics.LanguageCount)
                .Append(" 种语言 · ")
                .Append(statistics.SourceCount)
                .Append(" 个来源");
        }

        private void ClearPresetControlReferences()
        {
            _presetDropdown = null;
            _createPresetButton = null;
            _renamePresetButton = null;
            _renamePresetInput = null;
            _renamePresetErrorLabel = null;
            _renamePresetSlot = 0;
            _renamePresetDraft = string.Empty;
            _presetStatusLabel = null;
            _pageSizeDropdown = null;
            _importTargetDropdown = null;
            _exportPresetDropdown = null;
            _exportStatisticsLabel = null;
            _exportButton = null;
        }
    }
}
