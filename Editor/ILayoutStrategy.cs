namespace FluffySpectre.UnityEventGraph
{
    public enum LayoutStrategyType
    {
        ForceDirectedLayout,
        GridLayout,
        RadialLayout,
        SharedEdgesClusterLayout,
        HierarchicalLayout,
    }

    public interface ILayoutStrategy
    {
        void Layout(EventGraphView graphView);
    }
}
