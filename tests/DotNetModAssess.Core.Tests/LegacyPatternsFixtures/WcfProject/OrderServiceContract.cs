using System;
using System.ServiceModel;

namespace SampleApp.Wcf;

[ServiceContract]
public interface IOrderService
{
    [OperationContract]
    void PlaceOrder(int orderId);
}

[DataContract]
public class OrderDto
{
    [DataMember]
    public int OrderId { get; set; }
}

public class OrderServiceClient : ClientBase<IOrderService>
{
    public void Call()
    {
        var host = new ServiceHost(typeof(IOrderService));
        var factory = new ChannelFactory<IOrderService>("orderServiceEndpoint");
        Console.WriteLine(host.ToString() + factory.ToString());
    }
}
