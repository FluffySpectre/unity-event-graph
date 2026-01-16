using System.Linq;
using System.Collections.Generic;

namespace FluffySpectre.UnityEventGraph 
{
    public interface INodeFilter
    {
        bool IsNodeVisible(UnityEventNode node);
    }

    public class InvokedNodesFilter : INodeFilter
    {
        public bool IsNodeVisible(UnityEventNode node)
        {
            foreach (var e in node.GetUnityEvents())
            {
                var data = EventTracker.GetEventData(e);
                if (data != null && data.InvocationCount > 0)
                {
                    return true;
                }
            }
            return false;
        }
    }

    public class GraphFilterManager
    {
        private readonly List<INodeFilter> _activeNodeFilters = new();
        private readonly HashSet<UnityEventNode> _visibleNodesCache = new();
        private bool _cacheValid = false;

        public void AddNodeFilter(INodeFilter filter)
        {
            if (!_activeNodeFilters.Contains(filter))
            {
                _activeNodeFilters.Add(filter);
                InvalidateCache();
            }
        }

        public void RemoveNodeFilter(INodeFilter filter)
        {
            if (_activeNodeFilters.Remove(filter))
            {
                InvalidateCache();
            }
        }

        public bool HasNodeFilter(INodeFilter filter)
        {
            return _activeNodeFilters.Contains(filter);
        }

        public void InvalidateCache()
        {
            _cacheValid = false;
            _visibleNodesCache.Clear();
        }

        public void ApplyFilters(EventGraphView graphView)
        {
            if (graphView == null) return;

            _visibleNodesCache.Clear();
            
            // If no filters, all nodes are visible
            bool hasFilters = _activeNodeFilters.Count > 0;
            
            // Get nodes once to avoid repeated enumeration
            var allNodes = graphView.nodes.OfType<UnityEventNode>().ToList();
            var allEdges = graphView.edges.OfType<UnityEventEdge>().ToList();

            // Apply node visibility
            foreach (var node in allNodes)
            {
                bool visible;
                if (hasFilters)
                {
                    visible = true;
                    foreach (var filter in _activeNodeFilters)
                    {
                        if (!filter.IsNodeVisible(node))
                        {
                            visible = false;
                            break;
                        }
                    }
                }
                else
                {
                    visible = true;
                }

                node.SetVisibility(visible);

                if (visible)
                {
                    _visibleNodesCache.Add(node);
                }
            }

            // Apply edge visibility based on node visibility
            foreach (var edge in allEdges)
            {
                var outputNode = edge.output?.node as UnityEventNode;
                var inputNode = edge.input?.node as UnityEventNode;

                bool edgeVisible = outputNode != null && 
                                   inputNode != null &&
                                   _visibleNodesCache.Contains(outputNode) &&
                                   _visibleNodesCache.Contains(inputNode);

                edge.SetVisibility(edgeVisible);
            }

            _cacheValid = true;
        }
    }

    public static class GraphFilters 
    {
        public static INodeFilter InvokedNodesFilter => _invokedNodesFilter;
        private static readonly InvokedNodesFilter _invokedNodesFilter = new();
    }
}
