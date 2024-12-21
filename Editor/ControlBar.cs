using System;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace FluffySpectre.UnityEventGraph
{
    public class ControlBar : Toolbar
    {
        public Action OnSaveGraph;
        public Action OnRefreshGraphView;
        public Action OnRecord;
        public Action OnTogglePanel;
        public Action<LayoutStrategyType> OnLayoutStrategyChanged;
        public Action<bool> OnFilterChanged;
        public Action<bool> OnReferenceSearchChanged;

        private ToolbarButton _recordButton;
        private ToolbarButton _togglePanelButton;
        private ToolbarToggle _invokedNodesToggle;
        private Toggle _referenceSearchToggle;
        private bool _isRecording;

        public ControlBar()
        {
            var saveButton = new ToolbarButton(OnSaveGraphClick)
            {
                text = "Save",
                style = { alignSelf = Align.Center }
            };
            Add(saveButton);

            var refreshButton = new ToolbarButton(OnRefreshGraphViewClick)
            {
                text = "Rebuild",
                style = { alignSelf = Align.Center }
            };
            Add(refreshButton);

            var layoutStrategyMenu = new ToolbarMenu()
            {
                text = "Layout",
                style = { alignSelf = Align.Center }
            };
            var layoutStrategies = Enum.GetValues(typeof(LayoutStrategyType));
            foreach (var layoutStrategy in layoutStrategies)
            {
                layoutStrategyMenu.menu.AppendAction(layoutStrategy.ToString(), (a) => { OnLayoutStrategyChanged?.Invoke((LayoutStrategyType)layoutStrategy); });
            }
            Add(layoutStrategyMenu);

            _invokedNodesToggle = new ToolbarToggle
            {
                text = "Show Invoked Nodes Only"
            };
            _invokedNodesToggle.RegisterValueChangedCallback(evt => OnFilterChanged?.Invoke(evt.newValue));
            Add(_invokedNodesToggle);

            _referenceSearchToggle = new ToolbarToggle()
            {
                text = "Show Upstream References",
                value = false
            };
            _referenceSearchToggle.RegisterValueChangedCallback(evt =>
            {
                OnReferenceSearchChanged?.Invoke(evt.newValue);
            });
            Add(_referenceSearchToggle);

            var rightAlignedButtons = new Toolbar();
            rightAlignedButtons.style.flexGrow = 1;
            rightAlignedButtons.style.flexDirection = FlexDirection.RowReverse;
            rightAlignedButtons.style.alignItems = Align.Center;
            Add(rightAlignedButtons);

            _togglePanelButton = new ToolbarButton()
            {
                text = "Event Log",
                style = { alignSelf = Align.Center }
            };
            _togglePanelButton.clicked += () => OnTogglePanel?.Invoke();
            rightAlignedButtons.Add(_togglePanelButton);

            _recordButton = new ToolbarButton(OnRecordClick)
            {
                text = "Start Recording",
                style = { alignSelf = Align.Center }
            };
            rightAlignedButtons.Add(_recordButton);
        }

        public void SetRecording(bool isRecording)
        {
            _isRecording = isRecording;
            _recordButton.text = _isRecording ? "Recording..." : "Start Recording";
        }

        public void SetShowInvokedNodesOnly(bool showInvokedNodesOnly)
        {
            _invokedNodesToggle.value = showInvokedNodesOnly;
        }

        public void SetReferenceSearch(bool isEnabled)
        {
            _referenceSearchToggle.SetValueWithoutNotify(isEnabled);
        }

        private void OnSaveGraphClick()
        {
            OnSaveGraph?.Invoke();
        }

        private void OnRefreshGraphViewClick()
        {
            OnRefreshGraphView?.Invoke();
        }

        private void OnRecordClick()
        {
            OnRecord?.Invoke();
        }
    }
}
