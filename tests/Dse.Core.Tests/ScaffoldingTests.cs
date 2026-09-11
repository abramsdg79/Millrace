using System.Reflection;
using Xunit;

namespace Dse.Core.Tests;

public class ScaffoldingTests
{
    [Fact]
    public void CoreAssemblyIsReferenceable()
    {
        Assembly core = typeof(Dse.Io.Placeholder).Assembly;
        Assert.Equal("Dse.Io.Abstractions", core.GetName().Name);
    }
}
