namespace ArtSync.Data;

/// <summary>Metadata for one column read from <c>sys.columns</c>.</summary>
internal sealed record SqlColumnInfo(
    int    ColumnId,
    string Name,
    string QuotedName,      // e.g. [CustomerId]
    string TypeName,        // e.g. int, nvarchar, datetime2 (system type, not alias)
    bool   IsNullable,
    bool   IsIdentity,
    bool   IsComputed,
    bool   IsRowguid,
    bool   IsTimestamp,     // rowversion / timestamp
    bool   IsLob,           // text, ntext, image, xml, (n)varchar(MAX)
    bool   IsPrimaryKey,
    int    KeyOrdinal = 0); // 1-based PK / unique-key ordinal; 0 if not a key column

/// <summary>Metadata for one table, with columns split into key and data groups.</summary>
internal sealed record SqlTableInfo(
    string QualifiedName,                        // e.g. [dbo].[Orders] (source name)
    IReadOnlyList<SqlColumnInfo> PkColumns,      // PK, else unique constraint/index
    IReadOnlyList<SqlColumnInfo> DataColumns,    // non-key, non-computed, non-timestamp
    string? TargetQualifiedName = null)          // mapped target name; null → same as source
{
    public string ApplyName => TargetQualifiedName ?? QualifiedName;
}
