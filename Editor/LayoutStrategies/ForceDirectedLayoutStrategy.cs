using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FluffySpectre.UnityEventGraph.LayoutStrategies
{
    // Force Directed Layout Strategy
    public class ForceDirectedLayoutStrategy : ILayoutStrategy
    {
        private const float _repulsionStrength = 2500000f;
        private const float _attractionStrength = 0.1f;
        private const float _maxDisplacement = 200f;
        private const float _centeringFactor = 0.01f;
        
        // Node count thresholds for optimizations
        private const int LARGE_GRAPH_THRESHOLD = 200;
        private const int VERY_LARGE_GRAPH_THRESHOLD = 500;

        public void Layout(EventGraphView graphView)
        {
            var nodes = graphView.nodes.ToList().OfType<UnityEventNode>().ToList();
            var edges = graphView.edges.ToList();

            if (nodes.Count == 0)
                return;
                
            // Determine optimal iterations based on graph size
            int iterations = GetOptimalIterationCount(nodes.Count);

            // Initialize positions
            var positions = new Dictionary<UnityEventNode, Vector2>();
            foreach (var node in nodes)
            {
                var rect = node.GetPosition();
                if (rect.position == Vector2.zero)
                {
                    // Place the node at a random position
                    rect.position = new Vector2(Random.Range(-1000f, 1000f), Random.Range(-1000f, 1000f));
                    node.SetPosition(rect);
                }
                positions[node] = rect.position;
            }

            // Save edges as pairs of connected nodes
            var nodeEdges = edges.Select(e => new { 
                Source = e.output.node as UnityEventNode, 
                Target = e.input.node as UnityEventNode 
            }).ToList();

            // For very large graphs, use grid layout first as a starting point
            if (nodes.Count > VERY_LARGE_GRAPH_THRESHOLD)
            {
                PreLayoutGrid(nodes);
            }

            // Use a spatial partitioning approach for large graphs to avoid O(n²) comparisons
            bool useSpatialPartitioning = nodes.Count > LARGE_GRAPH_THRESHOLD;
            float cellSize = 300f; // Size of each spatial grid cell
            var spatialGrid = useSpatialPartitioning ? BuildSpatialGrid(nodes, positions, cellSize) : null;

            for (int iteration = 0; iteration < iterations; iteration++)
            {
                var forces = new Dictionary<UnityEventNode, Vector2>();
                foreach (var n in nodes) 
                    forces[n] = Vector2.zero;

                // Calculate repulsion forces between nodes
                if (useSpatialPartitioning)
                {
                    CalculateRepulsionForcesSpatial(nodes, positions, forces, spatialGrid, cellSize);
                }
                else
                {
                    CalculateRepulsionForcesAllPairs(nodes, positions, forces);
                }

                // Attractions forces between connected nodes
                foreach (var edgePair in nodeEdges)
                {
                    var delta = positions[edgePair.Source] - positions[edgePair.Target];
                    float distance = delta.magnitude + 0.01f;
                    Vector2 forceDir = delta.normalized;
                    float forceMagnitude = _attractionStrength * distance;
                    forces[edgePair.Source] -= forceDir * forceMagnitude;
                    forces[edgePair.Target] += forceDir * forceMagnitude;
                }

                // Add a centering force to prevent nodes from drifting too far apart
                Vector2 center = Vector2.zero;
                foreach (var n in nodes)
                    center += positions[n];
                center /= nodes.Count;
                
                foreach (var n in nodes)
                {
                    var toCenter = center - positions[n];
                    forces[n] += toCenter * _centeringFactor;
                }

                // Update positions
                foreach (var n in nodes)
                {
                    // Limit displacement to stabilize the layout
                    Vector2 displacement = forces[n];
                    if (displacement.magnitude > _maxDisplacement)
                    {
                        displacement = displacement.normalized * _maxDisplacement;
                    }

                    positions[n] += displacement;
                }
                
                // Update spatial grid every few iterations if using spatial partitioning
                if (useSpatialPartitioning && iteration % 5 == 0)
                {
                    spatialGrid = BuildSpatialGrid(nodes, positions, cellSize);
                }
            }

            // Set new positions
            foreach (var n in nodes)
            {
                var rect = n.GetPosition();
                rect.position = positions[n];
                n.SetPosition(rect);
            }
        }
        
        private void PreLayoutGrid(List<UnityEventNode> nodes)
        {
            float horizontalSpacing = 300f;
            float verticalSpacing = 200f;
            int nodesPerRow = Mathf.CeilToInt(Mathf.Sqrt(nodes.Count) * 1.5f);
            
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                int row = i / nodesPerRow;
                int column = i % nodesPerRow;

                float x = column * horizontalSpacing;
                float y = row * verticalSpacing;
                
                var rect = node.GetPosition();
                rect.position = new Vector2(x, y);
                node.SetPosition(rect);
            }
        }
        
        private Dictionary<Vector2Int, List<UnityEventNode>> BuildSpatialGrid(
            List<UnityEventNode> nodes, 
            Dictionary<UnityEventNode, Vector2> positions, 
            float cellSize)
        {
            var grid = new Dictionary<Vector2Int, List<UnityEventNode>>();
            
            foreach (var node in nodes)
            {
                Vector2 pos = positions[node];
                Vector2Int cell = new Vector2Int(
                    Mathf.FloorToInt(pos.x / cellSize),
                    Mathf.FloorToInt(pos.y / cellSize)
                );
                
                if (!grid.ContainsKey(cell))
                {
                    grid[cell] = new List<UnityEventNode>();
                }
                grid[cell].Add(node);
            }
            
            return grid;
        }
        
        private void CalculateRepulsionForcesSpatial(
            List<UnityEventNode> nodes,
            Dictionary<UnityEventNode, Vector2> positions,
            Dictionary<UnityEventNode, Vector2> forces,
            Dictionary<Vector2Int, List<UnityEventNode>> spatialGrid,
            float cellSize)
        {
            // For each node, calculate repulsion from nodes in the same and adjacent cells
            foreach (var node in nodes)
            {
                Vector2 pos = positions[node];
                Vector2Int cell = new Vector2Int(
                    Mathf.FloorToInt(pos.x / cellSize),
                    Mathf.FloorToInt(pos.y / cellSize)
                );
                
                // Check the current cell and adjacent cells
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        Vector2Int neighborCell = new Vector2Int(cell.x + dx, cell.y + dy);
                        
                        if (spatialGrid.TryGetValue(neighborCell, out var nodesInCell))
                        {
                            foreach (var otherNode in nodesInCell)
                            {
                                if (node == otherNode) continue;
                                
                                var delta = positions[node] - positions[otherNode];
                                float distance = delta.magnitude + 0.01f; // Avoid division by zero
                                
                                // Apply stronger repulsion at short distances, weaker at long distances
                                if (distance < cellSize * 2)
                                {
                                    Vector2 forceDir = delta.normalized;
                                    float forceMagnitude = _repulsionStrength / (distance * distance);
                                    forces[node] += forceDir * forceMagnitude;
                                }
                            }
                        }
                    }
                }
            }
        }
        
        private void CalculateRepulsionForcesAllPairs(
            List<UnityEventNode> nodes,
            Dictionary<UnityEventNode, Vector2> positions,
            Dictionary<UnityEventNode, Vector2> forces)
        {
            // Standard O(n²) all-pairs repulsion calculation
            for (int i = 0; i < nodes.Count; i++)
            {
                for (int j = i + 1; j < nodes.Count; j++)
                {
                    var n1 = nodes[i];
                    var n2 = nodes[j];
                    var delta = positions[n1] - positions[n2];
                    float distance = delta.magnitude + 0.01f; // Avoid division by zero
                    Vector2 forceDir = delta.normalized;
                    float forceMagnitude = _repulsionStrength / (distance * distance);
                    forces[n1] += forceDir * forceMagnitude;
                    forces[n2] -= forceDir * forceMagnitude;
                }
            }
        }
        
        private int GetOptimalIterationCount(int nodeCount)
        {
            // Scale down iterations for large graphs
            if (nodeCount > VERY_LARGE_GRAPH_THRESHOLD) return 20;
            if (nodeCount > LARGE_GRAPH_THRESHOLD) return 50;
            return 100;
        }
    }
}
