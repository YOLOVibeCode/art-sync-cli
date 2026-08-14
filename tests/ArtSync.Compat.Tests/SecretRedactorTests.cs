using ArtSync.Compat;
using FluentAssertions;

namespace ArtSync.Compat.Tests;

public sealed class SecretRedactorTests
{
    private static readonly SecretRedactor Redactor = new();

    [Theory]
    [InlineData(
        "Data Source=srv;Initial Catalog=db;Integrated Security=False;User ID=u;Password=s3cr3t;Pooling=False",
        "Data Source=srv;Initial Catalog=db;Integrated Security=False;User ID=u;Password=***;Pooling=False")]
    [InlineData(
        "Data Source=srv;PWD=hunter2;User ID=u",
        "Data Source=srv;PWD=***;User ID=u")]
    [InlineData(
        "Data Source=srv;Integrated Security=True",
        "Data Source=srv;Integrated Security=True")]
    public void Redact_ConnectionString_MasksPassword(string input, string expected)
    {
        Redactor.Redact(input).Should().Be(expected);
    }

    [Fact]
    public void RedactArgv_MasksSwitchPassword()
    {
        var argv = new[] { "/schemacompare", "/password:hunter2", "/sync" };
        var redacted = Redactor.RedactArgv(argv);

        redacted[1].Should().Be("/password:***");
        redacted[0].Should().Be("/schemacompare");
        redacted[2].Should().Be("/sync");
    }

    [Fact]
    public void RedactArgv_MasksEndpointSubParamPassword()
    {
        var argv = new[] { "/source", "server:S1", "user:sa", "password:hunter2", "database:db" };
        var redacted = Redactor.RedactArgv(argv);

        redacted[3].Should().Be("password:***");
    }

    [Fact]
    public void RedactArgv_MasksConnectionStringPasswordInlineValue()
    {
        var argv = new[]
        {
            "/datacompare",
            "/source",
            "connection:Data Source=srv;User ID=u;Password=s3cr3t;Initial Catalog=db",
        };
        var redacted = Redactor.RedactArgv(argv);

        redacted[2].Should().Contain("Password=***");
        redacted[2].Should().NotContain("s3cr3t");
    }

    [Fact]
    public void RedactArgv_DoesNotMutateOriginal()
    {
        var argv = new[] { "password:hunter2" };
        var original = argv[0];
        _ = Redactor.RedactArgv(argv);
        argv[0].Should().Be(original);
    }
}
