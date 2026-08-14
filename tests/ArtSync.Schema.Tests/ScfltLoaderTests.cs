using ArtSync.Abstractions;
using ArtSync.Schema;
using FluentAssertions;

namespace ArtSync.Schema.Tests;

/// <summary>
/// TDD tests for ScfltLoader (SPEC §9.4).
/// Uses temp files so tests remain self-contained without a database.
/// </summary>
public sealed class ScfltLoaderTests : IDisposable
{
    private readonly string _tmpDir = Path.Combine(Path.GetTempPath(), $"scflt_{Guid.NewGuid():N}");
    private readonly ScfltLoader _loader = new();

    public ScfltLoaderTests() => Directory.CreateDirectory(_tmpDir);
    public void Dispose() => Directory.Delete(_tmpDir, recursive: true);

    // ── Attribute form ────────────────────────────────────────────────────────

    [Fact]
    public void AttributeForm_TypeOnly_ParsesCheckedTrue()
    {
        var path = Write("""
            <?xml version="1.0" encoding="utf-8"?>
            <FiltersCollection>
              <Filter ObjectName="Table" Checked="True" />
            </FiltersCollection>
            """);

        var set = _loader.Load(path);

        set.Entries.Should().HaveCount(1);
        set.Entries[0].ObjectType.Should().Be("Table");
        set.Entries[0].TypeEnabled.Should().BeTrue();
        set.Entries[0].NameMask.Should().BeNull();
    }

    [Fact]
    public void AttributeForm_TypeDisabled_ParsesCheckedFalse()
    {
        var path = Write("""
            <?xml version="1.0" encoding="utf-8"?>
            <FiltersCollection>
              <Filter ObjectName="View" Checked="False" />
            </FiltersCollection>
            """);

        var set = _loader.Load(path);
        set.Entries[0].TypeEnabled.Should().BeFalse();
    }

    [Fact]
    public void AttributeForm_WithNameMask_ParsesInclude()
    {
        var path = Write("""
            <?xml version="1.0" encoding="utf-8"?>
            <FiltersCollection>
              <Filter ObjectName="Table" Checked="True" Filter="dbo.Audit*" Include="False" />
            </FiltersCollection>
            """);

        var set = _loader.Load(path);
        set.Entries[0].NameMask.Should().Be("dbo.Audit*");
        set.Entries[0].MaskIncludes.Should().BeFalse();
    }

    [Fact]
    public void AttributeForm_MultipleEntries_ParsesAll()
    {
        var path = Write("""
            <?xml version="1.0" encoding="utf-8"?>
            <FiltersCollection>
              <Filter ObjectName="Table" Checked="True" />
              <Filter ObjectName="View" Checked="False" />
              <Filter ObjectName="StoredProcedure" Checked="True" Filter="dbo.sp_Audit*" Include="False" />
            </FiltersCollection>
            """);

        var set = _loader.Load(path);
        set.Entries.Should().HaveCount(3);
    }

    // ── Element / sub-filter form ─────────────────────────────────────────────

    [Fact]
    public void ElementForm_ParsesObjectName()
    {
        var path = Write("""
            <?xml version="1.0" encoding="utf-8"?>
            <ArrayOfSchemaFilter>
              <SchemaFilter>
                <ObjectName>Table</ObjectName>
                <Checked>true</Checked>
              </SchemaFilter>
            </ArrayOfSchemaFilter>
            """);

        var set = _loader.Load(path);
        set.Entries[0].ObjectType.Should().Be("Table");
        set.Entries[0].TypeEnabled.Should().BeTrue();
    }

    [Fact]
    public void ElementForm_WithSubFilter_ParsesMask()
    {
        var path = Write("""
            <?xml version="1.0" encoding="utf-8"?>
            <ArrayOfSchemaFilter>
              <SchemaFilter>
                <ObjectName>Table</ObjectName>
                <Checked>true</Checked>
                <SubFilters>
                  <SchemaSubFilter>
                    <Filter>dbo.Audit*</Filter>
                    <Include>false</Include>
                  </SchemaSubFilter>
                </SubFilters>
              </SchemaFilter>
            </ArrayOfSchemaFilter>
            """);

        var set = _loader.Load(path);
        // Type-level entry + sub-filter entry
        set.Entries.Should().HaveCount(2);
        var maskEntry = set.Entries.First(e => e.NameMask is not null);
        maskEntry.NameMask.Should().Be("dbo.Audit*");
        maskEntry.MaskIncludes.Should().BeFalse();
    }

    // ── Error cases ───────────────────────────────────────────────────────────

    [Fact]
    public void MissingFile_ThrowsSchemaFilterException()
    {
        var action = () => _loader.Load(Path.Combine(_tmpDir, "no_such_file.scflt"));
        action.Should().Throw<SchemaFilterException>().WithMessage("*not found*");
    }

    [Fact]
    public void InvalidXml_ThrowsSchemaFilterException()
    {
        var path = Write("not xml at all >><<");
        var action = () => _loader.Load(path);
        action.Should().Throw<SchemaFilterException>().WithMessage("*not valid XML*");
    }

    [Fact]
    public void EmptyFilterCollection_ThrowsSchemaFilterException()
    {
        var path = Write("<FiltersCollection></FiltersCollection>");
        var action = () => _loader.Load(path);
        action.Should().Throw<SchemaFilterException>().WithMessage("*no recognisable filter entries*");
    }

    // ── ObjectFilterSet.IsIncluded ────────────────────────────────────────────

    [Fact]
    public void IsIncluded_NoEntries_DefaultsToTrue()
    {
        var set = new ObjectFilterSet([]);
        set.IsIncluded("Table", "dbo.Orders").Should().BeTrue();
    }

    [Fact]
    public void IsIncluded_TypeDisabled_ReturnsFalse()
    {
        var set = new ObjectFilterSet([
            new ObjectFilterEntry("View", TypeEnabled: false, NameMask: null, MaskIncludes: false),
        ]);
        set.IsIncluded("View", "dbo.SomeView").Should().BeFalse();
    }

    [Fact]
    public void IsIncluded_TypeEnabled_NoMask_ReturnsTrue()
    {
        var set = new ObjectFilterSet([
            new ObjectFilterEntry("Table", TypeEnabled: true, NameMask: null, MaskIncludes: true),
        ]);
        set.IsIncluded("Table", "dbo.Orders").Should().BeTrue();
    }

    [Fact]
    public void IsIncluded_ExcludeMask_MatchingName_ReturnsFalse()
    {
        var set = new ObjectFilterSet([
            new ObjectFilterEntry("Table", TypeEnabled: true, NameMask: null, MaskIncludes: true),
            new ObjectFilterEntry("Table", TypeEnabled: true, NameMask: "dbo.Audit*", MaskIncludes: false),
        ]);
        set.IsIncluded("Table", "dbo.AuditLog").Should().BeFalse();
        set.IsIncluded("Table", "dbo.Orders").Should().BeTrue();
    }

    [Fact]
    public void IsIncluded_WildcardStar_MatchesMidString()
    {
        var set = new ObjectFilterSet([
            new ObjectFilterEntry("Table", TypeEnabled: true, NameMask: "*Audit*", MaskIncludes: false),
        ]);
        set.IsIncluded("Table", "dbo.SystemAuditEntries").Should().BeFalse();
        set.IsIncluded("Table", "dbo.Orders").Should().BeTrue();
    }

    [Fact]
    public void IsIncluded_BracketedNames_StripBrackets()
    {
        var set = new ObjectFilterSet([
            new ObjectFilterEntry("Table", TypeEnabled: true, NameMask: "dbo.Audit*", MaskIncludes: false),
        ]);
        // DacFx typically produces bracketed names like [dbo].[AuditLog]
        set.IsIncluded("Table", "[dbo].[AuditLog]").Should().BeFalse();
    }

    [Fact]
    public void IsIncluded_UnknownType_DefaultsToTrue()
    {
        var set = new ObjectFilterSet([
            new ObjectFilterEntry("Table", TypeEnabled: true, NameMask: null, MaskIncludes: true),
        ]);
        // "StoredProcedure" has no entry → included by default
        set.IsIncluded("StoredProcedure", "dbo.GetOrder").Should().BeTrue();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string Write(string content)
    {
        var path = Path.Combine(_tmpDir, $"{Guid.NewGuid():N}.scflt");
        File.WriteAllText(path, content);
        return path;
    }
}
