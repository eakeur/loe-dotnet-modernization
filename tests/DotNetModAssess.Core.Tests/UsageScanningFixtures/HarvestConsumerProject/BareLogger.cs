namespace HarvestConsumer;

// A bare reference to a type harvested for a different target than BareUsage.cs's -- exercises
// that harvested suffixes stay correctly scoped per-target and don't cross-attribute.
public class BareLogger
{
    public void Method()
    {
        Logger.Instance.Log("test");
    }
}
