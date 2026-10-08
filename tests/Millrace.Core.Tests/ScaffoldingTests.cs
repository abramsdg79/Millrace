using System.Reflection;
using Xunit;

namespace Millrace.Core.Tests;

public class ScaffoldingTests
{
    [Fact]
    public void CoreAssemblyIsReferenceable()
    {
        Assembly core = typeof(Millrace.Io.TagValue).Assembly;
        Assert.Equal("Millrace.Io.Abstractions", core.GetName().Name);
    }
}
