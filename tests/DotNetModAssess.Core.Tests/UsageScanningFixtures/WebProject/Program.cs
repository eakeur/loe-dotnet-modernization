using System;
using SampleApp.Shared;

namespace SampleApp.Web;

public class Program
{
    public static void Main()
    {
        SampleApp.Shared.OrderDto order = new SampleApp.Shared.OrderDto();
        Console.WriteLine(order);
    }
}
