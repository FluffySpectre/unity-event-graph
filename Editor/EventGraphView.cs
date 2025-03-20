using System.Collections.Generic;
using System.Linq;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

using FluffySpectre.UnityEventGraph.LayoutStrategies;

namespace FluffySpectre.UnityEventGraph
{  
    public class EventGraphView : GraphView
    {
        private ILayoutStrategy _layoutStrategy = new ForceDirectedLayoutStrategy();
        private Label _statsLabel;
        private bool _isHighDetailMode = true;
        
        // For large graph optimization
        private const int LARGE_GRAPH_THRESHOLD = 200;

        public EventGraphView()
        {
            style.flexGrow = 1;

            var contentZoomer = new ContentZoomer
            {
                minScale = 0.05f
            };
            SetupZoom(contentZoomer.minScale, ContentZoomer.DefaultMaxScale);

            var grid = new GridBackground();
            Insert(0, grid);
            grid.StretchToParentSize();

            this.AddManipulator(contentZoomer);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());

            var miniMap = new MiniMap
            {
                anchored = false,
                style = 
                {
                    backgroundColor = new StyleColor(new Color(0.15f, 0.15f, 0.15f))
                }
            };
            miniMap.SetPosition(new Rect(10, 10, 200, 200));
            miniMap.graphView = this;
            Add(miniMap);

            _statsLabel = new Label("0 Nodes - 0 Connections")
            {
                style = { unityTextAlign = TextAnchor.MiddleCenter, position = Position.Absolute, bottom = 5, left = 5 }
            };
            Add(_statsLabel);
            
            // Register for zoom changes to update detail level
            this.viewTransformChanged += OnViewTransformChanged;
        }

        private void OnViewTransformChanged(GraphView graphView)
        {
            UpdateDetailLevel();
        }

        private void UpdateDetailLevel()
        {
            // Get current node count
            int nodeCount = nodes.OfType<UnityEventNode>().Count();
            
            // Only apply LOD for large graphs
            if (nodeCount < LARGE_GRAPH_THRESHOLD)
                return;
                
            float zoomLevel = viewTransform.scale.x;
            bool shouldBeHighDetail = zoomLevel > 0.5f;
            
            // Only update if detail level changed
            if (shouldBeHighDetail != _isHighDetailMode)
            {
                _isHighDetailMode = shouldBeHighDetail;
                UpdateNodeDetailLevel();
            }
        }
        
        private void UpdateNodeDetailLevel()
        {
            // For nodes
            foreach (var node in nodes.OfType<UnityEventNode>())
            {
                node.SetDetailLevel(_isHighDetailMode);
            }
            
            // For edges
            foreach (var edge in edges.OfType<UnityEventEdge>())
            {
                edge.SetDetailLevel(_isHighDetailMode);
            }
        }

        public void ClearGraph()
        {
            graphElements.ForEach(RemoveElement);
            UpdateStats();
        }

        public void PopulateGraph(List<UnityEventNode> nodes, List<EdgeData> edges)
        {
            foreach (var node in nodes)
            {
                AddElement(node);
            }

            foreach (var edgeData in edges)
            {
                var edge = UnityEventEdge.CreateEdge(edgeData);
                if (edge != null)
                {
                    AddElement(edge);
                }
            }

            // Update stats after populating
            UpdateStats();
            
            // Set appropriate detail level based on graph size
            _isHighDetailMode = nodes.Count < LARGE_GRAPH_THRESHOLD;
            UpdateNodeDetailLevel();
        }

        public void UpdateStats()
        {
            // Always count directly from the graph view
            int nodeCount = nodes.OfType<UnityEventNode>().Count();
            int edgeCount = edges.OfType<UnityEventEdge>().Count();
            
            // Count visible elements
            int visibleNodeCount = nodes.OfType<UnityEventNode>().Count(n => n.IsVisible());
            int visibleEdgeCount = edges.OfType<UnityEventEdge>().Count(e => e.IsVisible());

            if (visibleNodeCount != nodeCount || visibleEdgeCount != edgeCount)
            {
                _statsLabel.text = $"{visibleNodeCount}/{nodeCount} Nodes - {visibleEdgeCount}/{edgeCount} Connections";
            }
            else
            {
                _statsLabel.text = $"{nodeCount} Nodes - {edgeCount} Connections";
            }
        }

        public void SetLayoutStrategy(ILayoutStrategy layoutStrategy)
        {
            _layoutStrategy = layoutStrategy;
        }

        public void AutoLayout()
        {
            _layoutStrategy.Layout(this);
        }
        
        public void ShowPartialGraph(IEnumerable<UnityEventNode> visibleNodes, bool showConnections = true)
        {
            var nodeSet = new HashSet<UnityEventNode>(visibleNodes);
            
            foreach (var node in nodes.OfType<UnityEventNode>())
            {
                bool visible = nodeSet.Contains(node);
                node.SetVisibility(visible);
            }
            
            if (showConnections)
            {
                foreach (var edge in edges.OfType<UnityEventEdge>())
                {
                    bool edgeVisible = edge.output.node is UnityEventNode outputNode &&
                                    edge.input.node is UnityEventNode inputNode &&
                                    nodeSet.Contains(outputNode) &&
                                    nodeSet.Contains(inputNode);
                    
                    edge.SetVisibility(edgeVisible);
                }
            }
            
            UpdateStats();
        }
    }
}
