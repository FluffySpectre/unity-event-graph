using System.Linq;
using UnityEngine;

namespace FluffySpectre.UnityEventGraph.LayoutStrategies
{
    // Radial Layout Strategy
    public class RadialLayoutStrategy : ILayoutStrategy
    {
        public void Layout(EventGraphView graphView)
        {
            var nodes = graphView.nodes.ToList().OfType<UnityEventNode>().ToList();

            int nodeCount = nodes.Count;
            if (nodeCount == 0)
                return;

            float nodeSize = 200f;
            float spacingFactor = 2f;
            float circumference = nodeCount * nodeSize * spacingFactor;
            float radius = circumference / (2 * Mathf.PI);

            float centerX = 0f;
            float centerY = 0f;

            for (int i = 0; i < nodeCount; i++)
            {
                var node = nodes[i];
                float angle = (360f / nodeCount) * i;
                float radian = angle * Mathf.Deg2Rad;

                float x = centerX + radius * Mathf.Cos(radian);
                float y = centerY + radius * Mathf.Sin(radian);

                node.SetPosition(new Rect(new Vector2(x, y), node.GetPosition().size));
            }
        }
    }
}
