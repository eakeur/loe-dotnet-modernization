using System;
using System.Configuration;

namespace SampleApp.ConfigManager;

public class ConfirmedReader
{
    public string ReadSetting()
    {
        var value = ConfigurationManager.AppSettings["SomeKey"];
        var qualified = System.Configuration.ConfigurationManager.ConnectionStrings["Db"];
        Console.WriteLine(qualified?.ToString());
        return value ?? string.Empty;
    }
}
