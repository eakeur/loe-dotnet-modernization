using System;

namespace TextSearchSample.App;

// The scan target's name only ever appears here as a string literal argument to a reflection
// call -- it is not a `using` directive, fully-qualified type reference, member access,
// attribute, or base type, so the Roslyn syntax pass structurally cannot see it (there is no
// NameSyntax node for it anywhere in this file). Only the text-search pass, which searches raw
// line text, can find it -- and it should be reported at UsageConfidence.TextMatch, not Confirmed.
public class ReflectionUsage
{
    public Type? Resolve() => Type.GetType("System.Web.HttpContext");
}
