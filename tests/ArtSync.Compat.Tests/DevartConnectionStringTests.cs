using ArtSync.Abstractions;
using FluentAssertions;

namespace ArtSync.Compat.Tests;

public sealed class DevartConnectionStringTests
{
    private const string SchedulerShape =
        "Data Source=example.database.windows.net;Encrypt=False;Enlist=False;" +
        "Initial Catalog=ExampleDb;Integrated Security=False;Password=P@ss^#$word;" +
        "User ID=example;Pooling=False;Transaction Scope Local=True";

    [Fact]
    public void ForSqlClient_DropsTransactionScopeLocal()
    {
        var cs = DevartConnectionString.ForSqlClient(SchedulerShape);
        cs.Should().NotContain("Transaction Scope Local");
        cs.Should().Contain("Pooling=False");
        cs.Should().Contain("P@ss^#$word");
    }

    [Fact]
    public void ForSqlClient_Azure_ForcesEncrypt()
    {
        var cs = DevartConnectionString.ForSqlClient(SchedulerShape);
        cs.Should().Contain("Encrypt=True");
        cs.Should().NotContain("Encrypt=False");
    }

    [Fact]
    public void ForSqlClient_OnPrem_KeepsEncryptFalse()
    {
        var cs = DevartConnectionString.ForSqlClient(
            "Data Source=SQLHOST\\INSTANCE;Encrypt=False;Initial Catalog=ExampleDb;" +
            "User ID=u;Password=p;Transaction Scope Local=True");
        cs.Should().Contain("Encrypt=False");
        cs.Should().NotContain("Transaction Scope Local");
    }

    [Fact]
    public void ForSqlClient_StripsWrappingQuotes()
    {
        var cs = DevartConnectionString.ForSqlClient("\"Data Source=local;Initial Catalog=Db\"");
        cs.Should().StartWith("Data Source=local");
    }
}
