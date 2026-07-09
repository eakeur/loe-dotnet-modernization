namespace HarvestConsumer;

// True negative: nothing in this file relates to any scan target or harvested suffix. Must
// produce zero results from any of the three passes.
public class Unrelated
{
    public void Method()
    {
        var value = 42;
        var name = "just some unrelated string";
    }
}
