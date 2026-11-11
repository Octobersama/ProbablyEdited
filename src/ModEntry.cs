using System;
using HarmonyLib;

namespace ProbablyEdited
{
    public sealed class ModEntry : IMod
    {
        public const string ModId = "probably_edited";
        internal static ModEntry Current { get; private set; }
        private EditorController controller;
        private EditorWindow editor;
        private ModLog log;
        private UiLocalization uiLocalization;

        public void Init(ModManifest manifest)
        {
            log = new ModLog(manifest);
            uiLocalization = new UiLocalization();
            Current = this;
            ModHelper.GetConfig(manifest.ID).Define("editor", "Probably Edited", "Probably Edited",
                () => uiLocalization.GetOptionText("config.editor.action"),
                tooltip: () => uiLocalization.GetOptionText("config.editor.tooltip"));
        }

        public void OnEnable()
        {
            uiLocalization.RegisterOptions();
            ModHook.OnModAssetsLoaded += OnAssetsReady;
        }

        private void OnAssetsReady(LoadedMod mod)
        {
            if (mod?.Manifest?.ID != ModId) return;
            ModHook.OnModAssetsLoaded -= OnAssetsReady;
            uiLocalization.Attach(mod);
            controller = new EditorController(log);
            editor = new EditorWindow(controller);
            controller.Initialize();
        }

        public void OnDisable()
        {
            ModHook.OnModAssetsLoaded -= OnAssetsReady;
            editor?.Close();
            controller?.Dispose();
            NativeFontSupport.Release();
            uiLocalization?.Dispose();
            Current = null;
        }

        internal CustomUIWindow OpenEditor()
        {
            try
            {
                if (editor == null)
                    throw new InvalidOperationException(uiLocalization.GetOptionText("status.preparing"));
                if (!editor.IsOpen) uiLocalization.ActivateForEditor();
                return editor.Open();
            }
            catch (Exception error)
            {
                log.Error(uiLocalization.FormatOptionText("log.editor_open_failed", error));
                throw;
            }
        }
    }

    [HarmonyPatch(typeof(ModConfigWindow), nameof(ModConfigWindow.Open))]
    internal static class EditorSettingsEntry
    {
        private static bool Prefix(LoadedMod __0, ref CustomUIWindow __result)
        {
            if (__0?.Manifest?.ID != ModEntry.ModId || ModEntry.Current == null) return true;
            __result = ModEntry.Current.OpenEditor();
            return false;
        }
    }
}
