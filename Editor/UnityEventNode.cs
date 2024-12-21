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

        private Dictionary<string, UnityEventBase> _outputPorts = new();
        private Dictionary<UnityEventBase, Port> _unityEventToPort = new();

        private readonly Color _startNodeColor = new(193f / 255f, 83f / 255f, 32f / 255f);
        private readonly Color _endNodeColor = new(56f / 255f, 111f / 255f, 164f / 255f);
        private readonly Color _defaultNodeColor = new(0.2f, 0.2f, 0.2f);

        private bool _isVisible = true;

        public UnityEventNode(string title, GameObject representedObject)
        {
            this.title = title;
            RepresentedObject = representedObject;

            capabilities &= ~Capabilities.Deletable;
            capabilities |= Capabilities.Selectable;

            RefreshExpandedState();
            RefreshPorts();
        }

        public void SetVisibility(bool visible)
        {
            _isVisible = visible;
            style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public bool IsVisible()
        {
            return _isVisible;
        }

        public void AddInputPort(string componentName, string methodName, Component component = null)
        {
            string portName = $"{componentName}.{methodName}";

            bool portExists = inputContainer.Children()
                .OfType<Port>()
                .Any(port => port.portName == portName);

            if (!portExists)
            {
                var inputPort = UnityEventPort.Create(Orientation.Horizontal, Direction.Input, Port.Capacity.Single, typeof(UnityEngine.Object), component);
                
                inputPort.portName = portName;
                inputPort.pickingMode = PickingMode.Ignore;

                inputContainer.Add(inputPort);

                UpdateNodeColor();

                RefreshExpandedState();
                RefreshPorts();
            }
        }

        public void AddOutputPort(string portName, UnityEventBase unityEvent)
        {
            bool portExists = outputContainer.Children()
                .OfType<Port>()
                .Any(port => port.portName == portName);

            if (!portExists)
            {
                var outputPort = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(UnityEngine.Object));
                outputPort.portName = portName;
                outputPort.pickingMode = PickingMode.Ignore;
                outputContainer.Add(outputPort);

                _outputPorts[portName] = unityEvent;

                UpdatePortLabel(outputPort, unityEvent);
                UpdateNodeColor();

                RefreshExpandedState();
                RefreshPorts();

                _unityEventToPort[unityEvent] = outputPort;
            }
        }

        public bool HasPorts()
        {
            return inputContainer.childCount > 0 || outputContainer.childCount > 0;
        }

        public IEnumerable<UnityEventBase> GetUnityEvents()
        {
            return _outputPorts.Values;
        }

        public void UpdateInvocationData()
        {
            foreach (var port in outputContainer.Children().OfType<Port>())
            {
                if (_outputPorts.TryGetValue(port.portName, out var unityEvent))
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
            UpdateNodeColor();
        }

        public bool TryGetPortForUnityEvent(UnityEventBase unityEvent, out Port port)
        {
            return _unityEventToPort.TryGetValue(unityEvent, out port);
        }

        private void UpdatePortLabel(Port port, UnityEventBase unityEvent)
        {
            var eventData = EventTracker.GetEventData(unityEvent);
            if (eventData != null)
            {
                var label = port.contentContainer.Q<Label>("invocationCountLabel");
                if (label == null)
                {
                    label = new Label
                    {
                        style = { color = Color.yellow }
                    };
                    label.name = "invocationCountLabel";
                    port.contentContainer.Add(label);
                }
                label.text = $"Calls: {eventData.InvocationCount}";
            }
            else
            {
                // Remove label if there is no data
                var label = port.contentContainer.Q<Label>("invocationCountLabel");
                if (label != null)
                {
                    port.contentContainer.Remove(label);
                }
            }
        }

        private void UpdateEdgeLabels(Port port, UnityEventBase unityEvent)
        {
            // Update parameter labels of connected UnityEventEdges
            var eventData = EventTracker.GetEventData(unityEvent);
            if (eventData != null)
            {
                foreach (var edge in port.connections.OfType<UnityEventEdge>())
                {
                    var parameterValues = eventData.Invocations.Last().parameterValues;

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
        }

        private void UpdateNodeColor()
        {
            titleContainer.style.backgroundColor = new StyleColor(GetNodeColor());
        }

        private Color GetNodeColor()
        {
            // Start node
            if (inputContainer.childCount == 0)
            {
                return _startNodeColor;
            }

            // End node
            if (outputContainer.childCount == 0)
            {
                return _endNodeColor;
            }

            return _defaultNodeColor;
        }
    }
}
