using System.Linq;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace FluffySpectre.UnityEventGraph
{
    public class UnityEventEdge : Edge
    {
        private bool _isVisible = true;
        private Label _parameterLabel;
        private bool _labelPositionDirty = false;
        private bool _hasParameter = false;

        public static UnityEventEdge CreateEdge(EdgeData edgeData)
        {
            var sourcePort = edgeData.Source.outputContainer.Children()
                .OfType<UnityEventPort>()
                .FirstOrDefault(port => port.FullPortName == edgeData.SourcePortName);

            var targetPort = edgeData.Target.inputContainer.Children()
                .OfType<UnityEventPort>()
                .FirstOrDefault(port => port.FullPortName == edgeData.TargetPortName);

            if (sourcePort == null || targetPort == null)
            {
                Debug.LogWarning($"[EventGraph] Unable to connect ports: {edgeData.SourcePortName} -> {edgeData.TargetPortName}");
                return null;
            }

            var edge = new UnityEventEdge
            {
                output = sourcePort,
                input = targetPort,
            };

            edge.pickingMode = PickingMode.Ignore;
            edge.capabilities &= ~Capabilities.Deletable;

            // Add parameter label only if there are parameters
            if (!string.IsNullOrEmpty(edgeData.ParameterValue))
            {
                edge._hasParameter = true;
                edge._parameterLabel = new Label(edgeData.ParameterValue)
                {
                    style =
                    {
                        position = Position.Absolute,
                        unityTextAlign = TextAnchor.MiddleLeft,
                        backgroundColor = new Color(0.25f, 0.25f, 0.25f, 0.75f),
                        color = Color.white,
                        borderBottomLeftRadius = 5,
                        borderBottomRightRadius = 5,
                        borderTopLeftRadius = 5,
                        borderTopRightRadius = 5,
                        paddingLeft = 5,
                        paddingRight = 5,
                        paddingTop = 5,
                        paddingBottom = 5,
                    },
                    pickingMode = PickingMode.Ignore,
                };

                edge.Add(edge._parameterLabel);
                edge._labelPositionDirty = true;
                edge.RegisterNodePositionChangeListeners();
            }

            sourcePort.Connect(edge);
            targetPort.Connect(edge);

            return edge;
        }

        private void RegisterNodePositionChangeListeners()
        {
            // Use a flag instead of immediate update
            if (output?.node is Node sourceNode)
            {
                sourceNode.RegisterCallback<GeometryChangedEvent>(_ => _labelPositionDirty = true);
            }

            if (input?.node is Node targetNode)
            {
                targetNode.RegisterCallback<GeometryChangedEvent>(_ => _labelPositionDirty = true);
            }

            // Schedule periodic position update instead of every frame
            schedule.Execute(UpdateParameterLabelPositionIfNeeded).Every(100);
        }

        private void UpdateParameterLabelPositionIfNeeded()
        {
            if (_labelPositionDirty && _isVisible && _hasParameter)
            {
                _labelPositionDirty = false;
                UpdateParameterLabelPosition();
            }
        }

        public void SetParameterValue(string value)
        {
            if (_parameterLabel == null)
            {
                if (string.IsNullOrEmpty(value))
                    return;

                _hasParameter = true;
                _parameterLabel = new Label(value)
                {
                    style =
                    {
                        position = Position.Absolute,
                        unityTextAlign = TextAnchor.MiddleLeft,
                        backgroundColor = new Color(0.25f, 0.25f, 0.25f, 0.75f),
                        color = Color.white,
                        borderBottomLeftRadius = 5,
                        borderBottomRightRadius = 5,
                        borderTopLeftRadius = 5,
                        borderTopRightRadius = 5,
                        paddingLeft = 5,
                        paddingRight = 5,
                        paddingTop = 5,
                        paddingBottom = 5,
                    },
                    pickingMode = PickingMode.Ignore,
                };
                Add(_parameterLabel);
                RegisterNodePositionChangeListeners();
                _labelPositionDirty = true;
            }
            else if (!string.IsNullOrEmpty(value))
            {
                _parameterLabel.text = value;
                _parameterLabel.style.display = DisplayStyle.Flex;
                _labelPositionDirty = true;
            }
            else
            {
                _parameterLabel.style.display = DisplayStyle.None;
            }
        }

        private void UpdateParameterLabelPosition()
        {
            if (_parameterLabel == null || output == null || input == null)
                return;

            var graphView = output.node?.GetFirstAncestorOfType<GraphView>();
            if (graphView == null)
                return;

            Vector2 sourcePosition = output.worldBound.center;
            Vector2 targetPosition = input.worldBound.center;

            Vector2 localSourcePosition = graphView.contentViewContainer.WorldToLocal(sourcePosition);
            Vector2 localTargetPosition = graphView.contentViewContainer.WorldToLocal(targetPosition);

            Vector2 midPoint = (localSourcePosition + localTargetPosition) * 0.5f;

            float labelWidth = _parameterLabel.resolvedStyle.width;
            float labelHeight = _parameterLabel.resolvedStyle.height;

            if (labelWidth <= 0) labelWidth = 50f;
            if (labelHeight <= 0) labelHeight = 20f;

            _parameterLabel.style.left = midPoint.x - (labelWidth * 0.5f);
            _parameterLabel.style.top = midPoint.y - (labelHeight * 0.5f);
        }

        public void SetVisibility(bool visible)
        {
            if (_isVisible == visible) return;
            
            _isVisible = visible;
            style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

            if (_parameterLabel != null)
            {
                _parameterLabel.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        public bool IsVisible() => _isVisible;
    }
}
