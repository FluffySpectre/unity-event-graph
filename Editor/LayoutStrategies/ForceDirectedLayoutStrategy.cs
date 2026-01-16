using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FluffySpectre.UnityEventGraph.LayoutStrategies
{
    public class ForceDirectedLayoutStrategy : ILayoutStrategy
    {
        private const float RepulsionStrength = 2500000f;
        private const float AttractionStrength = 0.1f;
        private const float MaxDisplacement = 200f;
        private const float CenteringFactor = 0.01f;
        private const float StabilityThreshold = 0.5f;
        private const int MinIterations = 20;
        private const int MaxIterations = 100;
        private const float BarnesHutTheta = 0.8f;
        private const int BarnesHutThreshold = 50; // Use Barnes-Hut when node count exceeds this

        public void Layout(EventGraphView graphView)
        {
            var nodes = graphView.nodes.OfType<UnityEventNode>().ToList();
            var edges = graphView.edges.ToList();

            if (nodes.Count == 0)
                return;

            // Determine iteration count based on node count
            int iterations = CalculateIterations(nodes.Count);

            // Initialize positions
            var positions = new Dictionary<UnityEventNode, Vector2>(nodes.Count);
            var forces = new Dictionary<UnityEventNode, Vector2>(nodes.Count);
            
            foreach (var node in nodes)
            {
                var rect = node.GetPosition();
                if (rect.position == Vector2.zero)
                {
                    rect.position = new Vector2(Random.Range(-1000f, 1000f), Random.Range(-1000f, 1000f));
                    node.SetPosition(rect);
                }
                positions[node] = rect.position;
                forces[node] = Vector2.zero;
            }

            // Pre-compute edge pairs
            var nodeEdges = new List<(UnityEventNode Source, UnityEventNode Target)>(edges.Count);
            foreach (var e in edges)
            {
                var source = e.output?.node as UnityEventNode;
                var target = e.input?.node as UnityEventNode;
                if (source != null && target != null)
                {
                    nodeEdges.Add((source, target));
                }
            }

            bool useBarnesHut = nodes.Count > BarnesHutThreshold;

            for (int iteration = 0; iteration < iterations; iteration++)
            {
                // Reset forces
                foreach (var n in nodes)
                    forces[n] = Vector2.zero;

                // Calculate repulsion forces
                if (useBarnesHut)
                {
                    CalculateRepulsionBarnesHut(nodes, positions, forces);
                }
                else
                {
                    CalculateRepulsionDirect(nodes, positions, forces);
                }

                // Calculate attraction forces (always direct - O(E))
                CalculateAttractionForces(nodeEdges, positions, forces);

                // Add centering force
                ApplyCenteringForce(nodes, positions, forces);

                // Update positions and check for stability
                float maxMovement = UpdatePositions(nodes, positions, forces);

                // Early termination if stable
                if (iteration > MinIterations && maxMovement < StabilityThreshold)
                {
                    break;
                }
            }

            // Apply final positions
            foreach (var n in nodes)
            {
                var rect = n.GetPosition();
                rect.position = positions[n];
                n.SetPosition(rect);
            }
        }

        private int CalculateIterations(int nodeCount)
        {
            if (nodeCount <= 10) return MaxIterations;
            if (nodeCount <= 50) return 75;
            if (nodeCount <= 100) return 50;
            if (nodeCount <= 200) return 35;
            return MinIterations;
        }

        private void CalculateRepulsionDirect(List<UnityEventNode> nodes, Dictionary<UnityEventNode, Vector2> positions, Dictionary<UnityEventNode, Vector2> forces)
        {
            int count = nodes.Count;
            for (int i = 0; i < count; i++)
            {
                for (int j = i + 1; j < count; j++)
                {
                    var n1 = nodes[i];
                    var n2 = nodes[j];
                    var delta = positions[n1] - positions[n2];
                    float distSq = delta.sqrMagnitude + 0.01f;
                    float distance = Mathf.Sqrt(distSq);
                    Vector2 forceDir = delta / distance;
                    float forceMagnitude = RepulsionStrength / distSq;
                    
                    var force = forceDir * forceMagnitude;
                    forces[n1] += force;
                    forces[n2] -= force;
                }
            }
        }

        private void CalculateRepulsionBarnesHut(List<UnityEventNode> nodes, Dictionary<UnityEventNode, Vector2> positions, Dictionary<UnityEventNode, Vector2> forces)
        {
            // Build quadtree
            var bounds = CalculateBounds(nodes, positions);
            var quadTree = new QuadTree(bounds);

            foreach (var node in nodes)
            {
                quadTree.Insert(node, positions[node]);
            }

            // Calculate forces using quadtree
            foreach (var node in nodes)
            {
                var pos = positions[node];
                var force = quadTree.CalculateForce(node, pos, BarnesHutTheta, RepulsionStrength);
                forces[node] += force;
            }
        }

        private Rect CalculateBounds(List<UnityEventNode> nodes, Dictionary<UnityEventNode, Vector2> positions)
        {
            if (nodes.Count == 0) return new Rect(0, 0, 1000, 1000);

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;

            foreach (var node in nodes)
            {
                var pos = positions[node];
                minX = Mathf.Min(minX, pos.x);
                minY = Mathf.Min(minY, pos.y);
                maxX = Mathf.Max(maxX, pos.x);
                maxY = Mathf.Max(maxY, pos.y);
            }

            float padding = 100f;
            return new Rect(minX - padding, minY - padding, maxX - minX + padding * 2, maxY - minY + padding * 2);
        }

        private void CalculateAttractionForces(List<(UnityEventNode Source, UnityEventNode Target)> edges, Dictionary<UnityEventNode, Vector2> positions, Dictionary<UnityEventNode, Vector2> forces)
        {
            foreach (var (source, target) in edges)
            {
                var delta = positions[source] - positions[target];
                float distance = delta.magnitude + 0.01f;
                Vector2 forceDir = delta / distance;
                float forceMagnitude = AttractionStrength * distance;
                
                var force = forceDir * forceMagnitude;
                forces[source] -= force;
                forces[target] += force;
            }
        }

        private void ApplyCenteringForce(List<UnityEventNode> nodes, Dictionary<UnityEventNode, Vector2> positions, Dictionary<UnityEventNode, Vector2> forces)
        {
            Vector2 center = Vector2.zero;
            foreach (var n in nodes)
                center += positions[n];
            center /= nodes.Count;

            foreach (var n in nodes)
            {
                var toCenter = center - positions[n];
                forces[n] += toCenter * CenteringFactor;
            }
        }

        private float UpdatePositions(List<UnityEventNode> nodes, Dictionary<UnityEventNode, Vector2> positions, Dictionary<UnityEventNode, Vector2> forces)
        {
            float maxMovement = 0f;

            foreach (var n in nodes)
            {
                Vector2 displacement = forces[n];
                float magnitude = displacement.magnitude;
                
                if (magnitude > MaxDisplacement)
                {
                    displacement = displacement * (MaxDisplacement / magnitude);
                    magnitude = MaxDisplacement;
                }

                positions[n] += displacement;
                maxMovement = Mathf.Max(maxMovement, magnitude);
            }

            return maxMovement;
        }
    }

    internal class QuadTree
    {
        private const int MaxDepth = 8;
        private const int MaxNodesPerLeaf = 1;

        private readonly Rect _bounds;
        private readonly int _depth;
        private QuadTree[] _children;
        private readonly List<(UnityEventNode Node, Vector2 Position)> _nodes = new();
        private Vector2 _centerOfMass;
        private float _totalMass;

        public QuadTree(Rect bounds, int depth = 0)
        {
            _bounds = bounds;
            _depth = depth;
        }

        public void Insert(UnityEventNode node, Vector2 position)
        {
            // Iterative insertion using a stack
            var stack = new Stack<(QuadTree tree, UnityEventNode node, Vector2 pos)>();
            stack.Push((this, node, position));
            
            while (stack.Count > 0)
            {
                var (currentTree, currentNode, currentPos) = stack.Pop();
                
                if (!currentTree._bounds.Contains(currentPos))
                    continue;

                if (currentTree._children != null)
                {
                    foreach (var child in currentTree._children)
                    {
                        if (child._bounds.Contains(currentPos))
                        {
                            stack.Push((child, currentNode, currentPos));
                            break;
                        }
                    }
                    currentTree.UpdateCenterOfMass();
                    continue;
                }

                currentTree._nodes.Add((currentNode, currentPos));
                currentTree.UpdateCenterOfMass();

                if (currentTree._nodes.Count > MaxNodesPerLeaf && currentTree._depth < MaxDepth)
                {
                    currentTree.Subdivide();
                    foreach (var (n, p) in currentTree._nodes)
                    {
                        foreach (var child in currentTree._children)
                        {
                            if (child._bounds.Contains(p))
                            {
                                stack.Push((child, n, p));
                                break;
                            }
                        }
                    }
                    currentTree._nodes.Clear();
                }
            }
        }

        private void Subdivide()
        {
            float halfWidth = _bounds.width / 2;
            float halfHeight = _bounds.height / 2;
            float x = _bounds.x;
            float y = _bounds.y;

            _children = new QuadTree[4];
            _children[0] = new QuadTree(new Rect(x, y, halfWidth, halfHeight), _depth + 1);
            _children[1] = new QuadTree(new Rect(x + halfWidth, y, halfWidth, halfHeight), _depth + 1);
            _children[2] = new QuadTree(new Rect(x, y + halfHeight, halfWidth, halfHeight), _depth + 1);
            _children[3] = new QuadTree(new Rect(x + halfWidth, y + halfHeight, halfWidth, halfHeight), _depth + 1);
        }

        private void UpdateCenterOfMass()
        {
            if (_children != null)
            {
                _centerOfMass = Vector2.zero;
                _totalMass = 0;
                foreach (var child in _children)
                {
                    if (child._totalMass > 0)
                    {
                        _centerOfMass += child._centerOfMass * child._totalMass;
                        _totalMass += child._totalMass;
                    }
                }
                if (_totalMass > 0)
                {
                    _centerOfMass /= _totalMass;
                }
            }
            else
            {
                _centerOfMass = Vector2.zero;
                _totalMass = _nodes.Count;
                foreach (var (_, pos) in _nodes)
                {
                    _centerOfMass += pos;
                }
                if (_totalMass > 0)
                {
                    _centerOfMass /= _totalMass;
                }
            }
        }

        public Vector2 CalculateForce(UnityEventNode targetNode, Vector2 targetPosition, float theta, float repulsionStrength)
        {
            // Iterative force calculation using a stack
            Vector2 totalForce = Vector2.zero;
            var stack = new Stack<QuadTree>();
            stack.Push(this);
            
            int iterations = 0;
            const int MaxIterations = 10000;
            
            while (stack.Count > 0 && iterations < MaxIterations)
            {
                iterations++;
                var currentTree = stack.Pop();
                
                if (currentTree._totalMass == 0)
                    continue;

                var delta = targetPosition - currentTree._centerOfMass;
                float distance = delta.magnitude;

                if (distance < 0.01f)
                    continue;

                float size = Mathf.Max(currentTree._bounds.width, currentTree._bounds.height);

                // If far enough or leaf node, treat as single mass
                if (currentTree._children == null || (size / distance) < theta)
                {
                    // Don't apply force from self
                    if (currentTree._nodes.Count == 1 && currentTree._nodes[0].Node == targetNode)
                        continue;

                    float distSq = distance * distance;
                    Vector2 forceDir = delta / distance;
                    float forceMagnitude = repulsionStrength * currentTree._totalMass / distSq;
                    totalForce += forceDir * forceMagnitude;
                }
                else
                {
                    // Push children onto stack
                    foreach (var child in currentTree._children)
                    {
                        if (child._totalMass > 0)
                        {
                            stack.Push(child);
                        }
                    }
                }
            }
            
            return totalForce;
        }
    }
}
