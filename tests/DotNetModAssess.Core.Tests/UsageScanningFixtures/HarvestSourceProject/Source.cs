using System;

namespace HarvestSource;

// Confirmed, fully-qualified references elsewhere in the solution -- these are the seeds that
// the suffix-harvest pass reads its per-target vocabulary from.
public class Source
{
    public void Method()
    {
        var ctx = System.Web.HttpContext.Current;
        var logger = Acme.Utilities.Logger.Instance;
        Console.WriteLine(ctx?.ToString());
        Console.WriteLine(logger?.ToString());
    }
}
