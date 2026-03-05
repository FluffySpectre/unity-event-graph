namespace FluffySpectre.UnityEventGraph
{
    public enum LayoutStrategyType
    {
        ForceDirectedLayout,
        GridLayout,
        RadialLayout,
        HierarchicalLayout,
    }

    public interface ILayoutStrategy
    {
        void Layout(EventGraphView graphView);
    }
}
