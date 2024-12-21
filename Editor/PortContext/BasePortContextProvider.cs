using UnityEngine;
using UnityEngine.UIElements;

namespace FluffySpectre.UnityEventGraph.PortContext
{
    public abstract class BasePortContextProvider : IPortContextProvider
    {
        public abstract bool CanProvideFor(Component component);

        public virtual VisualElement GetVisualElement(Component component)
        {
            var container = new VisualElement()
            {
                style =
                {
                    paddingLeft = 5,
                    paddingTop = 5,
                    paddingBottom = 5,
                    paddingRight = 5,
                    borderBottomLeftRadius = 5,
                    borderBottomRightRadius = 5,
                    borderTopLeftRadius = 5,
                    borderTopRightRadius = 5,
                    color = new StyleColor(new Color(1f, 1f, 1f, 0.6f)),
                    backgroundColor = new StyleColor(new Color(0.1f, 0.1f, 0.1f, 0.6f))
                }
            };
            return container;
        }
    }
}
