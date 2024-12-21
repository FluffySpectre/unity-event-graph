using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FluffySpectre.UnityEventGraph.LayoutStrategies
{
    // Force Directed Layout Strategy
    public class ForceDirectedLayoutStrategy : ILayoutStrategy
    {
        private const int _iterations = 100;
        private const float _repulsionStrength = 2500000f;
        private const float _attractionStrength = 0.1f;
        private const float _maxDisplacement = 200f;
        private const float _centeringFactor = 0.01f;

        public void Layout(EventGraphView graphView)
        {
            var nodes = graphView.nodes.ToList().OfType<UnityEventNode>().ToList();
            var edges = graphView.edges.ToList();

            if (nodes.Count == 0)
                return;

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

            for (int iteration = 0; iteration < _iterations; iteration++)
            {
                var forces = new Dictionary<UnityEventNode, Vector2>();
                foreach (var n in nodes) 
                    forces[n] = Vector2.zero;

                // Calculate repulsion forces between all nodes
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
            }

            // Set new positions
            foreach (var n in nodes)
            {
                var rect = n.GetPosition();
                rect.position = positions[n];
                n.SetPosition(rect);
            }
        }
    }
}
