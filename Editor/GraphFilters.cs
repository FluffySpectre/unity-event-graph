using System.Linq;
using System.Collections.Generic;

namespace FluffySpectre.UnityEventGraph 
{
    // Filter
    public interface INodeFilter
    {
        bool IsNodeVisible(UnityEventNode node);
    }

    public class InvokedNodesFilter : INodeFilter
    {
        public bool IsNodeVisible(UnityEventNode node)
        {
            return node.GetUnityEvents()
                .Any(e => EventTracker.GetEventData(e)?.InvocationCount > 0);
        }
    }

    public class GraphFilterManager
    {
        private readonly List<INodeFilter> _activeNodeFilters = new();

        public void AddNodeFilter(INodeFilter filter)
        {
            if (!_activeNodeFilters.Contains(filter))
            {
                _activeNodeFilters.Add(filter);
            }
        }

        public void RemoveNodeFilter(INodeFilter filter)
        {
            _activeNodeFilters.Remove(filter);
        }

        public bool HasNodeFilter(INodeFilter filter)
        {
            return _activeNodeFilters.Contains(filter);
        }

        public void ApplyFilters(EventGraphView graphView)
        {
            // Apply node visibility
            var visibleNodes = new HashSet<UnityEventNode>();
            foreach (var node in graphView.nodes.OfType<UnityEventNode>())
            {
                bool visible = _activeNodeFilters.All(filter => filter.IsNodeVisible(node));
                node.SetVisibility(visible);

                if (visible)
                {
                    visibleNodes.Add(node);
                }
            }

            // Apply edge visibility based on node visibility
            foreach (var edge in graphView.edges.OfType<UnityEventEdge>())
            {
                bool edgeVisible = edge.output.node is UnityEventNode outputNode &&
                                edge.input.node is UnityEventNode inputNode &&
                                visibleNodes.Contains(outputNode) &&
                                visibleNodes.Contains(inputNode);

                edge.SetVisibility(edgeVisible);
            }
        }
    }

    public static class GraphFilters 
    {
        public static INodeFilter InvokedNodesFilter => _invokedNodesFilter;
        private static InvokedNodesFilter _invokedNodesFilter = new();
    }
}
