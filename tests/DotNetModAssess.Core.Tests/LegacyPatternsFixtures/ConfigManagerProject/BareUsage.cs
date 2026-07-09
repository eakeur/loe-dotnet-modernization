using System;

namespace SampleApp.ConfigManager;

// No "using System.Configuration;" in this file - a bare ConfigurationManager access here
// should only surface as the weaker "TextMatch" finding, not Confirmed.
public class BareReader
{
    public string ReadSetting() => ConfigurationManager.AppSettings["SomeKey"] ?? string.Empty;
}
