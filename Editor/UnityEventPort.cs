using System;
using UnityEditor.Experimental.GraphView;

namespace FluffySpectre.UnityEventGraph
{
    public class UnityEventPort : Port
    {
        public string FullPortName { get; set; }

        public static UnityEventPort Create(Orientation orientation, Direction direction, Capacity capacity, Type type)
        {
            return new UnityEventPort(orientation, direction, capacity, type);
        }

        public UnityEventPort(Orientation orientation, Direction direction, Capacity capacity, Type type) : base(orientation, direction, capacity, type)
        {
        }
    }
}
