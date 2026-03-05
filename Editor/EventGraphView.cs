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
        private IVisualElementScheduledItem _selectionHighlightSchedule;

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

            UpdateStats();
        }

        public void SetLayoutStrategy(ILayoutStrategy layoutStrategy)
        {
            _layoutStrategy = layoutStrategy;
        }

        public void AutoLayout()
        {
            _layoutStrategy.Layout(this);
        }

        public void UpdateSelectionHighlighting()
        {
            _selectionHighlightSchedule?.Pause();
            _selectionHighlightSchedule = schedule.Execute(ApplySelectionHighlighting);
        }

        private void ApplySelectionHighlighting()
        {
            var selectedNodes = selection.OfType<UnityEventNode>().ToList();

            if (selectedNodes.Count == 0)
            {
                foreach (var edge in edges.OfType<UnityEventEdge>())
                {
                    edge.ResetSelectionHighlight();
                }
                return;
            }

            var selectedNodeSet = new HashSet<Node>(selectedNodes);

            foreach (var edge in edges.OfType<UnityEventEdge>())
            {
                bool isConnected = selectedNodeSet.Contains(edge.output.node)
                    || selectedNodeSet.Contains(edge.input.node);
                edge.SetSelectionHighlight(isConnected);
            }
        }

        private void UpdateStats()
        {
            int nodeCount = nodes.OfType<UnityEventNode>().Count();
            int edgeCount = edges.OfType<UnityEventEdge>().Count();

            _statsLabel.text = $"{nodeCount} Nodes - {edgeCount} Connections";
        }
    }
}
