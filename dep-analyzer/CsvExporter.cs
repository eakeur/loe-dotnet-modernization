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
            Map(m => m.LinesOfCode).Index(10).Name("LinesOfCode");
            Map(m => m.LinesOfCode_cs).Index(11).Name("LinesOfCode_cs");
            Map(m => m.LinesOfCode_vb).Index(12).Name("LinesOfCode_vb");
            Map(m => m.LinesOfCode_csproj).Index(13).Name("LinesOfCode_csproj");
            Map(m => m.LinesOfCode_vbproj).Index(14).Name("LinesOfCode_vbproj");
            Map(m => m.LinesOfCode_asmx).Index(15).Name("LinesOfCode_asmx");
            Map(m => m.LinesOfCode_resx).Index(16).Name("LinesOfCode_resx");
            Map(m => m.LinesOfCode_json).Index(17).Name("LinesOfCode_json");
            Map(m => m.LinesOfCode_xml).Index(18).Name("LinesOfCode_xml");
            Map(m => m.LinesOfCode_config).Index(19).Name("LinesOfCode_config");
            Map(m => m.LinesOfCode_aspx).Index(20).Name("LinesOfCode_aspx");
            Map(m => m.LinesOfCode_ascx).Index(21).Name("LinesOfCode_ascx");
            Map(m => m.LinesOfCode_razor).Index(22).Name("LinesOfCode_razor");
            Map(m => m.LinesOfCode_cshtml).Index(23).Name("LinesOfCode_cshtml");
        }
    }
}
