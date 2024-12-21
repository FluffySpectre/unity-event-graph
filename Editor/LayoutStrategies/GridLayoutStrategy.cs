using System.Linq;
using UnityEngine;

namespace FluffySpectre.UnityEventGraph.LayoutStrategies
{
    // Grid Layout Strategy
    public class GridLayoutStrategy : ILayoutStrategy
    {
        public void Layout(EventGraphView graphView)
        {
            var nodes = graphView.nodes.ToList().OfType<UnityEventNode>().ToList();

            // Order nodes first by nodes which have no input ports, then nodes which have input ports and output ports and finally nodes which have no output ports
            nodes = nodes.OrderBy(n => n.inputContainer.childCount == 0 ? 0 : n.outputContainer.childCount == 0 ? 2 : 1).ToList();

            float horizontalSpacing = 300f;
            float verticalSpacing = 200f;

            int nodesPerRow = Mathf.CeilToInt(Mathf.Sqrt(nodes.Count) * 1.5f);
            nodesPerRow = Mathf.Min(nodesPerRow, 50);

            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                int row = i / nodesPerRow;
                int column = i % nodesPerRow;

                float x = column * horizontalSpacing;
                float y = row * verticalSpacing;

                node.SetPosition(new Rect(new Vector2(x, y), node.GetPosition().size));
            }
        }
    }
}
