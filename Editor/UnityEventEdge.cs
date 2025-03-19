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
        private bool _isHighDetail = true;
        private string _parameterValue;
        private bool _isPositioningScheduled = false;

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
                edge._parameterValue = edgeData.ParameterValue;
                edge.CreateParameterLabel();
                edge.RegisterNodePositionChangeListeners();
            }

            sourcePort.Connect(edge);
            targetPort.Connect(edge);

            return edge;
        }
        
        private void CreateParameterLabel()
        {
            if (_parameterLabel != null)
                return;
                
            _parameterLabel = new Label(_parameterValue)
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
            
            // Register for geometry changed events on the label itself
            _parameterLabel.RegisterCallback<GeometryChangedEvent>(OnLabelGeometryChanged);
            
            // Schedule a delayed position update to ensure layout is complete
            ScheduleDelayedPositionUpdate();
            
            _parameterLabel.style.display = _isHighDetail ? DisplayStyle.Flex : DisplayStyle.None;
        }
        
        private void OnLabelGeometryChanged(GeometryChangedEvent evt)
        {
            // When the label's geometry changes, update its position
            UpdateParameterLabelPosition();
        }

        private void RegisterNodePositionChangeListeners()
        {
            // Add listeners for both source and target nodes
            if (output?.node is Node sourceNode)
            {
                sourceNode.RegisterCallback<GeometryChangedEvent>(_ => {
                    ScheduleDelayedPositionUpdate();
                });
            }

            if (input?.node is Node targetNode)
            {
                targetNode.RegisterCallback<GeometryChangedEvent>(_ => {
                    ScheduleDelayedPositionUpdate();
                });
            }
            
            // Add a listener for the edge's own geometry changes
            this.RegisterCallback<GeometryChangedEvent>(_ => {
                ScheduleDelayedPositionUpdate();
            });
        }
        
        private void ScheduleDelayedPositionUpdate()
        {
            if (_isPositioningScheduled)
                return;
                
            _isPositioningScheduled = true;
            
            // Schedule multiple updates at different intervals to ensure proper positioning
            schedule.Execute(() => {
                UpdateParameterLabelPosition();
                _isPositioningScheduled = false;
            }).ExecuteLater(10);
            
            schedule.Execute(UpdateParameterLabelPosition).ExecuteLater(50);
            schedule.Execute(UpdateParameterLabelPosition).ExecuteLater(100);
            schedule.Execute(UpdateParameterLabelPosition).ExecuteLater(500);
        }

        public void SetParameterValue(string value)
        {
            _parameterValue = value;
            
            if (_parameterLabel != null)
            {
                if (value != null)
                {
                    _parameterLabel.text = value;
                    _parameterLabel.style.display = _isHighDetail ? DisplayStyle.Flex : DisplayStyle.None;
                    ScheduleDelayedPositionUpdate();
                }
                else
                {
                    // Hide label if there is no value
                    _parameterLabel.style.display = DisplayStyle.None;
                }
            }
            else if (value != null)
            {
                CreateParameterLabel();
            }
        }
        
        public void SetDetailLevel(bool highDetail)
        {
            _isHighDetail = highDetail;
            
            // For performance in large graphs, simplify the edge style in low detail mode
            if (highDetail)
            {
                // Standard edge style for high detail
                edgeControl.inputColor = Color.white;
                edgeControl.outputColor = Color.white;
                
                // Show parameter label in high detail mode
                if (_parameterLabel != null && _parameterValue != null)
                {
                    _parameterLabel.style.display = DisplayStyle.Flex;
                    ScheduleDelayedPositionUpdate();
                }
            }
            else
            {
                // Simplified edge style for low detail
                edgeControl.inputColor = new Color(0.5f, 0.5f, 0.5f, 0.8f);
                edgeControl.outputColor = new Color(0.5f, 0.5f, 0.5f, 0.8f);
                
                // Hide parameter label in low detail mode
                if (_parameterLabel != null)
                {
                    _parameterLabel.style.display = DisplayStyle.None;
                }
            }
        }
        
        // Public method that can be called to force update positions after graph loading
        public void ForceUpdateParameterPosition()
        {
            ScheduleDelayedPositionUpdate();
        }

        private void UpdateParameterLabelPosition()
        {
            if (_parameterLabel == null || output == null || input == null || !_isHighDetail)
                return;
                
            // Make sure the ports are visible and have valid geometry
            if (!output.worldBound.width.Equals(0) && !input.worldBound.width.Equals(0))
            {
                Vector2 sourcePosition = output.worldBound.center;
                Vector2 targetPosition = input.worldBound.center;

                var graphView = output.node.GetFirstAncestorOfType<GraphView>();
                if (graphView == null)
                    return;

                Vector2 localSourcePosition = graphView.contentViewContainer.WorldToLocal(sourcePosition);
                Vector2 localTargetPosition = graphView.contentViewContainer.WorldToLocal(targetPosition);

                // Calculate midpoint of the edge
                Vector2 midPoint = (localSourcePosition + localTargetPosition) / 2;

                float labelWidth = _parameterLabel.resolvedStyle.width;
                float labelHeight = _parameterLabel.resolvedStyle.height;

                // If the label's width or height is not resolved, use a fallback (e.g., default size)
                if (labelWidth <= 0)
                    labelWidth = 50f;
                if (labelHeight <= 0)
                    labelHeight = 20f;

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
                _parameterLabel.style.display = visible && _isHighDetail ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        public bool IsVisible()
        {
            return _isVisible;
        }
    }
}
