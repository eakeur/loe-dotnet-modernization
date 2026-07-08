using System.Configuration;
using System.Web;
using Newtonsoft.Json;

namespace Legacy.Net48App
{
    public class LegacyService
    {
        public string GetSetting(string key)
        {
            return ConfigurationManager.AppSettings[key];
        }

        public string Serialize(object value)
        {
            return JsonConvert.SerializeObject(value);
        }

        public string GetServerVariable(HttpContext context, string name)
        {
            return context?.Request?.ServerVariables[name];
        }
    }
}
