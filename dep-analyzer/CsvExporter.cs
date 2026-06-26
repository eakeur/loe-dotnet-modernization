using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;

namespace DepAnalyzer;

public static class CsvExporter
{
    public static void Write(IEnumerable<DependencyRow> rows, string outputPath)
    {
        using var writer = new StreamWriter(outputPath, append: false, System.Text.Encoding.UTF8);
        using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
        });

        csv.Context.RegisterClassMap<DependencyRowMap>();
        csv.WriteRecords(rows);
    }

    private sealed class DependencyRowMap : ClassMap<DependencyRow>
    {
        public DependencyRowMap()
        {
            Map(m => m.Name).Index(0).Name("Name");
            Map(m => m.Type).Index(1).Name("Type");
            Map(m => m.Version).Index(2).Name("Version");
            Map(m => m.TargetFramework).Index(3).Name("TargetFramework");
            Map(m => m.SupportsNet8).Index(4).Name("SupportsNet8");
            Map(m => m.PackageFrameworks).Index(5).Name("PackageFrameworks");
            Map(m => m.InternalProjectDependencies).Index(6).Name("InternalProjectDependencies");
            Map(m => m.IsTestProject).Index(7).Name("IsTestProject");
            Map(m => m.ProjectFormat).Index(8).Name("ProjectFormat");
            Map(m => m.Level).Index(9).Name("Level");
        }
    }
}
