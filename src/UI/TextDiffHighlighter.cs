using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProbablyEdited
{
    /// <summary>Highlights deleted and added text without changing either input field's value.</summary>
    public sealed class TextDiffHighlighter : IDisposable
    {
        private static readonly Color32 RemovedBackground = new Color32(214, 69, 61, 88);
        private static readonly Color32 RemovedStrike = new Color32(214, 69, 61, 235);
        private static readonly Color32 AddedBackground = new Color32(58, 170, 92, 88);

        private readonly TMP_InputField _original;
        private readonly TMP_InputField _replacement;
        private readonly TMP_Text _originalText;
        private readonly TMP_Text _replacementText;
        private readonly TextDiffGraphic _originalBackground;
        private readonly TextDiffGraphic _originalStrike;
        private readonly TextDiffGraphic _replacementBackground;
        private readonly List<TextDiffQuad> _originalBackgroundQuads = new List<TextDiffQuad>();
        private readonly List<TextDiffQuad> _originalStrikeQuads = new List<TextDiffQuad>();
        private readonly List<TextDiffQuad> _replacementBackgroundQuads = new List<TextDiffQuad>();

        private TextDiffResult _diff;
        private string _originalValue = string.Empty;
        private string _replacementValue = string.Empty;
        private bool _hasTexts;
        private bool _disposed;

        public static TextDiffHighlighter Attach(TMP_InputField original, TMP_InputField replacement)
        {
            if (original == null)
                throw new ArgumentNullException(nameof(original));
            if (replacement == null)
                throw new ArgumentNullException(nameof(replacement));
            if (ReferenceEquals(original, replacement))
                throw new ArgumentException("Original and replacement fields must be different.", nameof(replacement));
            if (original.textComponent == null)
                throw new ArgumentException("The original field has no text component.", nameof(original));
            if (replacement.textComponent == null)
                throw new ArgumentException("The replacement field has no text component.", nameof(replacement));

            return new TextDiffHighlighter(original, replacement);
        }

        private TextDiffHighlighter(TMP_InputField original, TMP_InputField replacement)
        {
            _original = original;
            _replacement = replacement;
            _originalText = original.textComponent;
            _replacementText = replacement.textComponent;

            _originalBackground = CreateGraphic(_originalText, "Deleted text background", false);
            _originalStrike = CreateGraphic(_originalText, "Deleted text strike", true);
            _replacementBackground = CreateGraphic(_replacementText, "Added text background", false);

            _original.onValueChanged.AddListener(OnInputValueChanged);
            _replacement.onValueChanged.AddListener(OnInputValueChanged);
            _originalText.OnPreRenderText += OnOriginalTextPreRender;
            _replacementText.OnPreRenderText += OnReplacementTextPreRender;

            _originalText.ForceMeshUpdate();
            _replacementText.ForceMeshUpdate();
            SetTexts(_original.text, _replacement.text);
        }

        public void SetTexts(string original, string replacement)
        {
            ThrowIfDisposed();
            original = original ?? string.Empty;
            replacement = replacement ?? string.Empty;

            if (_hasTexts &&
                string.Equals(_originalValue, original, StringComparison.Ordinal) &&
                string.Equals(_replacementValue, replacement, StringComparison.Ordinal))
            {
                return;
            }

            _hasTexts = true;
            _originalValue = original;
            _replacementValue = replacement;
            _diff = TextDiff.Compare(original, replacement);
            RefreshOriginalGeometry(_originalText.textInfo);
            RefreshReplacementGeometry(_replacementText.textInfo);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            if (_original != null)
                _original.onValueChanged.RemoveListener(OnInputValueChanged);
            if (_replacement != null)
                _replacement.onValueChanged.RemoveListener(OnInputValueChanged);
            if (_originalText != null)
                _originalText.OnPreRenderText -= OnOriginalTextPreRender;
            if (_replacementText != null)
                _replacementText.OnPreRenderText -= OnReplacementTextPreRender;

            DestroyGraphic(_originalBackground);
            DestroyGraphic(_originalStrike);
            DestroyGraphic(_replacementBackground);
        }

        private static TextDiffGraphic CreateGraphic(TMP_Text text, string name, bool drawAfterText)
        {
            var gameObject = new GameObject(name, typeof(RectTransform));
            TextDiffGraphic graphic = gameObject.AddComponent<TextDiffGraphic>();
            graphic.SetReference(text.rectTransform, drawAfterText);
            return graphic;
        }

        private static void DestroyGraphic(TextDiffGraphic graphic)
        {
            if (graphic == null)
                return;

            graphic.enabled = false;
            UnityEngine.Object.Destroy(graphic.gameObject);
        }

        private void OnInputValueChanged(string ignored)
        {
            if (!_disposed)
                SetTexts(_original.text, _replacement.text);
        }

        private void OnOriginalTextPreRender(TMP_TextInfo textInfo)
        {
            if (!_disposed && _hasTexts)
                RefreshOriginalGeometry(textInfo);
        }

        private void OnReplacementTextPreRender(TMP_TextInfo textInfo)
        {
            if (!_disposed && _hasTexts)
                RefreshReplacementGeometry(textInfo);
        }

        private void RefreshOriginalGeometry(TMP_TextInfo textInfo)
        {
            BuildGeometry(textInfo, _originalValue, _diff.Removed, true, _originalBackgroundQuads, _originalStrikeQuads);
            _originalBackground.SetQuads(_originalBackgroundQuads, RemovedBackground);
            _originalStrike.SetQuads(_originalStrikeQuads, RemovedStrike);
        }

        private void RefreshReplacementGeometry(TMP_TextInfo textInfo)
        {
            BuildGeometry(textInfo, _replacementValue, _diff.Added, false, _replacementBackgroundQuads, null);
            _replacementBackground.SetQuads(_replacementBackgroundQuads, AddedBackground);
        }

        private static void BuildGeometry(
            TMP_TextInfo textInfo,
            string rawText,
            IReadOnlyList<TextDiffSpan> spans,
            bool strikeThrough,
            List<TextDiffQuad> backgroundQuads,
            List<TextDiffQuad> strikeQuads)
        {
            backgroundQuads.Clear();
            if (strikeQuads != null)
                strikeQuads.Clear();

            if (textInfo == null || textInfo.characterInfo == null)
                return;

            int characterCount = Math.Min(textInfo.characterCount, textInfo.characterInfo.Length);
            for (int spanIndex = 0; spanIndex < spans.Count; spanIndex++)
            {
                TextDiffSpan span = spans[spanIndex];
                int firstBackgroundQuad = backgroundQuads.Count;
                int firstStrikeQuad = strikeQuads == null ? 0 : strikeQuads.Count;

                for (int characterIndex = 0; characterIndex < characterCount; characterIndex++)
                {
                    TMP_CharacterInfo character = textInfo.characterInfo[characterIndex];
                    if (character.index < 0 || character.stringLength <= 0)
                        continue;
                    if (character.index >= span.End || character.index + character.stringLength <= span.Start)
                        continue;
                    if (ContainsLineBreak(rawText, character.index, character.index + character.stringLength))
                        continue;

                    Rect bounds = GetCharacterBounds(character, textInfo);
                    AddQuad(backgroundQuads, bounds, character.lineNumber, firstBackgroundQuad);
                    if (strikeThrough)
                    {
                        float thickness = Mathf.Max(1f, bounds.height * 0.045f);
                        float centerY = (bounds.yMin + bounds.yMax) * 0.5f;
                        var strikeBounds = Rect.MinMaxRect(
                            bounds.xMin,
                            centerY - thickness * 0.5f,
                            bounds.xMax,
                            centerY + thickness * 0.5f);
                        AddQuad(strikeQuads, strikeBounds, character.lineNumber, firstStrikeQuad);
                    }
                }

                AddLineBreakQuads(
                    textInfo,
                    rawText,
                    span,
                    backgroundQuads,
                    strikeQuads,
                    firstBackgroundQuad,
                    firstStrikeQuad,
                    strikeThrough);
            }
        }

        private static Rect GetCharacterBounds(TMP_CharacterInfo character, TMP_TextInfo textInfo)
        {
            float minX = Mathf.Min(character.origin, character.xAdvance);
            float maxX = Mathf.Max(character.origin, character.xAdvance);
            float minY = character.descender;
            float maxY = character.ascender;

            if (character.isVisible)
            {
                minX = Mathf.Min(minX, Mathf.Min(character.bottomLeft.x, character.topLeft.x));
                maxX = Mathf.Max(maxX, Mathf.Max(character.bottomRight.x, character.topRight.x));
                minY = Mathf.Min(minY, Mathf.Min(character.bottomLeft.y, character.bottomRight.y));
                maxY = Mathf.Max(maxY, Mathf.Max(character.topLeft.y, character.topRight.y));
            }

            if (maxY <= minY && character.lineNumber >= 0 && character.lineNumber < textInfo.lineCount)
            {
                TMP_LineInfo line = textInfo.lineInfo[character.lineNumber];
                minY = line.descender;
                maxY = line.ascender;
            }

            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        private static void AddLineBreakQuads(
            TMP_TextInfo textInfo,
            string rawText,
            TextDiffSpan span,
            List<TextDiffQuad> backgroundQuads,
            List<TextDiffQuad> strikeQuads,
            int firstBackgroundQuad,
            int firstStrikeQuad,
            bool strikeThrough)
        {
            int end = Math.Min(span.End, rawText.Length);
            for (int index = Math.Max(0, span.Start); index < end; index++)
            {
                char value = rawText[index];
                if (value == '\r' && index + 1 < end && rawText[index + 1] == '\n')
                    continue;
                if (value != '\r' && value != '\n')
                    continue;

                int lineNumber = FindLineNumber(textInfo, index);
                Rect bounds = GetLineBreakBounds(textInfo, lineNumber, index);
                AddQuad(backgroundQuads, bounds, lineNumber, firstBackgroundQuad);
                if (strikeThrough)
                {
                    float thickness = Mathf.Max(1f, bounds.height * 0.045f);
                    float centerY = (bounds.yMin + bounds.yMax) * 0.5f;
                    var strikeBounds = Rect.MinMaxRect(
                        bounds.xMin,
                        centerY - thickness * 0.5f,
                        bounds.xMax,
                        centerY + thickness * 0.5f);
                    AddQuad(strikeQuads, strikeBounds, lineNumber, firstStrikeQuad);
                }
            }
        }

        private static int FindLineNumber(TMP_TextInfo textInfo, int rawIndex)
        {
            int lineNumber = 0;
            int characterCount = Math.Min(textInfo.characterCount, textInfo.characterInfo.Length);
            for (int i = 0; i < characterCount; i++)
            {
                TMP_CharacterInfo character = textInfo.characterInfo[i];
                if (character.index > rawIndex)
                    break;
                lineNumber = character.lineNumber;
            }

            return textInfo.lineCount == 0 ? 0 : Mathf.Clamp(lineNumber, 0, textInfo.lineCount - 1);
        }

        private static Rect GetLineBreakBounds(TMP_TextInfo textInfo, int lineNumber, int rawIndex)
        {
            if (textInfo.lineCount == 0)
                return Rect.zero;

            TMP_LineInfo line = textInfo.lineInfo[lineNumber];
            if (line.ascender <= line.descender)
                return Rect.zero;

            float lineHeight = line.ascender - line.descender;
            float x = line.lineExtents.min.x;
            int characterCount = Math.Min(textInfo.characterCount, textInfo.characterInfo.Length);
            for (int i = 0; i < characterCount; i++)
            {
                TMP_CharacterInfo character = textInfo.characterInfo[i];
                if (character.lineNumber != lineNumber)
                    continue;
                if (character.index <= rawIndex && rawIndex < character.index + character.stringLength)
                {
                    x = Mathf.Max(character.origin, character.xAdvance);
                    break;
                }
                if (character.index < rawIndex)
                    x = Mathf.Max(x, Mathf.Max(character.origin, character.xAdvance));
            }

            float width = Mathf.Max(1f, lineHeight * 0.18f);
            return Rect.MinMaxRect(x, line.descender, x + width, line.ascender);
        }

        private static bool ContainsLineBreak(string text, int start, int end)
        {
            int boundedStart = Math.Max(0, start);
            int boundedEnd = Math.Min(text.Length, end);
            for (int i = boundedStart; i < boundedEnd; i++)
            {
                if (text[i] == '\r' || text[i] == '\n')
                    return true;
            }

            return false;
        }

        private static void AddQuad(List<TextDiffQuad> quads, Rect bounds, int lineNumber, int firstQuadOnSpan)
        {
            if (bounds.width <= 0f || bounds.height <= 0f)
                return;

            int lastIndex = quads.Count - 1;
            if (lastIndex >= firstQuadOnSpan)
            {
                TextDiffQuad previous = quads[lastIndex];
                if (previous.LineNumber == lineNumber && CanMerge(previous.Bounds, bounds))
                {
                    previous.Bounds = Rect.MinMaxRect(
                        Mathf.Min(previous.Bounds.xMin, bounds.xMin),
                        Mathf.Min(previous.Bounds.yMin, bounds.yMin),
                        Mathf.Max(previous.Bounds.xMax, bounds.xMax),
                        Mathf.Max(previous.Bounds.yMax, bounds.yMax));
                    quads[lastIndex] = previous;
                    return;
                }
            }

            quads.Add(new TextDiffQuad(bounds, lineNumber));
        }

        private static bool CanMerge(Rect first, Rect second)
        {
            const float gap = 0.5f;
            return second.xMin <= first.xMax + gap && second.xMax >= first.xMin - gap;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(TextDiffHighlighter));
        }
    }

    internal sealed class TextDiffGraphic : MaskableGraphic
    {
        private static readonly WaitForEndOfFrame EndOfFrame = new WaitForEndOfFrame();
        private Coroutine _pendingRepaint;
        private List<TextDiffQuad> _quads;
        private Rect _quadBounds;
        private bool _hasQuadBounds;
        private RectTransform _reference;
        private bool _drawAfterText;
        private Color32 _quadColor = Color.white;

        public void SetReference(RectTransform reference, bool drawAfterText)
        {
            _reference = reference;
            _drawAfterText = drawAfterText;
            raycastTarget = false;
            maskable = true;
            color = Color.white;
            SynchronizeTransform();
        }

        public void SetQuads(List<TextDiffQuad> quads, Color32 color)
        {
            _quads = quads;
            _quadColor = color;
            _hasQuadBounds = quads != null && quads.Count > 0;
            if (_hasQuadBounds)
            {
                _quadBounds = quads[0].Bounds;
                for (int i = 1; i < quads.Count; i++)
                {
                    Rect bounds = quads[i].Bounds;
                    _quadBounds = Rect.MinMaxRect(
                        Mathf.Min(_quadBounds.xMin, bounds.xMin),
                        Mathf.Min(_quadBounds.yMin, bounds.yMin),
                        Mathf.Max(_quadBounds.xMax, bounds.xMax),
                        Mathf.Max(_quadBounds.yMax, bounds.yMax));
                }
            }

            if (!isActiveAndEnabled || _pendingRepaint != null)
                return;

            if (CanvasUpdateRegistry.IsRebuildingGraphics())
            {
                _pendingRepaint = StartCoroutine(FlushPendingVertices());
                return;
            }

            SetVerticesDirty();
        }

        // TMP can scroll its RectTransform past the viewport while its glyph quads remain visible.
        // Cull by those quads; MaskableGraphic still applies the parent's clip rect.
        public override void Cull(Rect clipRect, bool validRect)
        {
            bool cull = !validRect || !_hasQuadBounds || _reference == null;
            Canvas rootCanvas = canvas != null ? canvas.rootCanvas : null;
            if (!cull && rootCanvas != null)
            {
                Matrix4x4 localToCanvas = rootCanvas.transform.worldToLocalMatrix * _reference.localToWorldMatrix;
                Vector3 bottomLeft = localToCanvas.MultiplyPoint3x4(new Vector3(_quadBounds.xMin, _quadBounds.yMin, 0f));
                Vector3 topLeft = localToCanvas.MultiplyPoint3x4(new Vector3(_quadBounds.xMin, _quadBounds.yMax, 0f));
                Vector3 topRight = localToCanvas.MultiplyPoint3x4(new Vector3(_quadBounds.xMax, _quadBounds.yMax, 0f));
                Vector3 bottomRight = localToCanvas.MultiplyPoint3x4(new Vector3(_quadBounds.xMax, _quadBounds.yMin, 0f));
                float minX = Mathf.Min(Mathf.Min(bottomLeft.x, topLeft.x), Mathf.Min(topRight.x, bottomRight.x));
                float minY = Mathf.Min(Mathf.Min(bottomLeft.y, topLeft.y), Mathf.Min(topRight.y, bottomRight.y));
                float maxX = Mathf.Max(Mathf.Max(bottomLeft.x, topLeft.x), Mathf.Max(topRight.x, bottomRight.x));
                float maxY = Mathf.Max(Mathf.Max(bottomLeft.y, topLeft.y), Mathf.Max(topRight.y, bottomRight.y));
                cull = !clipRect.Overlaps(Rect.MinMaxRect(minX, minY, maxX, maxY), true);
            }

            if (canvasRenderer.cull == cull)
                return;

            canvasRenderer.cull = cull;
            onCullStateChanged.Invoke(cull);
            OnCullingChanged();
        }

        private IEnumerator FlushPendingVertices()
        {
            // TMP pre-render callbacks can run inside CanvasUpdateRegistry's graphic pass.
            // Register the overlay rebuild only after that pass has rendered.
            yield return EndOfFrame;
            _pendingRepaint = null;

            if (isActiveAndEnabled)
                SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            if (_quads == null)
                return;

            for (int i = 0; i < _quads.Count; i++)
            {
                Rect bounds = _quads[i].Bounds;
                if (bounds.width <= 0f || bounds.height <= 0f)
                    continue;

                int firstVertex = vertexHelper.currentVertCount;
                UIVertex vertex = UIVertex.simpleVert;
                vertex.color = _quadColor;
                vertex.position = new Vector3(bounds.xMin, bounds.yMin, 0f);
                vertexHelper.AddVert(vertex);
                vertex.position = new Vector3(bounds.xMin, bounds.yMax, 0f);
                vertexHelper.AddVert(vertex);
                vertex.position = new Vector3(bounds.xMax, bounds.yMax, 0f);
                vertexHelper.AddVert(vertex);
                vertex.position = new Vector3(bounds.xMax, bounds.yMin, 0f);
                vertexHelper.AddVert(vertex);
                vertexHelper.AddTriangle(firstVertex, firstVertex + 1, firstVertex + 2);
                vertexHelper.AddTriangle(firstVertex + 2, firstVertex + 3, firstVertex);
            }
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            Canvas.willRenderCanvases += SynchronizeTransform;
        }

        protected override void OnDisable()
        {
            if (_pendingRepaint != null)
            {
                StopCoroutine(_pendingRepaint);
                _pendingRepaint = null;
            }
            Canvas.willRenderCanvases -= SynchronizeTransform;
            base.OnDisable();
        }

        private void SynchronizeTransform()
        {
            if (_reference == null)
                return;

            RectTransform overlay = rectTransform;
            if (overlay.parent != _reference.parent)
            {
                overlay.SetParent(_reference.parent, false);
                PlaceRelativeToText(overlay);
            }
            else if (_drawAfterText && overlay.GetSiblingIndex() <= _reference.GetSiblingIndex())
            {
                overlay.SetSiblingIndex(_reference.GetSiblingIndex() + 1);
            }
            else if (!_drawAfterText && overlay.GetSiblingIndex() >= _reference.GetSiblingIndex())
            {
                overlay.SetSiblingIndex(_reference.GetSiblingIndex());
            }

            if (overlay.anchorMin != _reference.anchorMin)
                overlay.anchorMin = _reference.anchorMin;
            if (overlay.anchorMax != _reference.anchorMax)
                overlay.anchorMax = _reference.anchorMax;
            if (overlay.pivot != _reference.pivot)
                overlay.pivot = _reference.pivot;
            if (overlay.sizeDelta != _reference.sizeDelta)
                overlay.sizeDelta = _reference.sizeDelta;
            if (overlay.anchoredPosition3D != _reference.anchoredPosition3D)
                overlay.anchoredPosition3D = _reference.anchoredPosition3D;
            if (overlay.localRotation != _reference.localRotation)
                overlay.localRotation = _reference.localRotation;
            if (overlay.localScale != _reference.localScale)
                overlay.localScale = _reference.localScale;
        }

        private void PlaceRelativeToText(RectTransform overlay)
        {
            int textIndex = _reference.GetSiblingIndex();
            overlay.SetSiblingIndex(_drawAfterText ? textIndex + 1 : textIndex);
        }
    }

    internal struct TextDiffQuad
    {
        public Rect Bounds;
        public int LineNumber;

        public TextDiffQuad(Rect bounds, int lineNumber)
        {
            Bounds = bounds;
            LineNumber = lineNumber;
        }
    }
}
