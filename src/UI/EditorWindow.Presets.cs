using System;
using System.Collections.Generic;
using System.Text;
using TMPro;

namespace ProbablyEdited
{
    public sealed partial class EditorWindow
    {
        private const string RenamePresetWindowId = "probably_edited.editor.rename_preset";
        private const string DeletePresetWindowId = "probably_edited.editor.delete_preset";
        private readonly List<TMP_Text> _presetTextBuffer = new List<TMP_Text>(8);
        private static readonly List<string> PageSizeOptions = new List<string> { "30", "50", "100", "200", "500" };

        private readonly List<string> _presetIds = new List<string>();
        private readonly List<string> _presetCaptions = new List<string>();
        private readonly List<string> _importTargetPresetIds = new List<string>();
        private readonly List<string> _importTargetCaptions = new List<string>();
        private readonly List<string> _exportPresetIds = new List<string>();
        private readonly List<string> _exportPresetCaptions = new List<string>();
        private CustomUIElement _presetDropdown;
        private CustomUIElement _createPresetButton;
        private CustomUIElement _renamePresetButton;
        private CustomUIElement _deletePresetButton;
        private CustomUIWindow _deletePresetWindow;
        private CustomUIElement _deletePresetErrorLabel;
        private string _deletePresetId;
        private string _deletePresetName = string.Empty;
        private PresetStatistics _deletePresetStatistics;
        private CustomUIWindow _renamePresetWindow;
        private CustomUIElement _renamePresetInput;
        private CustomUIElement _renamePresetErrorLabel;
        private string _renamePresetId;
        private string _renamePresetDraft = string.Empty;
        private CustomUIElement _presetStatusLabel;
        private CustomUIElement _pageSizeDropdown;
        private CustomUIElement _importTargetDropdown;
        private CustomUIElement _exportPresetDropdown;
        private CustomUIElement _exportButton;
        private CustomUIElement _exportStatisticsLabel;
        private int _pageSize = 100;
        private string _importTargetPresetId;
        private string _exportPresetId;
        private PresetImportPreview _importPreview;

        private void BuildPresetControls(CustomUIBuilder builder)
        {
            builder.BeginRow(8f, UnityEngine.TextAnchor.MiddleLeft);
            builder.AddLabel("编辑预设");
            builder.Last.SetFixedWidth(112f);
            builder.AddDropdown(BuildPresetCaptions(), GetPresetIndex(_controller.SelectedPresetId), OnPresetChanged, "preset");
            _presetDropdown = builder.Last;
            _presetDropdown.SetFixedWidth(235f);
            DisablePresetNameRichText(_presetDropdown);
            builder.AddButton("新建预设", CreatePreset, "create_preset");
            _createPresetButton = builder.Last;
            builder.AddButton("重命名", OpenPresetRenameWindow, "rename_preset");
            _renamePresetButton = builder.Last;
            builder.AddButton("删除", OpenPresetDeletionWindow, "delete_preset");
            _deletePresetButton = builder.Last;
            _deletePresetButton.SetTooltip(BuildPresetDeletionTooltip());
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
            _presetIds.Clear();
            _presetCaptions.Clear();
            IReadOnlyList<PresetInfo> presets = _controller.Presets;
            for (int i = 0; presets != null && i < presets.Count; i++)
            {
                PresetInfo preset = presets[i];
                _presetIds.Add(preset.Id);
                string caption = GetPresetLabel(presets, preset.Id);
                if (preset.IsDirty)
                    caption += " · 未保存";
                _presetCaptions.Add(caption);
            }

            return _presetCaptions;
        }

        private List<string> BuildImportTargetCaptions()
        {
            _importTargetPresetIds.Clear();
            _importTargetCaptions.Clear();
            IReadOnlyList<PresetInfo> presets = _controller.Presets;
            for (int i = 0; i < presets.Count; i++)
            {
                PresetInfo preset = presets[i];
                _importTargetPresetIds.Add(preset.Id);
                string caption = GetPresetLabel(presets, preset.Id);
                if (preset.IsActive)
                    caption += " · 当前应用";
                _importTargetCaptions.Add(caption);
            }

            if (_importTargetPresetId != null && FindPreset(presets, _importTargetPresetId) == null)
                _importTargetPresetId = _controller.SelectedPresetId;

            if (_controller.CanCreatePreset)
            {
                _importTargetPresetIds.Add(null);
                _importTargetCaptions.Add("新建预设");
            }
            else if (_importTargetPresetId == null)
            {
                _importTargetPresetId = _controller.SelectedPresetId;
            }

            return _importTargetCaptions;
        }

        private List<string> BuildExportPresetCaptions()
        {
            _exportPresetIds.Clear();
            _exportPresetCaptions.Clear();
            IReadOnlyList<PresetInfo> presets = _controller.Presets;
            for (int i = 0; i < presets.Count; i++)
            {
                PresetInfo preset = presets[i];
                _exportPresetIds.Add(preset.Id);
                string caption = GetPresetLabel(presets, preset.Id);
                if (preset.IsActive)
                    caption += " · 当前应用";
                _exportPresetCaptions.Add(caption);
            }

            if (_exportPresetId == null || FindPreset(presets, _exportPresetId) == null)
                _exportPresetId = _controller.SelectedPresetId;

            return _exportPresetCaptions;
        }

        private void RefreshPresetControls()
        {
            if (_presetDropdown != null)
            {
                _presetDropdown.SetOptions(BuildPresetCaptions());
                _presetDropdown.SetIndex(GetPresetIndex(_controller.SelectedPresetId), false);
            }

            _presetStatusLabel?.SetText(BuildPresetStatus());
            _createPresetButton?.SetInteractable(!_controller.IsLoading && !_saveApplyPending && _controller.CanCreatePreset && _pendingEntry == null);
            _renamePresetButton?.SetInteractable(!_controller.IsLoading && !_saveApplyPending && _pendingEntry == null);
            _deletePresetButton?.SetInteractable(!_controller.IsLoading && !_saveApplyPending && _controller.CanDeleteSelectedPreset && _pendingEntry == null);
            _deletePresetButton?.SetTooltip(BuildPresetDeletionTooltip());
            _presetDropdown?.SetInteractable(!_controller.IsLoading && !_saveApplyPending && _presetIds.Count > 1 && _pendingEntry == null);

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
            _exportPresetDropdown.SetInteractable(!_controller.IsLoading && !_saveApplyPending && _exportPresetIds.Count > 0);
            _exportButton?.SetInteractable(!_controller.IsLoading && !_saveApplyPending && _exportPresetIds.Count > 0);
            UpdateExportStatistics();
        }

        private int GetPresetIndex(string presetId)
        {
            return _presetIds.IndexOf(presetId);
        }

        private int GetImportTargetIndex()
        {
            int index = _importTargetPresetIds.IndexOf(_importTargetPresetId);
            return index < 0 ? 0 : index;
        }

        private int GetExportPresetIndex()
        {
            int index = _exportPresetIds.IndexOf(_exportPresetId);
            return index < 0 ? 0 : index;
        }

        private int GetPageSizeIndex()
        {
            return PageSizeOptions.IndexOf(_pageSize.ToString());
        }

        private string BuildPresetStatus()
        {
            return "当前应用：" + GetPresetLabel(_controller.Presets, _controller.ActivePresetId);
        }

        private string BuildPresetDeletionTooltip()
        {
            if (_controller.Presets.Count == 1)
                return "至少保留一个预设；先创建另一个预设并保存、应用后，才能删除当前预设。";
            if (StringComparer.Ordinal.Equals(_controller.SelectedPresetId, _controller.ActivePresetId))
                return "当前应用的预设不能删除；先保存并应用另一个预设后，才能删除当前预设。";
            return "删除所选非当前应用预设的草稿；仅在保存并应用后写入磁盘。";
        }

        private string GetPresetName(string presetId)
        {
            return _controller.GetPresetName(presetId);
        }

        private string GetPresetLabel(string presetId)
        {
            return GetPresetLabel(_controller.Presets, presetId);
        }

        private static string GetPresetLabel(IReadOnlyList<PresetInfo> presets, string presetId)
        {
            PresetInfo selected = FindPreset(presets, presetId);
            if (selected == null)
                return string.Empty;

            int duplicateCount = 0;
            int duplicateIndex = 0;
            for (int i = 0; i < presets.Count; i++)
            {
                PresetInfo preset = presets[i];
                if (!StringComparer.Ordinal.Equals(preset.Name, selected.Name))
                    continue;

                duplicateCount++;
                if (StringComparer.Ordinal.Equals(preset.Id, presetId))
                    duplicateIndex = duplicateCount;
            }

            if (duplicateCount == 1)
                return selected.Name;

            return selected.Name + " · 同名 " +
                duplicateIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/" +
                duplicateCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
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


        private static PresetInfo FindPreset(IReadOnlyList<PresetInfo> presets, string presetId)
        {
            for (int i = 0; presets != null && i < presets.Count; i++)
            {
                if (presets[i] != null && StringComparer.Ordinal.Equals(presets[i].Id, presetId))
                    return presets[i];
            }

            return null;
        }

        private void OnPresetChanged(int index)
        {
            if (_controller.IsLoading || _saveApplyPending || index < 0 || index >= _presetIds.Count)
            {
                RefreshPresetControls();
                return;
            }

            string presetId = _presetIds[index];
            if (StringComparer.Ordinal.Equals(presetId, _controller.SelectedPresetId))
                return;

            RefreshPresetControls();
            TryCommitCurrentDraft(() => SelectPreset(presetId), "切换预设");
        }

        private void SelectPreset(string presetId)
        {
            _controller.SelectPreset(presetId);
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

            _renamePresetId = _controller.SelectedPresetId;
            _renamePresetDraft = _controller.GetPresetName(_renamePresetId);
            CustomUIBuilder builder = CreateNativeSettingsWindow(RenamePresetWindowId, "重命名预设")
                .UseMetaPrefabs()
                .Center()
                .SetCloseOnEscape(true)
                .OnClose(OnPresetRenameWindowClosed);

            builder.AddHeader("重命名所选预设");
            builder.AddLabel("保存并应用后，名称会写入本地预设工作区。");
            builder.AddInput("预设名称", _renamePresetDraft, OnPresetRenameInputChanged, "preset_name");
            _renamePresetInput = builder.Last;
            _renamePresetInput.SetFixedWidth(420f);
            DisablePresetNameRichText(_renamePresetInput);
            TextInputContextMenu.Attach(_renamePresetInput, _renamePresetId);
            TextInputContextMenu.ResetHistory(_renamePresetInput, _renamePresetId);
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
                _controller.RenamePreset(_renamePresetId, _renamePresetDraft);
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
            _renamePresetId = null;
            _renamePresetDraft = string.Empty;
        }

        private void OpenPresetDeletionWindow()
        {
            if (_controller.IsLoading || _saveApplyPending || _pendingEntry != null || !_controller.CanDeleteSelectedPreset)
                return;
            if (_deletePresetWindow != null && _deletePresetWindow.IsAlive)
            {
                _deletePresetWindow.BringToFront();
                return;
            }

            _deletePresetId = _controller.SelectedPresetId;
            _deletePresetName = _controller.GetPresetName(_deletePresetId);
            _deletePresetStatistics = _controller.GetPresetStatistics(_deletePresetId);
            CustomUIBuilder builder = CreateNativeSettingsWindow(DeletePresetWindowId, "删除预设")
                .UseMetaPrefabs()
                .Center()
                .SetCloseOnEscape(true)
                .OnClose(OnPresetDeletionWindowClosed);

            builder.AddHeader("确认删除预设");
            builder.AddLabel("名称：“" + GetPresetLabel(_deletePresetId) + "”");
            DisablePresetNameRichText(builder.Last);
            builder.AddLabel("现有内容：" + _deletePresetStatistics.OverrideCount + " 项修改 · "
                + _deletePresetStatistics.LanguageCount + " 种语言 · "
                + _deletePresetStatistics.SourceCount + " 个来源");
            builder.AddWarning("删除会丢弃该预设的全部修改，以及当前文本框中尚未暂存的输入。当前应用的文本不会改变。");
            builder.AddLabel("删除只会成为工作区草稿；点击主窗口“保存并应用”后才会写入磁盘。取消不会更改任何内容。");
            builder.AddLabel(string.Empty);
            _deletePresetErrorLabel = builder.Last;
            DisablePresetNameRichText(_deletePresetErrorLabel);

            builder.BeginRow(8f, UnityEngine.TextAnchor.MiddleRight);
            builder.AddButton("删除预设", ConfirmPresetDeletion, "confirm_delete_preset");
            builder.AddButton("取消", () => _deletePresetWindow?.Close(), "cancel_delete_preset");
            builder.End();

            _deletePresetWindow = builder.Show();
            NativeFontSupport.Apply(_deletePresetWindow);
        }

        private void ConfirmPresetDeletion()
        {
            if (_controller.IsLoading || _saveApplyPending || _pendingEntry != null)
            {
                _deletePresetErrorLabel?.SetText("请等待当前操作完成后再确认删除。");
                return;
            }

            try
            {
                PresetInfo current = FindPreset(_controller.Presets, _deletePresetId);
                PresetStatistics statistics = current == null ? null : _controller.GetPresetStatistics(_deletePresetId);
                if (current == null
                    || !StringComparer.Ordinal.Equals(_controller.SelectedPresetId, _deletePresetId)
                    || !StringComparer.Ordinal.Equals(current.Name, _deletePresetName)
                    || statistics.OverrideCount != _deletePresetStatistics.OverrideCount
                    || statistics.LanguageCount != _deletePresetStatistics.LanguageCount
                    || statistics.SourceCount != _deletePresetStatistics.SourceCount)
                {
                    throw new InvalidOperationException("预设在确认前已变化；请关闭此窗口并重新打开确认。");
                }

                _controller.DeletePreset(_deletePresetId);
                _importPreview = null;
                if (FindPreset(_controller.Presets, _exportPresetId) == null)
                {
                    _exportPresetId = _controller.SelectedPresetId;
                    SetReadOnlyText(_exportResultInput, string.Empty);
                }

                RebindSelectedEntryToCurrentState();
                RefreshFilterOptions();
                RefreshDetail();
                RefreshRows();
                RefreshPresetControls();
                UpdateImportWindow();
                RefreshMainStatus("已移除“" + _deletePresetName + "”；保存并应用后删除本地记录。");
                ClosePresetDeletionWindow();
            }
            catch (Exception exception)
            {
                _deletePresetErrorLabel?.SetText(exception.Message);
                NativeFontSupport.Apply(_deletePresetErrorLabel);
            }
        }

        private void ClosePresetDeletionWindow()
        {
            if (_deletePresetWindow != null && _deletePresetWindow.IsAlive)
                _deletePresetWindow.Close();
            OnPresetDeletionWindowClosed();
        }

        private void OnPresetDeletionWindowClosed()
        {
            _deletePresetWindow = null;
            _deletePresetErrorLabel = null;
            _deletePresetId = null;
            _deletePresetName = string.Empty;
            _deletePresetStatistics = null;
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
            if (_saveApplyPending || index < 0 || index >= _importTargetPresetIds.Count)
            {
                return;
            }

            _importTargetPresetId = _importTargetPresetIds[index];
            _importPreview = null;
            UpdateImportWindow();
        }

        private void OnExportPresetChanged(int index)
        {
            if (_saveApplyPending || index < 0 || index >= _exportPresetIds.Count)
            {
                return;
            }

            _exportPresetId = _exportPresetIds[index];
            SetReadOnlyText(_exportResultInput, string.Empty);
            UpdateExportStatistics();
        }

        private void UpdateExportStatistics()
        {
            if (_exportStatisticsLabel == null || _exportWindow == null || !_exportWindow.IsAlive || _exportPresetId == null)
            {
                return;
            }

            PresetStatistics statistics = _controller.GetPresetStatistics(_exportPresetId);
            _exportStatisticsLabel.SetText("将导出“" + GetPresetLabel(_exportPresetId) + "”：" + statistics.OverrideCount + " 项修改 · "
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
            _deletePresetButton = null;
            _renamePresetInput = null;
            _renamePresetErrorLabel = null;
            _renamePresetId = null;
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
