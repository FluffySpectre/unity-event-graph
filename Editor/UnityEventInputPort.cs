using System;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

using FluffySpectre.UnityEventGraph.PortContext;

namespace FluffySpectre.UnityEventGraph
{
    public class UnityEventInputPort : UnityEventPort
    {
        private Component _component;

        public static UnityEventPort Create(Orientation orientation, Direction direction, Capacity capacity, Type type, Component component)
        {
            var port = new UnityEventInputPort(orientation, direction, capacity, type, component);
            port.InitializeContext();
            return port;
        }

        private UnityEventInputPort(Orientation orientation, Direction direction, Capacity capacity, Type type, Component component) : base(orientation, direction, capacity, type)
        {
            _component = component;
        }

        private void InitializeContext()
        {
            if (_component == null)
                return;

            var provider = PortContextProviderRegistry.GetProviderFor(_component);
            if (provider != null)
            {
                VisualElement element = provider.GetVisualElement(_component);
                if (element != null)
                {
                    contentContainer.Add(element);

                    style.height = element.resolvedStyle.height;
                }
            }
        }
    }
}
