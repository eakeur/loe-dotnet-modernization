using System;

namespace SampleApp.Domains;

public class DomainManager
{
    public void CreateAndUnload()
    {
        var domain = AppDomain.CreateDomain("PluginDomain");
        var current = AppDomain.CurrentDomain;
        Console.WriteLine(current.BaseDirectory);
        AppDomain.Unload(domain);
    }
}

public class RemoteProxy : MarshalByRefObject
{
    public void Ping() => Console.WriteLine("pong");
}
