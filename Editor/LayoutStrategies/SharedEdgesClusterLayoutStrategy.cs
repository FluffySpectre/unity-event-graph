using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FluffySpectre.UnityEventGraph.LayoutStrategies
{
    // Cluster Layout Strategy based on Shared Edges
    public class SharedEdgesClusterLayoutStrategy : ILayoutStrategy
    {
        public void Layout(EventGraphView graphView)
        {
            var nodes = graphView.nodes.OfType<UnityEventNode>().ToList();
            if (nodes.Count == 0) return;

            // Build node to index mapping
            var nodeToIndex = new Dictionary<UnityEventNode, int>(nodes.Count);
            for (int i = 0; i < nodes.Count; i++)
            {
                nodeToIndex[nodes[i]] = i;
            }

            // Use Union-Find for efficient clustering
            var unionFind = new UnionFind(nodes.Count);

            // Union connected nodes
            foreach (var edge in graphView.edges)
            {
                var sourceNode = edge.output?.node as UnityEventNode;
                var targetNode = edge.input?.node as UnityEventNode;

                if (sourceNode != null && targetNode != null &&
                    nodeToIndex.TryGetValue(sourceNode, out int sourceIdx) &&
                    nodeToIndex.TryGetValue(targetNode, out int targetIdx))
                {
                    unionFind.Union(sourceIdx, targetIdx);
                }
            }

            // Group nodes by cluster
            var clusters = new Dictionary<int, List<UnityEventNode>>();
            for (int i = 0; i < nodes.Count; i++)
            {
                int root = unionFind.Find(i);
                if (!clusters.TryGetValue(root, out var cluster))
                {
                    cluster = new List<UnityEventNode>();
                    clusters[root] = cluster;
                }
                cluster.Add(nodes[i]);
            }

            // Layout each cluster
            float clusterSpacing = 500f;
            float nodeSpacing = 200f;
            Vector2 currentClusterPosition = Vector2.zero;

            foreach (var cluster in clusters.Values)
            {
                int clusterSize = cluster.Count;
                int nodesPerRow = Mathf.CeilToInt(Mathf.Sqrt(clusterSize));

                for (int i = 0; i < clusterSize; i++)
                {
                    var node = cluster[i];
                    int row = i / nodesPerRow;
                    int column = i % nodesPerRow;

                    float x = currentClusterPosition.x + column * nodeSpacing;
                    float y = currentClusterPosition.y + row * nodeSpacing;

                    node.SetPosition(new Rect(new Vector2(x, y), node.GetPosition().size));
                }

                // Move to the next cluster position
                currentClusterPosition.x += clusterSpacing;
            }
        }
    }

    internal class UnionFind
    {
        private readonly int[] _parent;
        private readonly int[] _rank;

        public UnionFind(int size)
        {
            _parent = new int[size];
            _rank = new int[size];
            
            for (int i = 0; i < size; i++)
            {
                _parent[i] = i;
                _rank[i] = 0;
            }
        }

        public int Find(int x)
        {
            if (_parent[x] != x)
            {
                _parent[x] = Find(_parent[x]); // Path compression
            }
            return _parent[x];
        }

        public void Union(int x, int y)
        {
            int rootX = Find(x);
            int rootY = Find(y);

            if (rootX == rootY) return;

            // Union by rank
            if (_rank[rootX] < _rank[rootY])
            {
                _parent[rootX] = rootY;
            }
            else if (_rank[rootX] > _rank[rootY])
            {
                _parent[rootY] = rootX;
            }
            else
            {
                _parent[rootY] = rootX;
                _rank[rootX]++;
            }
        }
    }
}
