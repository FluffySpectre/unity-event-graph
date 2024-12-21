using System.Collections.Generic;
using UnityEngine;

namespace FluffySpectre.UnityEventGraph 
{
    public class NodeGraphData : ScriptableObject
    {
        public string sceneName;
        public List<NodePosition> nodePositions = new();

        [System.Serializable]
        public class NodePosition
        {
            public string nodeId;
            public Vector2 position;
        }

        public Vector2? GetNodePosition(string nodeId)
        {
            var node = nodePositions.Find(n => n.nodeId == nodeId);
            return node != null ? (Vector2?)node.position : null;
        }

        public void SetNodePosition(string nodeId, Vector2 position)
        {
            var node = nodePositions.Find(n => n.nodeId == nodeId);
            if (node == null)
            {
                nodePositions.Add(new NodePosition { nodeId = nodeId, position = position });
            }
            else
            {
                node.position = position;
            }
        }
    }
}
