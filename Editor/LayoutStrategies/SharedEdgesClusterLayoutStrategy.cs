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
            // Convert nodes to a list
            var nodes = graphView.nodes.ToList().OfType<UnityEventNode>().ToList();

            // Create a dictionary to track connections (adjacency list)
            var adjacencyList = new Dictionary<UnityEventNode, List<UnityEventNode>>();

            foreach (var node in nodes)
            {
                // Get all connected nodes (edges)
                var connectedNodes = GetConnectedNodes(node, graphView);
                adjacencyList[node] = connectedNodes;
            }

            // Perform clustering based on shared edges
            var clusters = PerformClustering(adjacencyList);

            // Layout each cluster
            float clusterSpacing = 500f; // Space between clusters
            float nodeSpacing = 200f; // Space between nodes within a cluster
            Vector2 currentClusterPosition = Vector2.zero;

            foreach (var cluster in clusters)
            {
                var clusterNodes = cluster.ToList();
                int nodesPerRow = Mathf.CeilToInt(Mathf.Sqrt(clusterNodes.Count));

                for (int i = 0; i < clusterNodes.Count; i++)
                {
                    var node = clusterNodes[i];
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

        // Get connected nodes (edges)
        private List<UnityEventNode> GetConnectedNodes(UnityEventNode node, EventGraphView graphView)
        {
            var connectedNodes = new List<UnityEventNode>();

            // Iterate through all edges in the graph
            foreach (var edge in graphView.edges)
            {
                if (edge.input.node == node)
                {
                    connectedNodes.Add((UnityEventNode)edge.output.node);
                }
                else if (edge.output.node == node)
                {
                    connectedNodes.Add((UnityEventNode)edge.input.node);
                }
            }

            return connectedNodes;
        }

        // Perform clustering based on shared edges
        private List<List<UnityEventNode>> PerformClustering(Dictionary<UnityEventNode, List<UnityEventNode>> adjacencyList)
        {
            var visited = new HashSet<UnityEventNode>();
            var clusters = new List<List<UnityEventNode>>();

            foreach (var node in adjacencyList.Keys)
            {
                if (!visited.Contains(node))
                {
                    var cluster = new List<UnityEventNode>();
                    DepthFirstSearch(node, adjacencyList, visited, cluster);
                    clusters.Add(cluster);
                }
            }

            return clusters;
        }

        // Depth-First Search to find connected components
        private void DepthFirstSearch(UnityEventNode node, Dictionary<UnityEventNode, List<UnityEventNode>> adjacencyList, HashSet<UnityEventNode> visited, List<UnityEventNode> cluster)
        {
            visited.Add(node);
            cluster.Add(node);

            foreach (var neighbor in adjacencyList[node])
            {
                if (!visited.Contains(neighbor))
                {
                    DepthFirstSearch(neighbor, adjacencyList, visited, cluster);
                }
            }
        }
    }
}
