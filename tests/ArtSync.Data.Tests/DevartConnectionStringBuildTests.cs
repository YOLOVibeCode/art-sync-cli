using ArtSync.Abstractions;
using ArtSync.Data;
using FluentAssertions;

namespace ArtSync.Data.Tests;

public sealed class DevartConnectionStringBuildTests
{
    [Fact]
    public void BuildCs_SchedulerAzureString_IsSqlClientSafe()
    {
        var ep = new Endpoint(
            EndpointKind.ConnectionString,
            ConnectionString:
                "Data Source=example.database.windows.net;Encrypt=False;Enlist=False;" +
                "Initial Catalog=ExampleDb;Integrated Security=False;Password=P@ss^#$word;" +
                "User ID=example;Pooling=False;Transaction Scope Local=True");

        var cs = SqlDataCompare.BuildCs(ep);
        cs.Should().NotContain("Transaction Scope Local");
        cs.Should().Contain("Encrypt=True");
        cs.Should().Contain("P@ss^#$word");
    }
}
