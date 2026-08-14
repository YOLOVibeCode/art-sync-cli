using ArtSync.Data;
using FluentAssertions;

namespace ArtSync.Data.Tests;

public sealed class SqlFkGraphTests
{
    private static readonly (string Child, string Parent)[] Edges =
    [
        ("[dbo].[Orders]",     "[dbo].[Customers]"),
        ("[dbo].[OrderLines]", "[dbo].[Orders]"),
    ];

    [Fact]
    public void ParentsFirst_CustomersBeforeOrdersBeforeLines()
    {
        var tables = new[] { "[dbo].[OrderLines]", "[dbo].[Customers]", "[dbo].[Orders]" };
        var ordered = SqlFkGraph.ParentsFirst(tables, Edges);
        ordered.Should().Equal("[dbo].[Customers]", "[dbo].[Orders]", "[dbo].[OrderLines]");
    }

    [Fact]
    public void ChildrenFirst_IsReverseOfParentsFirst()
    {
        var tables = new[] { "[dbo].[OrderLines]", "[dbo].[Customers]", "[dbo].[Orders]" };
        SqlFkGraph.ChildrenFirst(tables, Edges)
            .Should().Equal("[dbo].[OrderLines]", "[dbo].[Orders]", "[dbo].[Customers]");
    }

    [Fact]
    public void Cycle_DoesNotThrow_AndIncludesEveryTable()
    {
        var cycle = new (string Child, string Parent)[]
        {
            ("[dbo].[A]", "[dbo].[B]"),
            ("[dbo].[B]", "[dbo].[A]"),
        };
        var tables = new[] { "[dbo].[A]", "[dbo].[B]" };
        var ordered = SqlFkGraph.ParentsFirst(tables, cycle);
        ordered.Should().HaveCount(2);
        ordered.Should().Contain("[dbo].[A]").And.Contain("[dbo].[B]");
    }

    [Fact]
    public void InvolvedTables_PullsInFkNeighbours()
    {
        var involved = SqlFkGraph.InvolvedTables(["[dbo].[Customers]"], Edges);
        involved.Should().Contain("[dbo].[Orders]");
        involved.Should().Contain("[dbo].[OrderLines]");
    }
}

public sealed class SqlNameMaskTests
{
    [Theory]
    [InlineData("[dbo].[AuditLog]", "dbo.Audit*", true)]
    [InlineData("[dbo].[AuditLog]", "AuditLog",   true)]
    [InlineData("[dbo].[AuditLog]", "dbo.Cust*",  false)]
    [InlineData("[dbo].[Customers]", "dbo.Cust*", true)]
    public void TableMask_MatchesQualifiedAndUnqualified(string name, string mask, bool expected)
        => SqlNameMask.Matches(name, mask).Should().Be(expected);

    [Fact]
    public void ExcludeMask_DropsMatchingTables()
    {
        var opts = new Dictionary<string, string> { ["ExcludeObjectsByMask"] = "TypeSampler*,Heap*" };
        SqlNameMask.IsTableIncluded("[dbo].[TypeSampler]", opts).Should().BeFalse();
        SqlNameMask.IsTableIncluded("[dbo].[Customers]", opts).Should().BeTrue();
    }

    [Fact]
    public void IncludeMask_KeepsOnlyMatches()
    {
        var opts = new Dictionary<string, string> { ["IncludeObjectsByMask"] = "dbo.Cust*" };
        SqlNameMask.IsTableIncluded("[dbo].[Customers]", opts).Should().BeTrue();
        SqlNameMask.IsTableIncluded("[dbo].[Orders]", opts).Should().BeFalse();
    }

    [Fact]
    public void ColumnMask_IgnoresMatchingNames()
    {
        var opts = new Dictionary<string, string> { ["IgnoreColumnsByMask"] = "CreatedAt,Modified*" };
        SqlNameMask.IsColumnIgnored("CreatedAt", opts).Should().BeTrue();
        SqlNameMask.IsColumnIgnored("ModifiedDate", opts).Should().BeTrue();
        SqlNameMask.IsColumnIgnored("Email", opts).Should().BeFalse();
    }
}
