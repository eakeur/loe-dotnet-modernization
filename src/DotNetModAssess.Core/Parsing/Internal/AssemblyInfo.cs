using System.Runtime.CompilerServices;

// Allows the test project to exercise the internal parsing helpers (raw XML reader, solution
// discovery, Directory.Build.* walker, etc.) directly, in addition to the end-to-end coverage
// through the public BuildalyzerSolutionParser / ISolutionParser surface.
[assembly: InternalsVisibleTo("DotNetModAssess.Core.Tests")]
