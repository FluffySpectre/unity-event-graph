using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace FluffySpectre.UnityEventGraph.LayoutStrategies
{
    /// <summary>
    /// Hierarchical layout that arranges nodes in layers based on event flow direction (sources -> listeners), with crossing minimization
    /// </summary>
    public class HierarchicalLayoutStrategy : ILayoutStrategy
    {
        private const float LayerSpacing = 500f;
        private const float NodeSpacing = 250f;
        private const int BarycenterSweeps = 4;
        private const float MaxRowWidth = 20000f;
        private const float ComponentPadding = 400f;

        private struct PortEdge
        {
            public UnityEventNode Source;
            public UnityEventNode Target;
            public float SourcePortOffset;
            public float TargetPortOffset;
        }

        public void Layout(EventGraphView graphView)
        {
            var nodes = graphView.nodes.ToList().OfType<UnityEventNode>().ToList();
            var edges = graphView.edges.ToList();

            if (nodes.Count == 0)
                return;

            var successors = new Dictionary<UnityEventNode, List<UnityEventNode>>();
            var predecessors = new Dictionary<UnityEventNode, List<UnityEventNode>>();
            var incomingPortEdges = new Dictionary<UnityEventNode, List<PortEdge>>();
            var outgoingPortEdges = new Dictionary<UnityEventNode, List<PortEdge>>();

            foreach (var node in nodes)
            {
                successors[node] = new List<UnityEventNode>();
                predecessors[node] = new List<UnityEventNode>();
                incomingPortEdges[node] = new List<PortEdge>();
                outgoingPortEdges[node] = new List<PortEdge>();
            }

            foreach (var edge in edges)
            {
                var source = edge.output.node as UnityEventNode;
                var target = edge.input.node as UnityEventNode;
                if (source != null && target != null && source != target)
                {
                    successors[source].Add(target);
                    predecessors[target].Add(source);

                    var portEdge = new PortEdge
                    {
                        Source = source,
                        Target = target,
                        SourcePortOffset = GetPortOffset(source.outputContainer, edge.output),
                        TargetPortOffset = GetPortOffset(target.inputContainer, edge.input)
                    };
                    outgoingPortEdges[source].Add(portEdge);
                    incomingPortEdges[target].Add(portEdge);
                }
            }

            // Find connected components
            var components = FindConnectedComponents(nodes, successors, predecessors);

            // Prepare each component's layers and measure dimensions
            var preparedComponents = new List<(List<List<UnityEventNode>> layers, float width, float height)>();
            foreach (var component in components)
            {
                var layers = AssignLayers(component, successors, predecessors);
                MinimizeCrossings(layers, incomingPortEdges, outgoingPortEdges);

                float width = layers.Count * LayerSpacing;
                float height = layers.Max(l => l.Count) * NodeSpacing;
                preparedComponents.Add((layers, width, height));
            }

            // Place components in rows, positioning each directly at its final location
            float currentRowX = 0f;
            float currentRowY = 0f;
            float currentRowMaxHeight = 0f;

            foreach (var (layers, width, height) in preparedComponents)
            {
                // Wrap to next row if this component would exceed the max row width
                if (currentRowX > 0f && currentRowX + width > MaxRowWidth)
                {
                    currentRowX = 0f;
                    currentRowY += currentRowMaxHeight + ComponentPadding;
                    currentRowMaxHeight = 0f;
                }

                AssignCoordinates(layers, currentRowX, currentRowY);

                currentRowX += width + ComponentPadding;
                if (height > currentRowMaxHeight)
                    currentRowMaxHeight = height;
            }
        }

        private List<List<UnityEventNode>> FindConnectedComponents(List<UnityEventNode> nodes, Dictionary<UnityEventNode, List<UnityEventNode>> successors, Dictionary<UnityEventNode, List<UnityEventNode>> predecessors)
        {
            var visited = new HashSet<UnityEventNode>();
            var components = new List<List<UnityEventNode>>();

            foreach (var node in nodes)
            {
                if (visited.Contains(node))
                    continue;

                var component = new List<UnityEventNode>();
                var stack = new Stack<UnityEventNode>();
                stack.Push(node);

                while (stack.Count > 0)
                {
                    var current = stack.Pop();
                    if (!visited.Add(current))
                        continue;

                    component.Add(current);

                    foreach (var neighbor in successors[current])
                    {
                        if (!visited.Contains(neighbor))
                            stack.Push(neighbor);
                    }

                    foreach (var neighbor in predecessors[current])
                    {
                        if (!visited.Contains(neighbor))
                            stack.Push(neighbor);
                    }
                }

                components.Add(component);
            }

            return components;
        }

        private List<List<UnityEventNode>> AssignLayers(List<UnityEventNode> component, Dictionary<UnityEventNode, List<UnityEventNode>> successors, Dictionary<UnityEventNode, List<UnityEventNode>> predecessors)
        {
            var componentSet = new HashSet<UnityEventNode>(component);

            // DFS-based topological sort (back edges are skipped to break cycles)
            var visited = new HashSet<UnityEventNode>();
            var inStack = new HashSet<UnityEventNode>();
            var topoOrder = new List<UnityEventNode>();

            var roots = component.Where(n =>
                !predecessors[n].Any(p => componentSet.Contains(p))).ToList();
            if (roots.Count == 0)
                roots.Add(component[0]);

            foreach (var root in roots)
                TopologicalSortDfs(root, successors, componentSet, visited, inStack, topoOrder);
            foreach (var node in component)
            {
                if (!visited.Contains(node))
                    TopologicalSortDfs(node, successors, componentSet, visited, inStack, topoOrder);
            }

            topoOrder.Reverse();

            // Build topological position map for back-edge detection
            var topoPosition = new Dictionary<UnityEventNode, int>();
            for (int i = 0; i < topoOrder.Count; i++)
                topoPosition[topoOrder[i]] = i;

            // Longest-path layering in O(V+E), skipping back edges
            var layerMap = new Dictionary<UnityEventNode, int>();
            foreach (var node in topoOrder)
            {
                if (!layerMap.ContainsKey(node))
                    layerMap[node] = 0;

                foreach (var succ in successors[node])
                {
                    if (!componentSet.Contains(succ))
                        continue;
                    if (topoPosition[succ] <= topoPosition[node])
                        continue; // Back edge - skip to break cycle

                    int newLayer = layerMap[node] + 1;
                    if (!layerMap.ContainsKey(succ) || layerMap[succ] < newLayer)
                        layerMap[succ] = newLayer;
                }
            }

            // Group nodes by layer
            int maxLayer = layerMap.Count > 0 ? layerMap.Values.Max() : 0;
            var layers = new List<List<UnityEventNode>>();
            for (int i = 0; i <= maxLayer; i++)
                layers.Add(component.Where(n => layerMap[n] == i).ToList());

            return layers;
        }

        private void TopologicalSortDfs(UnityEventNode node,
            Dictionary<UnityEventNode, List<UnityEventNode>> successors,
            HashSet<UnityEventNode> componentSet,
            HashSet<UnityEventNode> visited,
            HashSet<UnityEventNode> inStack,
            List<UnityEventNode> topoOrder)
        {
            if (visited.Contains(node))
                return;

            visited.Add(node);
            inStack.Add(node);

            foreach (var succ in successors[node])
            {
                if (!componentSet.Contains(succ) || visited.Contains(succ))
                    continue;
                if (inStack.Contains(succ))
                    continue; // Back edge - skip to break cycle
                TopologicalSortDfs(succ, successors, componentSet, visited, inStack, topoOrder);
            }

            inStack.Remove(node);
            topoOrder.Add(node);
        }

        private void MinimizeCrossings(List<List<UnityEventNode>> layers,
            Dictionary<UnityEventNode, List<PortEdge>> incomingPortEdges,
            Dictionary<UnityEventNode, List<PortEdge>> outgoingPortEdges)
        {
            if (layers.Count <= 1)
                return;

            for (int sweep = 0; sweep < BarycenterSweeps; sweep++)
            {
                // Forward sweep (layer 1 to last) - sort by predecessors' output port positions
                for (int i = 1; i < layers.Count; i++)
                {
                    var prevLayerSet = new HashSet<UnityEventNode>(layers[i - 1]);
                    SortLayerByBarycenter(layers[i], layers[i - 1], prevLayerSet, incomingPortEdges, isForward: true);
                }

                // Backward sweep (second-to-last to layer 0) - sort by successors' input port positions
                for (int i = layers.Count - 2; i >= 0; i--)
                {
                    var nextLayerSet = new HashSet<UnityEventNode>(layers[i + 1]);
                    SortLayerByBarycenter(layers[i], layers[i + 1], nextLayerSet, outgoingPortEdges, isForward: false);
                }
            }
        }

        private void SortLayerByBarycenter(List<UnityEventNode> layer, List<UnityEventNode> referenceLayer,
            HashSet<UnityEventNode> referenceSet, Dictionary<UnityEventNode, List<PortEdge>> portEdges,
            bool isForward)
        {
            // Build position map for the reference layer
            var positionMap = new Dictionary<UnityEventNode, int>();
            for (int i = 0; i < referenceLayer.Count; i++)
                positionMap[referenceLayer[i]] = i;

            // Compute port-aware barycenter for each node in the layer
            var barycenters = new Dictionary<UnityEventNode, float>();
            foreach (var node in layer)
            {
                var relevantEdges = portEdges[node]
                    .Where(e => referenceSet.Contains(isForward ? e.Source : e.Target))
                    .ToList();

                if (relevantEdges.Count > 0)
                {
                    float sum = 0f;
                    foreach (var edge in relevantEdges)
                    {
                        var refNode = isForward ? edge.Source : edge.Target;
                        // Use the port offset on the reference-layer side to break ties
                        var portOffset = isForward ? edge.SourcePortOffset : edge.TargetPortOffset;
                        sum += positionMap[refNode] + portOffset;
                    }
                    barycenters[node] = sum / relevantEdges.Count;
                }
                else
                {
                    // Keep relative position for nodes with no connections to reference layer
                    barycenters[node] = layer.IndexOf(node);
                }
            }

            layer.Sort((a, b) => barycenters[a].CompareTo(barycenters[b]));
        }

        private static float GetPortOffset(VisualElement container, VisualElement port)
        {
            int count = container.childCount;
            if (count <= 1)
                return 0f;

            int index = 0;
            foreach (var child in container.Children())
            {
                if (child == port)
                    break;
                index++;
            }

            // Normalize to [-0.5, 0.5] so port order breaks ties without overriding node position
            return (float)index / (count - 1) - 0.5f;
        }

        private void AssignCoordinates(List<List<UnityEventNode>> layers, float offsetX, float offsetY)
        {
            for (int layerIndex = 0; layerIndex < layers.Count; layerIndex++)
            {
                var layer = layers[layerIndex];
                float layerHeight = layer.Count * NodeSpacing;
                float startY = offsetY - layerHeight / 2f;

                for (int nodeIndex = 0; nodeIndex < layer.Count; nodeIndex++)
                {
                    var node = layer[nodeIndex];
                    float x = offsetX + layerIndex * LayerSpacing;
                    float y = startY + nodeIndex * NodeSpacing;
                    node.SetPosition(new Rect(new Vector2(x, y), node.GetPosition().size));
                }
            }
        }
    }
}
