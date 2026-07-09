using System;
using System.Linq;
using System.Collections.Generic;

namespace SampleApp.Web.Utilities;

// This file deliberately avoids referencing (even as plain text, e.g. in a comment) any of
// the other projects' namespaces or NuGet package ids in any form -- it exists to exercise the
// true-negative case (a file that should produce zero UsageResult matches for those targets,
// from either the Roslyn syntax pass or the text-search pass).
public class Calculator
{
    public int Add(int a, int b) => a + b;

    public int SumAll(IEnumerable<int> values) => values.Sum();
}
