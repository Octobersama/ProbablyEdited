using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ProbablyEdited
{
    public static class NativeFontSupport
    {
        private const string KoreanGlyphProbe = "\uD55C\uAD6D\uC5B4";

        private static readonly Dictionary<TMP_FontAsset, TMP_FontAsset> fontCopies = new Dictionary<TMP_FontAsset, TMP_FontAsset>();
        private static readonly Dictionary<TMP_FontAsset, TMP_FontAsset> copySources = new Dictionary<TMP_FontAsset, TMP_FontAsset>();
        private static readonly List<TMP_Text> textBuffer = new List<TMP_Text>(32);
        private static readonly List<TMP_Dropdown> dropdownBuffer = new List<TMP_Dropdown>(4);
        private static TMP_FontAsset koreanFallback;
        private static bool koreanFallbackWarningReported;

        public static void Apply(CustomUIWindow window)
        {
            if (window != null)
            {
                Apply(window.Rect);
            }
        }

        public static void Apply(CustomUIElement element)
        {
            if (element != null)
            {
                Apply(element.Rect);
            }
        }

        public static void Release()
        {
            foreach (TMP_FontAsset copy in fontCopies.Values)
            {
                if (copy != null)
                {
                    Object.Destroy(copy);
                }
            }

            fontCopies.Clear();
            copySources.Clear();
            koreanFallback = null;
            textBuffer.Clear();
            dropdownBuffer.Clear();
        }

        private static void Apply(RectTransform root)
        {
            if (root == null)
            {
                return;
            }

            TMP_FontAsset fallback = GetKoreanFallback();
            if (fallback == null)
            {
                return;
            }

            ApplyTextChildren(root, fallback);

            dropdownBuffer.Clear();
            try
            {
                root.GetComponentsInChildren(true, dropdownBuffer);
                for (int i = 0; i < dropdownBuffer.Count; i++)
                {
                    ApplyDropdown(dropdownBuffer[i], fallback);
                }
            }
            finally
            {
                dropdownBuffer.Clear();
            }
        }

        private static TMP_FontAsset GetKoreanFallback()
        {
            if (koreanFallback != null)
            {
                return koreanFallback;
            }

            TMP_FontAsset candidate = ScriptFontSwap.FontForText(KoreanGlyphProbe);
            if (candidate == null)
            {
                WarnKoreanFallbackUnavailable("the game Korean script font is not available.");
                return null;
            }

            if (!CanSupplyGlyph(candidate, '\uD55C')
                || !CanSupplyGlyph(candidate, '\uAD6D')
                || !CanSupplyGlyph(candidate, '\uC5B4'))
            {
                WarnKoreanFallbackUnavailable("the game Korean font cannot supply U+D55C, U+AD6D, and U+C5B4 from its atlas or dynamic source.");
                return null;
            }

            koreanFallback = candidate;
            return koreanFallback;
        }

        private static bool CanSupplyGlyph(TMP_FontAsset font, char character)
        {
            if (font.HasCharacter(character, false, false))
            {
                return true;
            }

            return font.atlasPopulationMode == AtlasPopulationMode.Dynamic
                && font.sourceFontFile != null
                && font.sourceFontFile.HasCharacter(character);
        }

        private static void WarnKoreanFallbackUnavailable(string reason)
        {
            if (koreanFallbackWarningReported)
            {
                return;
            }

            koreanFallbackWarningReported = true;
            Debug.LogWarning("[ProbablyEdited] Korean UI font fallback unavailable: " + reason);
        }

        private static void ApplyTextChildren(Transform root, TMP_FontAsset fallback)
        {
            if (root == null)
            {
                return;
            }

            textBuffer.Clear();
            try
            {
                root.GetComponentsInChildren(true, textBuffer);
                for (int i = 0; i < textBuffer.Count; i++)
                {
                    ApplyText(textBuffer[i], fallback);
                }
            }
            finally
            {
                textBuffer.Clear();
            }
        }

        private static void ApplyDropdown(TMP_Dropdown dropdown, TMP_FontAsset fallback)
        {
            if (dropdown == null)
            {
                return;
            }

            ApplyText(dropdown.captionText, fallback);
            if (!HasKoreanOption(dropdown))
            {
                return;
            }

            ApplyText(dropdown.itemText, fallback);
            ApplyTextChildren(dropdown.template, fallback);

            TMP_FontAsset itemPrimary = GetPrimaryFont(dropdown.itemText != null ? dropdown.itemText.font : null);
            NativeFontDropdownPostProcessor postProcessor = dropdown.GetComponent<NativeFontDropdownPostProcessor>();
            if (postProcessor == null)
            {
                postProcessor = dropdown.gameObject.AddComponent<NativeFontDropdownPostProcessor>();
            }

            postProcessor.Configure(dropdown, itemPrimary);
        }

        internal static void ApplyExpandedDropdown(TMP_Dropdown dropdown, TMP_FontAsset itemPrimary)
        {
            if (dropdown == null || !dropdown.IsExpanded)
            {
                return;
            }

            Transform root = dropdown.template != null ? dropdown.template.parent : dropdown.transform;
            ApplyHangulTextChildren(root, itemPrimary);
        }

        private static void ApplyHangulTextChildren(Transform root, TMP_FontAsset itemPrimary)
        {
            if (root == null)
            {
                return;
            }

            textBuffer.Clear();
            try
            {
                root.GetComponentsInChildren(true, textBuffer);
                for (int i = 0; i < textBuffer.Count; i++)
                {
                    TMP_Text text = textBuffer[i];
                    if (text == null || !ContainsHangul(text.text))
                    {
                        continue;
                    }

                    TMP_FontAsset scriptFont = ScriptFontSwap.FontForText(text.text);
                    if (scriptFont == null)
                    {
                        continue;
                    }

                    TMP_FontAsset primary = itemPrimary != null ? itemPrimary : GetPrimaryFont(text.font);
                    TMP_FontAsset copy = GetFontWithFallback(primary, scriptFont);
                    if (copy != null && text.font != copy)
                    {
                        text.font = copy;
                    }
                }
            }
            finally
            {
                textBuffer.Clear();
            }
        }

        private static TMP_FontAsset GetPrimaryFont(TMP_FontAsset font)
        {
            TMP_FontAsset source;
            return font != null && copySources.TryGetValue(font, out source) ? source : font;
        }

        private static bool HasKoreanOption(TMP_Dropdown dropdown)
        {
            List<TMP_Dropdown.OptionData> options = dropdown.options;
            if (options == null)
            {
                return false;
            }

            for (int i = 0; i < options.Count; i++)
            {
                TMP_Dropdown.OptionData option = options[i];
                if (option != null && ContainsHangul(option.text))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsHangul(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                if ((character >= '\u1100' && character <= '\u11FF')
                    || (character >= '\u3130' && character <= '\u318F')
                    || (character >= '\uA960' && character <= '\uA97F')
                    || (character >= '\uAC00' && character <= '\uD7FF'))
                {
                    return true;
                }
            }

            return false;
        }

        private static void ApplyText(TMP_Text text, TMP_FontAsset fallback)
        {
            if (text == null || fallback == null)
            {
                return;
            }

            TMP_FontAsset copy = GetFontWithFallback(text.font, fallback);
            if (copy != null && text.font != copy)
            {
                text.font = copy;
            }
        }

        private static TMP_FontAsset GetFontWithFallback(TMP_FontAsset primary, TMP_FontAsset fallback)
        {
            if (primary == null || fallback == null)
            {
                return null;
            }

            if (primary == fallback || copySources.ContainsKey(primary))
            {
                return primary;
            }

            List<TMP_FontAsset> existingFallbacks = primary.fallbackFontAssetTable;
            if (existingFallbacks != null && existingFallbacks.Contains(fallback))
            {
                return primary;
            }

            TMP_FontAsset copy;
            if (!fontCopies.TryGetValue(primary, out copy) || copy == null)
            {
                copy = Object.Instantiate(primary);
                if (copy == null)
                {
                    return null;
                }

                copy.hideFlags |= HideFlags.DontSave;
                copy.atlasPopulationMode = AtlasPopulationMode.Static;
                copy.fallbackFontAssetTable = new List<TMP_FontAsset>(2)
                {
                    primary,
                    fallback
                };

                fontCopies[primary] = copy;
                copySources[copy] = primary;
            }

            return copy;
        }
    }
    internal sealed class NativeFontDropdownPostProcessor : MonoBehaviour, IPointerClickHandler, ISubmitHandler
    {
        private TMP_Dropdown dropdown;
        private TMP_FontAsset itemPrimary;
        private Coroutine pendingApply;

        internal void Configure(TMP_Dropdown dropdown, TMP_FontAsset itemPrimary)
        {
            this.dropdown = dropdown;
            this.itemPrimary = itemPrimary;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            ScheduleApply();
        }

        public void OnSubmit(BaseEventData eventData)
        {
            ScheduleApply();
        }

        private void ScheduleApply()
        {
            if (pendingApply != null)
            {
                StopCoroutine(pendingApply);
            }

            // TMP normally creates the options before this handler; also reapply after native Start callbacks.
            NativeFontSupport.ApplyExpandedDropdown(dropdown, itemPrimary);
            pendingApply = StartCoroutine(ApplyAfterOpen());
        }

        private IEnumerator ApplyAfterOpen()
        {
            yield return null;
            pendingApply = null;
            NativeFontSupport.ApplyExpandedDropdown(dropdown, itemPrimary);
        }

        private void OnDisable()
        {
            if (pendingApply != null)
            {
                StopCoroutine(pendingApply);
                pendingApply = null;
            }
        }
    }
}
