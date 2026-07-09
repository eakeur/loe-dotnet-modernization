using System.Web;

namespace TextSearchSample.App;

// A plain `using` directive for the scan target package, confirmed by the Roslyn pass at line 1.
// The text-search pass also matches that same line, so this exercises de-duplication: the
// occurrence must not be reported a second time at lower confidence.
public class ConfirmedUsage
{
}
