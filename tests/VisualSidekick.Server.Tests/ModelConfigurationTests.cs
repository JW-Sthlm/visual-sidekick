using VisualSidekick.Server;
using Xunit;

namespace VisualSidekick.Server.Tests;

public sealed class ModelConfigurationTests
{
    [Fact]
    public void ResolvePrefersExplicitModel()
    {
        Assert.Equal("custom-vision-model", ModelConfiguration.Resolve(" custom-vision-model "));
    }

    [Fact]
    public void ResolveUsesEnvironmentModel()
    {
        var previous = Environment.GetEnvironmentVariable(ModelConfiguration.EnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(ModelConfiguration.EnvironmentVariable, "environment-model");
            Assert.Equal("environment-model", ModelConfiguration.Resolve(null));
        }
        finally
        {
            Environment.SetEnvironmentVariable(ModelConfiguration.EnvironmentVariable, previous);
        }
    }
}
