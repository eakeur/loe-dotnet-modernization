using System;
using System.ServiceModel;

namespace SampleApp.Wcf;

// Imports the namespace but uses none of the specific WCF idioms this detector matches - should
// only surface as the weaker "TextMatch" using-directive finding, not a Confirmed one.
public class HelperThatJustImportsTheNamespace
{
    public string Describe() => "no attributes, no ServiceHost/ChannelFactory/ClientBase here";
}
