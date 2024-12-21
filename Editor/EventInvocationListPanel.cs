using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace FluffySpectre.UnityEventGraph
{
    public class EventInvocationListPanel : VisualElement
    {
        public Action<EventData, bool> OnEventButtonClicked { get; set; }
        public Action OnClearList { get; set; }

        private ScrollView _scrollView;
        private ToolbarButton _prevButton;
        private ToolbarButton _nextButton;
        private Label _emptyListLabel;
        private List<EventData> _events = new();
        private List<Button> _eventButtons = new();
        private int _currentIndex = -1;
        private bool _isPanelVisible = false;
        private bool _jumpToNodeEnabled = false;

        public EventInvocationListPanel()
        {
            style.flexGrow = 0;
            style.flexShrink = 0;
            style.width = 300;
            style.backgroundColor = new StyleColor(new Color(0.2f, 0.2f, 0.2f));

            var navigationContainer = new VisualElement();
            navigationContainer.style.flexDirection = FlexDirection.Row;

            _prevButton = new ToolbarButton(() => Navigate(-1)) { text = "<", style = { width = 40, unityTextAlign = TextAnchor.MiddleCenter } };
            _nextButton = new ToolbarButton(() => Navigate(1)) { text = ">", style = { width = 40, unityTextAlign = TextAnchor.MiddleCenter } };

            navigationContainer.Add(_prevButton);
            navigationContainer.Add(_nextButton);

            var jumpToNodeButton = new ToolbarToggle
            {
                text = "Jump To Node"
            };
            jumpToNodeButton.RegisterValueChangedCallback(evt => OnJumpToNodeChanged(evt.newValue));
            navigationContainer.Add(jumpToNodeButton);

            var clearLogButton = new ToolbarButton(() => OnClearList?.Invoke()) { text = "Clear", style = { unityTextAlign = TextAnchor.MiddleCenter } };
            navigationContainer.Add(clearLogButton);

            Add(navigationContainer);

            _emptyListLabel = new Label("No events recorded yet...")
            {
                style = { unityTextAlign = TextAnchor.MiddleCenter, marginTop = 20, marginBottom = 20, display = DisplayStyle.None }
            };
            Add(_emptyListLabel);

            _scrollView = new ScrollView()
            {
                style = { flexGrow = 1, marginTop = 5 }
            };
            Add(_scrollView);

            // Keyboard navigation
            RegisterCallback<KeyDownEvent>(OnKeyDown);

            UpdateNavigationButtons();
        }

        private void OnJumpToNodeChanged(bool newValue)
        {
            _jumpToNodeEnabled = newValue;
        }

        private void Navigate(int direction)
        {
            if (_events.Count == 0)
                return;

            _currentIndex += direction;

            if (_currentIndex < 0)
                _currentIndex = 0;
            if (_currentIndex >= _events.Count)
                _currentIndex = _events.Count - 1;

            ScrollToEvent(_currentIndex);
            UpdateNavigationButtons();
        }

        private void ScrollToEvent(int index)
        {
            if (!_isPanelVisible)
            {
                return;
            }

            foreach (var btn in _eventButtons)
            {
                btn.style.backgroundColor = Color.clear;
            }

            if (index >= 0 && index < _eventButtons.Count)
            {
                var button = _eventButtons[index];
                if (button != null)
                {
                    button.style.backgroundColor = new StyleColor(Color.grey);
                    button.Focus();

                    OnEventButtonClicked?.Invoke(_events[index], _jumpToNodeEnabled);

                    // Schedule the ScrollTo call to ensure proper layout update
                    schedule.Execute(() =>
                    {
                        if (button.parent != null)
                        {
                            _scrollView.ScrollTo(button);
                        }
                    }).ExecuteLater(50);
                }
            }
        }

        private void UpdateNavigationButtons()
        {
            _prevButton.SetEnabled(_currentIndex > 0);
            _nextButton.SetEnabled(_currentIndex < _events.Count - 1);
        }

        public void AddEvent(EventData eventData)
        {
            _events.Add(eventData);
            _currentIndex = _events.Count - 1;

            // Format the parameters as a string
            var parametersString = eventData.parameterValues != null 
                ? string.Join(", ", eventData.parameterValues.Select(p => p?.ToString() ?? "null"))
                : "";

            var eventButton = new Button
            {
                text = $"{eventData.name}({parametersString})",
                style =
                {
                    unityTextAlign = TextAnchor.MiddleLeft,
                    whiteSpace = WhiteSpace.Normal
                },
            };
            eventButton.clicked += () =>
            {
                _currentIndex = _events.IndexOf(eventData);
                ScrollToEvent(_currentIndex);
                UpdateNavigationButtons();
            };
            _scrollView.Add(eventButton);
            _eventButtons.Add(eventButton);

            ScrollToEvent(_currentIndex);

            UpdateEmptyListLabel();
            UpdateNavigationButtons();
        }

        public void ClearList()
        {
            _events.Clear();
            _currentIndex = -1;
            _scrollView.Clear();
            _eventButtons.Clear();

            UpdateEmptyListLabel();
            UpdateNavigationButtons();
        }

        public void SetVisible(bool visible)
        {
            _isPanelVisible = visible;
            style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

            if (visible && _events.Count > 0)
            {
                ScrollToEvent(_currentIndex);
            }
        }

        private void UpdateEmptyListLabel()
        {
            _emptyListLabel.style.display = _events.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (!_isPanelVisible)
                return;

            switch (evt.keyCode)
            {
                case KeyCode.UpArrow:
                case KeyCode.LeftArrow:
                    Navigate(-1);
                    evt.StopPropagation();
                    break;
                case KeyCode.DownArrow:
                case KeyCode.RightArrow:
                    Navigate(1);
                    evt.StopPropagation();
                    break;
            }
        }
    }
}
