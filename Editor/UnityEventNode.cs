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
        private Dictionary<UnityEventBase, UnityEventPort> _unityEventToPort = new();

        private readonly Color _startNodeColor = new(193f / 255f, 83f / 255f, 32f / 255f);
        private readonly Color _endNodeColor = new(56f / 255f, 111f / 255f, 164f / 255f);
        private readonly Color _defaultNodeColor = new(0.2f, 0.2f, 0.2f);

        private bool _isVisible = true;
        private bool _isHighDetail = true;
        private List<VisualElement> _optionalElements = new List<VisualElement>();

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
        
        public void SetDetailLevel(bool highDetail)
        {
            if (_isHighDetail == highDetail)
                return;
                
            _isHighDetail = highDetail;
            
            // Toggle visibility of optional UI elements based on detail level
            foreach (var element in _optionalElements)
            {
                element.style.display = highDetail ? DisplayStyle.Flex : DisplayStyle.None;
            }
            
            // In low detail mode, collapse the node
            expanded = highDetail;
            
            // Simplify ports in low detail mode
            foreach (var port in inputContainer.Children().OfType<UnityEventPort>())
            {
                UpdatePortDetailLevel(port);
            }
            
            foreach (var port in outputContainer.Children().OfType<UnityEventPort>())
            {
                UpdatePortDetailLevel(port);
            }
        }
        
        private void UpdatePortDetailLevel(UnityEventPort port)
        {
            // In low detail mode, simplify port labels
            port.portName = _isHighDetail ? port.FullPortName : GetSimplifiedPortName(port.FullPortName);
            
            // Toggle visibility of port labels in low detail mode
            foreach (var label in port.Query<Label>().ToList())
            {
                if (label.name == "invocationCountLabel")
                {
                    label.style.display = _isHighDetail ? DisplayStyle.Flex : DisplayStyle.None;
                }
            }
        }
        
        private string GetSimplifiedPortName(string fullPortName)
        {
            // Only show method name in low detail mode
            var parts = fullPortName.Split('.');
            return parts.Length > 0 ? parts[parts.Length - 1] : fullPortName;
        }

        public void AddInputPort(string portName, Component component = null)
        {
            bool portExists = inputContainer.Children()
                .OfType<UnityEventPort>()
                .Any(port => port.FullPortName == portName);

            if (!portExists)
            {
                var inputPort = UnityEventInputPort.Create(Orientation.Horizontal, Direction.Input, Port.Capacity.Single, typeof(UnityEngine.Object), component);
                
                inputPort.FullPortName = portName;
                inputPort.portName = _isHighDetail ? GetPortDisplayName(portName) : GetSimplifiedPortName(portName);
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
                .OfType<UnityEventPort>()
                .Any(port => port.FullPortName == portName);

            if (!portExists)
            {
                var outputPort = UnityEventPort.Create(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(UnityEngine.Object));
                
                outputPort.FullPortName = portName;
                outputPort.portName = _isHighDetail ? GetPortDisplayName(portName) : GetSimplifiedPortName(portName);
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
            UpdateNodeColor();
        }

        public bool TryGetPortForUnityEvent(UnityEventBase unityEvent, out UnityEventPort port)
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
                        style = { color = Color.yellow },
                        name = "invocationCountLabel"
                    };
                    port.contentContainer.Add(label);
                    _optionalElements.Add(label);
                }
                label.text = $"Calls: {eventData.InvocationCount}";
                
                // Set display based on current detail level
                label.style.display = _isHighDetail ? DisplayStyle.Flex : DisplayStyle.None;
            }
            else
            {
                // Remove label if there is no data
                var label = port.contentContainer.Q<Label>("invocationCountLabel");
                if (label != null)
                {
                    port.contentContainer.Remove(label);
                    _optionalElements.Remove(label);
                }
            }
        }

        private void UpdateEdgeLabels(Port port, UnityEventBase unityEvent)
        {
            // Update parameter labels of connected UnityEventEdges
            var eventData = EventTracker.GetEventData(unityEvent);
            if (eventData != null && eventData.Invocations.Count > 0)
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

        private string GetPortDisplayName(string portName)
        {
            // Strip the name of the GameObject from the port name
            if (portName.Count(c => c == '.') >= 2)
            {
                return portName.Substring(portName.IndexOf('.') + 1);
            }

            return portName;
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
