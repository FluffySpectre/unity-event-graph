using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UIElements;

namespace FluffySpectre.UnityEventGraph
{
    public class UnityEventNode : Node
    {
        public GameObject RepresentedObject { get; }

        private readonly Dictionary<string, UnityEventBase> _outputPorts = new();
        private readonly Dictionary<UnityEventBase, UnityEventPort> _unityEventToPort = new();
        private readonly HashSet<string> _inputPortNames = new();
        private readonly HashSet<string> _outputPortNames = new();

        private static readonly Color StartNodeColor = new(193f / 255f, 83f / 255f, 32f / 255f);
        private static readonly Color EndNodeColor = new(56f / 255f, 111f / 255f, 164f / 255f);
        private static readonly Color DefaultNodeColor = new(0.2f, 0.2f, 0.2f);

        private bool _isVisible = true;
        private Color _currentColor;

        public UnityEventNode(string title, GameObject representedObject)
        {
            this.title = title;
            RepresentedObject = representedObject;

            capabilities &= ~Capabilities.Deletable;
            capabilities |= Capabilities.Selectable;

            _currentColor = DefaultNodeColor;
            
            RefreshExpandedState();
            RefreshPorts();
        }

        public void SetVisibility(bool visible)
        {
            if (_isVisible == visible) return;
            
            _isVisible = visible;
            style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public bool IsVisible() => _isVisible;

        public void AddInputPort(string portName, Component component = null)
        {
            if (_inputPortNames.Contains(portName))
                return;

            _inputPortNames.Add(portName);

            var inputPort = UnityEventInputPort.Create(
                Orientation.Horizontal, 
                Direction.Input, 
                Port.Capacity.Single, 
                typeof(UnityEngine.Object), 
                component
            );

            inputPort.FullPortName = portName;
            inputPort.portName = GetPortDisplayName(portName);
            inputPort.pickingMode = PickingMode.Ignore;

            inputContainer.Add(inputPort);

            UpdateNodeColor();
            RefreshExpandedState();
            RefreshPorts();
        }

        public void AddOutputPort(string portName, UnityEventBase unityEvent)
        {
            if (_outputPortNames.Contains(portName))
                return;

            _outputPortNames.Add(portName);

            var outputPort = UnityEventPort.Create(
                Orientation.Horizontal, 
                Direction.Output, 
                Port.Capacity.Multi, 
                typeof(UnityEngine.Object)
            );

            outputPort.FullPortName = portName;
            outputPort.portName = GetPortDisplayName(portName);
            outputPort.pickingMode = PickingMode.Ignore;
            outputContainer.Add(outputPort);

            _outputPorts[portName] = unityEvent;
            _unityEventToPort[unityEvent] = outputPort;

            UpdatePortLabel(outputPort, unityEvent);
            UpdateNodeColor();

            RefreshExpandedState();
            RefreshPorts();
        }

        public bool HasPorts() => _inputPortNames.Count > 0 || _outputPortNames.Count > 0;

        public IEnumerable<UnityEventBase> GetUnityEvents() => _outputPorts.Values;

        public void UpdateInvocationData()
        {
            foreach (var port in outputContainer.Children().OfType<UnityEventPort>())
            {
                if (_outputPorts.TryGetValue(port.FullPortName, out var unityEvent))
                {
                    UpdatePortLabel(port, unityEvent);
                    UpdateEdgeLabels(port, unityEvent);
                }
            }
        }

        public override void OnSelected()
        {
            base.OnSelected();
            if (RepresentedObject != null)
            {
                Selection.activeGameObject = RepresentedObject;
            }
        }

        public void Highlight()
        {
            titleContainer.style.backgroundColor = Color.yellow;
        }

        public void ResetHighlight()
        {
            titleContainer.style.backgroundColor = new StyleColor(_currentColor);
        }

        public bool TryGetPortForUnityEvent(UnityEventBase unityEvent, out UnityEventPort port)
        {
            return _unityEventToPort.TryGetValue(unityEvent, out port);
        }

        private void UpdatePortLabel(Port port, UnityEventBase unityEvent)
        {
            var eventData = EventTracker.GetEventData(unityEvent);
            var label = port.contentContainer.Q<Label>("invocationCountLabel");
            
            if (eventData != null)
            {
                if (label == null)
                {
                    label = new Label
                    {
                        name = "invocationCountLabel",
                        style = { color = Color.yellow }
                    };
                    port.contentContainer.Add(label);
                }
                label.text = $"Calls: {eventData.InvocationCount}";
            }
            else if (label != null)
            {
                port.contentContainer.Remove(label);
            }
        }

        private void UpdateEdgeLabels(Port port, UnityEventBase unityEvent)
        {
            var eventData = EventTracker.GetEventData(unityEvent);
            if (eventData == null || eventData.Invocations.Count == 0)
                return;

            var invocations = eventData.Invocations;
            var lastInvocation = invocations[invocations.Count - 1];
            var parameterValues = lastInvocation.parameterValues;

            foreach (var edge in port.connections.OfType<UnityEventEdge>())
            {
                if (parameterValues != null && parameterValues.Length > 0)
                {
                    edge.SetParameterValue(string.Join("\n", parameterValues));
                }
                else
                {
                    edge.SetParameterValue(null);
                }
            }
        }

        private string GetPortDisplayName(string portName)
        {
            int firstDot = portName.IndexOf('.');
            if (firstDot >= 0)
            {
                int secondDot = portName.IndexOf('.', firstDot + 1);
                if (secondDot >= 0)
                {
                    return portName.Substring(firstDot + 1);
                }
            }
            return portName;
        }

        private void UpdateNodeColor()
        {
            _currentColor = GetNodeColor();
            titleContainer.style.backgroundColor = new StyleColor(_currentColor);
        }

        private Color GetNodeColor()
        {
            if (_inputPortNames.Count == 0)
                return StartNodeColor;
            if (_outputPortNames.Count == 0)
                return EndNodeColor;
            return DefaultNodeColor;
        }
    }
}
