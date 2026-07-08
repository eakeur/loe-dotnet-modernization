using System;
using System.Linq;
using System.Collections.Generic;

namespace SampleApp.Web.Utilities;

// This file deliberately does not reference System.Web, System.ServiceModel or
// SampleApp.Shared in any form -- it exists to exercise the true-negative case
// (a file that should produce zero UsageResult matches for those targets).
public class Calculator
{
    public int Add(int a, int b) => a + b;

    public int SumAll(IEnumerable<int> values) => values.Sum();
}
