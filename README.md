# dep-tree — .NET Solution Dependency Tree Builder

A C# console app that walks a .NET solution, parses every `.csproj`, and
produces a JSON data file + an interactive HTML viewer for migration assessment.

## Requirements

- .NET 8 SDK (`dotnet --version`)

## Usage

```
dotnet run -- --solution <path-to-.sln-or-folder> [options]
```

### Options

| Flag | Short | Default | Description |
|---|---|---|---|
| `--solution` | `-s` | `.` | Path to `.sln` file or root directory |
| `--output-dir` | `-o` | `.` | Where to write `*.json` and `*.html` |
| `--name` | `-n` | `dependency-tree` | Base name for output files |

### Examples

```bash
# From inside the deptree folder:
dotnet run -- -s C:\repos\MyApp\MyApp.sln
dotnet run -- -s C:\repos\MyApp -o C:\reports -n my-solution
```

## Output

- `dependency-tree.json` — full project graph data
- `dependency-tree.html` — self-contained interactive viewer (open in any browser)

## What it extracts

- Target framework(s), framework class (.NET Framework / Standard / Core / modern)
- Legacy vs SDK-style project format
- Output type, assembly name, language version, nullable context
- NuGet packages + versions (attribute and child-element style)
- Project references (with reverse "used by" graph)
- `.cs` file count, Dockerfile presence, config files (appsettings, web.config, app.config)
- **Migration risk** (High / Medium / Low) with per-project issue list
