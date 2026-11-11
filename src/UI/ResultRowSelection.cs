using System;
using UnityEngine;
using UnityEngine.UI;

namespace ProbablyEdited
{
    /// <summary>Keeps a result row highlighted without taking focus from the text editor.</summary>
    internal sealed class ResultRowSelection
    {
        private readonly Button _button;
        private readonly ColorBlock _colors;
        private readonly SpriteState _sprites;
        private readonly Sprite _normalSprite;
        private readonly AnimationTriggers _normalTriggers;
        private readonly AnimationTriggers _selectedTriggers;
        private bool _selected;

        public ResultRowSelection(GameObject root)
        {
            _button = root.GetComponentInChildren<Button>(true);
            if (_button == null)
            {
                throw new InvalidOperationException("原生文本条目按钮不可用。");
            }

            _colors = _button.colors;
            _sprites = _button.spriteState;
            _normalSprite = _button.image != null ? _button.image.sprite : null;
            _normalTriggers = _button.animationTriggers;
            if (_button.transition == Selectable.Transition.Animation)
            {
                _selectedTriggers = new AnimationTriggers
                {
                    normalTrigger = _normalTriggers.highlightedTrigger,
                    highlightedTrigger = _normalTriggers.highlightedTrigger,
                    pressedTrigger = _normalTriggers.pressedTrigger,
                    selectedTrigger = _normalTriggers.highlightedTrigger,
                    disabledTrigger = _normalTriggers.disabledTrigger
                };
            }
        }

        public void SetSelected(bool selected)
        {
            if (_selected == selected) return;
            _selected = selected;

            switch (_button.transition)
            {
                case Selectable.Transition.ColorTint:
                    ColorBlock colors = _colors;
                    if (selected)
                    {
                        colors.normalColor = colors.highlightedColor;
                        colors.selectedColor = colors.highlightedColor;
                    }
                    _button.colors = colors;
                    break;

                case Selectable.Transition.SpriteSwap:
                    if (_button.image != null)
                    {
                        _button.image.sprite = selected && _sprites.highlightedSprite != null
                            ? _sprites.highlightedSprite : _normalSprite;
                    }
                    SpriteState sprites = _sprites;
                    if (selected) sprites.selectedSprite = sprites.highlightedSprite;
                    _button.spriteState = sprites;
                    break;

                case Selectable.Transition.Animation:
                    _button.animationTriggers = selected ? _selectedTriggers : _normalTriggers;
                    break;

                case Selectable.Transition.None:
                    if (_button.targetGraphic != null)
                    {
                        Color color = selected ? _colors.highlightedColor : _colors.normalColor;
                        _button.targetGraphic.CrossFadeColor(color * _colors.colorMultiplier, 0f, true, true);
                    }
                    break;
            }
        }
    }
}
