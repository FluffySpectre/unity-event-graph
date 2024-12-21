using UnityEngine;
using UnityEngine.UIElements;

namespace FluffySpectre.UnityEventGraph.PortContext
{
    public interface IPortContextProvider
    {
        bool CanProvideFor(Component component);
        VisualElement GetVisualElement(Component component);
    }
}
