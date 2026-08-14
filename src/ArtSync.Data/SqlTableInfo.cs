namespace ArtSync.Data;

/// <summary>Metadata for one column read from <c>sys.columns</c>.</summary>
internal sealed record SqlColumnInfo(
    int    ColumnId,
    string Name,
    string QuotedName,      // e.g. [CustomerId]
    string TypeName,        // e.g. int, nvarchar, datetime2
    bool   IsNullable,
    bool   IsIdentity,
    bool   IsComputed,
    bool   IsRowguid,
    bool   IsTimestamp,     // rowversion / timestamp
    bool   IsLob,           // text, ntext, image, xml, (n)varchar(MAX)
    bool   IsPrimaryKey);

/// <summary>Metadata for one table, with columns split into PK and data groups.</summary>
internal sealed record SqlTableInfo(
    string QualifiedName,                        // e.g. [dbo].[Orders]
    IReadOnlyList<SqlColumnInfo> PkColumns,
    IReadOnlyList<SqlColumnInfo> DataColumns);   // non-PK, non-skipped columns
