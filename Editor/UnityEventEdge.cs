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
                edge.UpdateParameterLabelPosition();
                edge.RegisterNodePositionChangeListeners();
            }

            sourcePort.Connect(edge);
            targetPort.Connect(edge);

            return edge;
        }

        private void RegisterNodePositionChangeListeners()
        {
            // Add listeners for both source and target nodes
            if (output.node is Node sourceNode)
            {
                sourceNode.RegisterCallback<GeometryChangedEvent>(_ => UpdateParameterLabelPosition());
            }

            if (input.node is Node targetNode)
            {
                targetNode.RegisterCallback<GeometryChangedEvent>(_ => UpdateParameterLabelPosition());
            }
        }

        public void SetParameterValue(string value)
        {
            if (_parameterLabel != null)
            {
                if (value != null)
                {
                    _parameterLabel.text = value;
                    _parameterLabel.style.display = DisplayStyle.Flex;
                }
                else
                {
                    // Hide label if there is no value
                    _parameterLabel.style.display = DisplayStyle.None;
                }
            }
        }

        private void UpdateParameterLabelPosition()
        {
            if (_parameterLabel != null && output != null && input != null)
            {
                Vector2 sourcePosition = output.worldBound.center;
                Vector2 targetPosition = input.worldBound.center;

                var graphView = output.node.GetFirstAncestorOfType<GraphView>();
                if (graphView == null)
                {
                    return;
                }

                Vector2 localSourcePosition = graphView.contentViewContainer.WorldToLocal(sourcePosition);
                Vector2 localTargetPosition = graphView.contentViewContainer.WorldToLocal(targetPosition);

                // Calculate midpoint of the edge
                Vector2 midPoint = (localSourcePosition + localTargetPosition) / 2;

                float labelWidth = _parameterLabel.resolvedStyle.width;
                float labelHeight = _parameterLabel.resolvedStyle.height;

                // If the label's width or height is not resolved, use a fallback (e.g., default size)
                if (labelWidth <= 0)
                {
                    labelWidth = 50f;
                }
                if (labelHeight <= 0)
                {
                    labelHeight = 20f;
                }

                _parameterLabel.style.left = midPoint.x - (labelWidth / 2);
                _parameterLabel.style.top = midPoint.y - (labelHeight / 2);
            }
        }

        public void SetVisibility(bool visible)
        {
            _isVisible = visible;
            style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

            if (_parameterLabel != null)
            {
                _parameterLabel.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        public bool IsVisible()
        {
            return _isVisible;
        }
    }
}
