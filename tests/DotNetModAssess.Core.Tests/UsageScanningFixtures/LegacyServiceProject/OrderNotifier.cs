using System;
using System.Web;
using System.ServiceModel;
using SampleApp.Shared;

namespace SampleApp.LegacyService.Services;

[SampleApp.Shared.LegacyMarkerAttribute]
public class OrderNotifier : SampleApp.Shared.INotifier
{
    public void Notify()
    {
        var ctx = System.Web.HttpContext.Current;
        var channel = System.ServiceModel.OperationContext.Current;
        Console.WriteLine(ctx?.ToString());
        Console.WriteLine(channel?.ToString());
    }
}
