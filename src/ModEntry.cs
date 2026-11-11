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

        public void Init(ModManifest manifest)
        {
            log = new ModLog(manifest);
            controller = new EditorController(log);
            editor = new EditorWindow(controller);
            Current = this;
            ModHelper.GetConfig(manifest.ID).Define("editor", "文本编辑器", "Probably Edited", () => "打开文本编辑器",
                tooltip: () => "从本模组的设置齿轮进入全文本编辑器。");
        }

        public void OnEnable() => ModHook.OnModAssetsLoaded += OnAssetsReady;

        private void OnAssetsReady(LoadedMod mod)
        {
            if (mod?.Manifest?.ID != ModId) return;
            ModHook.OnModAssetsLoaded -= OnAssetsReady;
            controller.Initialize();
        }

        public void OnDisable()
        {
            ModHook.OnModAssetsLoaded -= OnAssetsReady;
            editor?.Close();
            controller?.Dispose();
            NativeFontSupport.Release();
            Current = null;
        }

        internal CustomUIWindow OpenEditor()
        {
            try { return editor.Open(); }
            catch (Exception error)
            {
                log.Error("无法打开文本编辑器：" + error);
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
