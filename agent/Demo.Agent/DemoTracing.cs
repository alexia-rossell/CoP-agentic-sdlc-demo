using System.Diagnostics;

namespace Demo.Agent;

public static class DemoTracing
{
    public const string SourceName = "Demo.Agent";
    public static readonly ActivitySource Source = new(SourceName);
}
