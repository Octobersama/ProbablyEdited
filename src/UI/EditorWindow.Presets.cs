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
            builder.AddLabel(UiText.Get("preset.section.editor"));
            builder.Last.SetFixedWidth(112f);
            builder.AddDropdown(BuildPresetCaptions(), GetPresetIndex(_controller.SelectedPresetId), OnPresetChanged, "preset");
            _presetDropdown = builder.Last;
            _presetDropdown.SetFixedWidth(235f);
            DisablePresetNameRichText(_presetDropdown);
            builder.AddButton(UiText.Get("preset.action.create"), CreatePreset, "create_preset");
            _createPresetButton = builder.Last;
            builder.AddButton(UiText.Get("preset.action.rename"), OpenPresetRenameWindow, "rename_preset");
            _renamePresetButton = builder.Last;
            builder.AddButton(UiText.Get("preset.action.delete"), OpenPresetDeletionWindow, "delete_preset");
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
            builder.AddLabel(UiText.Get("preset.page_size"));
            builder.Last.SetFixedWidth(72f);
            builder.AddDropdown(PageSizeOptions, GetPageSizeIndex(), OnPageSizeChanged, "page_size");
            _pageSizeDropdown = builder.Last;
            _pageSizeDropdown.SetFixedWidth(86f);
        }

        private void BuildImportTargetControl(CustomUIBuilder builder)
        {
            builder.AddLabel(UiText.Get("preset.target"));
            builder.Last.SetFixedWidth(112f);
            builder.AddDropdown(BuildImportTargetCaptions(), GetImportTargetIndex(), OnImportTargetChanged, "import_target");
            _importTargetDropdown = builder.Last;
            DisablePresetNameRichText(_importTargetDropdown);
            _importTargetDropdown.SetFlexible(1f, 0f);
        }

        private void BuildExportPresetControls(CustomUIBuilder builder)
        {
            builder.BeginRow(8f, UnityEngine.TextAnchor.MiddleLeft);
            builder.AddLabel(UiText.Get("preset.export_section"));
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
                    caption += UiText.Get("preset.caption.unsaved");
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
                    caption += UiText.Get("preset.caption.active");
                _importTargetCaptions.Add(caption);
            }

            if (_importTargetPresetId != null && FindPreset(presets, _importTargetPresetId) == null)
                _importTargetPresetId = _controller.SelectedPresetId;

            if (_controller.CanCreatePreset)
            {
                _importTargetPresetIds.Add(null);
                _importTargetCaptions.Add(UiText.Get("preset.action.create"));
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
                    caption += UiText.Get("preset.caption.active");
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
            return UiText.Format("preset.status.active", GetPresetLabel(_controller.Presets, _controller.ActivePresetId));
        }

        private string BuildPresetDeletionTooltip()
        {
            if (_controller.Presets.Count == 1)
                return UiText.Get("preset.tooltip.delete_only");
            if (StringComparer.Ordinal.Equals(_controller.SelectedPresetId, _controller.ActivePresetId))
                return UiText.Get("preset.tooltip.delete_active");
            return UiText.Get("preset.tooltip.delete_inactive");
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

            return UiText.Format("preset.caption.duplicate", selected.Name, duplicateIndex, duplicateCount);
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
            TryCommitCurrentDraft(() => SelectPreset(presetId), UiText.Get("editor.transition.change_preset"));
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
                RefreshMainStatus(UiText.Get("preset.status.created"));
            }, UiText.Get("editor.transition.create_preset"));
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
            CustomUIBuilder builder = CreateNativeSettingsWindow(RenamePresetWindowId, UiText.Get("preset.rename.title"))
                .UseMetaPrefabs()
                .Center()
                .SetCloseOnEscape(true)
                .OnClose(OnPresetRenameWindowClosed);

            builder.AddHeader(UiText.Get("preset.rename.heading"));
            builder.AddLabel(UiText.Get("preset.rename.help"));
            builder.AddInput(UiText.Get("preset.rename.name"), _renamePresetDraft, null, "preset_name", OnPresetRenameInputChanged);
            _renamePresetInput = builder.Last;
            _renamePresetInput.SetFixedWidth(420f);
            DisablePresetNameRichText(_renamePresetInput);
            TextInputContextMenu.Attach(_renamePresetInput, _renamePresetId);
            TextInputContextMenu.ResetHistory(_renamePresetInput, _renamePresetId);
            builder.AddLabel(string.Empty);
            _renamePresetErrorLabel = builder.Last;
            DisablePresetNameRichText(_renamePresetErrorLabel);

            builder.BeginRow(8f, UnityEngine.TextAnchor.MiddleRight);
            builder.AddButton(UiText.Get("preset.rename.save"), ApplyPresetRename, "save_preset_name");
            builder.AddButton(UiText.Get("preset.rename.cancel"), () => _renamePresetWindow?.Close(), "cancel_preset_name");
            builder.End();

            _renamePresetWindow = builder.Show();
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
            CustomUIBuilder builder = CreateNativeSettingsWindow(DeletePresetWindowId, UiText.Get("preset.delete.title"))
                .UseMetaPrefabs()
                .Center()
                .SetCloseOnEscape(true)
                .OnClose(OnPresetDeletionWindowClosed);

            builder.AddHeader(UiText.Get("preset.delete.heading"));
            builder.AddLabel(UiText.Format("preset.delete.name", GetPresetLabel(_deletePresetId)));
            DisablePresetNameRichText(builder.Last);
            builder.AddLabel(UiText.Format("preset.delete.statistics", _deletePresetStatistics.OverrideCount,
                _deletePresetStatistics.LanguageCount, _deletePresetStatistics.SourceCount));
            builder.AddWarning(UiText.Get("preset.delete.warning"));
            builder.AddLabel(UiText.Get("preset.delete.persist_help"));
            builder.AddLabel(string.Empty);
            _deletePresetErrorLabel = builder.Last;
            DisablePresetNameRichText(_deletePresetErrorLabel);

            builder.BeginRow(8f, UnityEngine.TextAnchor.MiddleRight);
            builder.AddButton(UiText.Get("preset.delete.confirm"), ConfirmPresetDeletion, "confirm_delete_preset");
            builder.AddButton(UiText.Get("preset.delete.cancel"), () => _deletePresetWindow?.Close(), "cancel_delete_preset");
            builder.End();

            _deletePresetWindow = builder.Show();
        }

        private void ConfirmPresetDeletion()
        {
            if (_controller.IsLoading || _saveApplyPending || _pendingEntry != null)
            {
                _deletePresetErrorLabel?.SetText(UiText.Get("preset.delete.wait"));
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
                    throw new InvalidOperationException(UiText.Get("preset.delete.changed"));
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
                RefreshMainStatus(UiText.Format("preset.delete.removed", _deletePresetName));
                ClosePresetDeletionWindow();
            }
            catch (Exception exception)
            {
                _deletePresetErrorLabel?.SetText(exception.Message);
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
            _exportStatisticsLabel.SetText(UiText.Format("preset.export.statistics", GetPresetLabel(_exportPresetId),
                statistics.OverrideCount, statistics.LanguageCount, statistics.SourceCount));
        }

        private static void AppendPresetStatistics(StringBuilder text, string label, PresetStatistics statistics)
        {
            text.Append(UiText.Format("preset.statistics.line", label,
                statistics.OverrideCount, statistics.LanguageCount, statistics.SourceCount));
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
