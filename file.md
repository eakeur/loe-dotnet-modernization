Implement a project dependency analyzer for a .NET modernization/migration project. using and simple C# Console application that reads a solution or solution filter and lists all of the dependencies of every project in that solution/solution filter. 


## Application
A .NET/C# console application that reads a solution or solution filter, lists all of the dependencies (projects and package) of every project in that solution/solution filter, as well as other metadata. The projects in the solution can be in many .NET versions (net48, net8, netstandard), contain csproj files or packages.config depending on the version. 

The output of the application is a CSV file with the dependency analysis. The analysis should contain the following information for each dependency identified:
- Dependency name (fully qualified name)
- Dependency type (nuget package/project reference)
- Version (optional)
- Supports .NET 8 on (optional) - can be checked by pinging the artifactory for that dependency
- Number of internal projects it depends on
- IsTestProject
- Is csproj in the SDK format? In which format it is
- Level - The level of that dependency in the dependency tree. this must be analyzed by the application. Packages that do not depend on anything are '0', packages that depend in L0 packages are '1', packages that depend on L1 packages are '2', and so on.

## Constraints for the app behavior
- The app should receive the solution/solution filter file path as a parameter
- The app should work regardless of the version of the projects in the solution targeted (from .NET48 until .NET 10)
- The app should not modify anything in the solution or files it'll read


## Constraints for building the app
- For scaffolding the app, and adding any packages needed, prefer the dotnet CLI instead of writing csproj files. This saves tokens and assures the output is right
- Seaqrch the internet for articles that talk about .NET migration or dependency analysis and for libs that can read these sort of information more properly. Do NOT invent the wheel
- The app should be easy to run with dotnet run
- Build and verify before completion