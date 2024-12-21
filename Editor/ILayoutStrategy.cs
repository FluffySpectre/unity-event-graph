namespace FluffySpectre.UnityEventGraph
{
    public enum LayoutStrategyType
    {
        ForceDirectedLayout,
        GridLayout,
        RadialLayout,
        SharedEdgesClusterLayout,
    }

    public interface ILayoutStrategy
    {
        void Layout(EventGraphView graphView);
    }
}
