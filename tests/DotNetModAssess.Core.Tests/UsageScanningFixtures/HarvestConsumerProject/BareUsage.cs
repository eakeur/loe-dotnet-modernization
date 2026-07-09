using System;
using System.Web;

namespace HarvestConsumer;

// A `using` directive for the target (confirmed by Roslyn) followed by a later *bare*,
// unqualified reference to a type that belongs to that target. Roslyn cannot confirm the bare
// reference on its own, and a plain text search for the target's own name cannot find it either
// -- only a harvested-suffix search seeded by an unrelated confirmed reference elsewhere in the
// solution can.
public class BareUsage
{
    public void Method()
    {
        var ctx = HttpContext.Current;
        Console.WriteLine(ctx);
    }
}
