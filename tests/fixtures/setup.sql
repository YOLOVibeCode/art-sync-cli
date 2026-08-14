-- ArtSync integration-test database setup
-- Run once after the SQL Server container is healthy.
-- Idempotent: drops and recreates both databases.
--
-- Usage:
--   sqlcmd -S localhost,1433 -U sa -P "ArtSync_Test@2026" -C -i tests/fixtures/setup.sql

USE master;
GO

-- ── Source database ───────────────────────────────────────────────────────────

IF DB_ID('artsync_src') IS NOT NULL
BEGIN
    ALTER DATABASE artsync_src SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE artsync_src;
END
CREATE DATABASE artsync_src;
GO

USE artsync_src;
GO

-- ── Tables ────────────────────────────────────────────────────────────────────

CREATE TABLE dbo.Customers (
    CustomerId   INT            NOT NULL PRIMARY KEY IDENTITY(1,1),
    Name         NVARCHAR(100)  NOT NULL,
    Email        NVARCHAR(200)  NULL,
    CreatedAt    DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE TABLE dbo.Products (
    ProductId    INT            NOT NULL PRIMARY KEY IDENTITY(1,1),
    Sku          NVARCHAR(50)   NOT NULL UNIQUE,
    Description  NVARCHAR(500)  NULL,
    Price        DECIMAL(18,4)  NOT NULL
);

CREATE TABLE dbo.Orders (
    OrderId      INT            NOT NULL PRIMARY KEY IDENTITY(1,1),
    CustomerId   INT            NOT NULL REFERENCES dbo.Customers(CustomerId),
    OrderDate    DATE           NOT NULL,
    TotalAmount  DECIMAL(18,2)  NOT NULL
);

-- Composite PK + child FK (full-sync coverage)
CREATE TABLE dbo.OrderLines (
    OrderId INT           NOT NULL,
    [LineNo] INT          NOT NULL,
    Sku     NVARCHAR(50)  NOT NULL,
    Qty     INT           NOT NULL,
    CONSTRAINT PK_OrderLines PRIMARY KEY (OrderId, [LineNo]),
    CONSTRAINT FK_OrderLines_Orders FOREIGN KEY (OrderId) REFERENCES dbo.Orders(OrderId)
);

CREATE TABLE dbo.GuidKeys (
    Id    UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    Label NVARCHAR(100)    NOT NULL
);

-- Unique constraint, no PK (SPEC DC-1 fallback)
CREATE TABLE dbo.Settings (
    SettingKey   NVARCHAR(50)  NOT NULL UNIQUE,
    SettingValue NVARCHAR(200) NULL
);

-- Heap with no usable key (SPEC DC-2 skip)
CREATE TABLE dbo.HeapEvents (
    Note NVARCHAR(100) NOT NULL
);

-- Extra table that only exists in source (used for schema-diff tests)
CREATE TABLE dbo.AuditLog (
    EventId      BIGINT         NOT NULL PRIMARY KEY IDENTITY(1,1),
    EventType    NVARCHAR(50)   NOT NULL,
    OccurredAt   DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME()
);
GO

-- ── Seed data ─────────────────────────────────────────────────────────────────

INSERT INTO dbo.Customers (Name, Email) VALUES
    (N'Alice Turing',   N'alice@example.com'),
    (N'Bob Lovelace',   N'bob@example.com'),
    (N'Carol Shannon',  NULL);

INSERT INTO dbo.Products (Sku, Description, Price) VALUES
    (N'WIDGET-A',  N'Standard Widget',   9.99),
    (N'WIDGET-B',  N'Premium Widget',   24.99),
    (N'GADGET-X',  NULL,                49.99);

INSERT INTO dbo.Orders (CustomerId, OrderDate, TotalAmount) VALUES
    (1, '2026-01-10', 9.99),
    (1, '2026-02-14', 34.98),
    (2, '2026-01-20', 49.99);

INSERT INTO dbo.OrderLines (OrderId, [LineNo], Sku, Qty) VALUES
    (1, 1, N'WIDGET-A', 1),
    (2, 1, N'WIDGET-A', 1),
    (2, 2, N'WIDGET-B', 1),
    (3, 1, N'GADGET-X', 1);

INSERT INTO dbo.GuidKeys (Id, Label) VALUES
    ('11111111-1111-1111-1111-111111111111', N'alpha');

INSERT INTO dbo.Settings (SettingKey, SettingValue) VALUES
    (N'Theme', N'dark');

INSERT INTO dbo.HeapEvents (Note) VALUES
    (N'ignored-heap');
GO


-- ── Target database ───────────────────────────────────────────────────────────

USE master;
GO

IF DB_ID('artsync_tgt') IS NOT NULL
BEGIN
    ALTER DATABASE artsync_tgt SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE artsync_tgt;
END
CREATE DATABASE artsync_tgt;
GO

USE artsync_tgt;
GO

-- Schema identical to source EXCEPT AuditLog is absent (for schema-diff tests).

CREATE TABLE dbo.Customers (
    CustomerId   INT            NOT NULL PRIMARY KEY IDENTITY(1,1),
    Name         NVARCHAR(100)  NOT NULL,
    Email        NVARCHAR(200)  NULL,
    CreatedAt    DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE TABLE dbo.Products (
    ProductId    INT            NOT NULL PRIMARY KEY IDENTITY(1,1),
    Sku          NVARCHAR(50)   NOT NULL UNIQUE,
    Description  NVARCHAR(500)  NULL,
    Price        DECIMAL(18,4)  NOT NULL
);

CREATE TABLE dbo.Orders (
    OrderId      INT            NOT NULL PRIMARY KEY IDENTITY(1,1),
    CustomerId   INT            NOT NULL REFERENCES dbo.Customers(CustomerId),
    OrderDate    DATE           NOT NULL,
    TotalAmount  DECIMAL(18,2)  NOT NULL
);

CREATE TABLE dbo.OrderLines (
    OrderId INT           NOT NULL,
    [LineNo] INT          NOT NULL,
    Sku     NVARCHAR(50)  NOT NULL,
    Qty     INT           NOT NULL,
    CONSTRAINT PK_OrderLines PRIMARY KEY (OrderId, [LineNo]),
    CONSTRAINT FK_OrderLines_Orders FOREIGN KEY (OrderId) REFERENCES dbo.Orders(OrderId)
);

CREATE TABLE dbo.GuidKeys (
    Id    UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    Label NVARCHAR(100)    NOT NULL
);

CREATE TABLE dbo.Settings (
    SettingKey   NVARCHAR(50)  NOT NULL UNIQUE,
    SettingValue NVARCHAR(200) NULL
);

CREATE TABLE dbo.HeapEvents (
    Note NVARCHAR(100) NOT NULL
);
GO

-- Seed target with SAME data as source (so data tests start from identical state).
-- CreatedAt must be specified explicitly so hash matches source exactly.

SET IDENTITY_INSERT dbo.Customers ON;
INSERT INTO dbo.Customers (CustomerId, Name, Email, CreatedAt) VALUES
    (1, N'Alice Turing',  N'alice@example.com', '2026-01-01 00:00:00.0000000'),
    (2, N'Bob Lovelace',  N'bob@example.com',   '2026-01-01 00:00:00.0000000'),
    (3, N'Carol Shannon', NULL,                  '2026-01-01 00:00:00.0000000');
SET IDENTITY_INSERT dbo.Customers OFF;

SET IDENTITY_INSERT dbo.Products ON;
INSERT INTO dbo.Products (ProductId, Sku, Description, Price) VALUES
    (1, N'WIDGET-A', N'Standard Widget', 9.99),
    (2, N'WIDGET-B', N'Premium Widget',  24.99),
    (3, N'GADGET-X', NULL,               49.99);
SET IDENTITY_INSERT dbo.Products OFF;

SET IDENTITY_INSERT dbo.Orders ON;
INSERT INTO dbo.Orders (OrderId, CustomerId, OrderDate, TotalAmount) VALUES
    (1, 1, '2026-01-10', 9.99),
    (2, 1, '2026-02-14', 34.98),
    (3, 2, '2026-01-20', 49.99);
SET IDENTITY_INSERT dbo.Orders OFF;

INSERT INTO dbo.OrderLines (OrderId, [LineNo], Sku, Qty) VALUES
    (1, 1, N'WIDGET-A', 1),
    (2, 1, N'WIDGET-A', 1),
    (2, 2, N'WIDGET-B', 1),
    (3, 1, N'GADGET-X', 1);

INSERT INTO dbo.GuidKeys (Id, Label) VALUES
    ('11111111-1111-1111-1111-111111111111', N'alpha');

INSERT INTO dbo.Settings (SettingKey, SettingValue) VALUES
    (N'Theme', N'dark');

INSERT INTO dbo.HeapEvents (Note) VALUES
    (N'ignored-heap');
GO

-- Fix the source Customers seed too — use same explicit CreatedAt values.
UPDATE artsync_src.dbo.Customers SET CreatedAt = '2026-01-01 00:00:00.0000000';
GO

-- ── TypeSampler: one row covering every major SQL type ───────────────────────
-- Used by DataTypeIntegrationTests to verify all types round-trip correctly
-- through hash → compare → script → apply.
--
-- Created in BOTH databases (same schema); source row differs from target so
-- the test can verify the full update-sync path.

USE artsync_src;
GO

CREATE TABLE dbo.TypeSampler (
    SamplerId         INT                NOT NULL PRIMARY KEY IDENTITY(1,1),
    -- Integer family
    ColBigInt         BIGINT             NULL,
    ColSmallInt       SMALLINT           NULL,
    ColTinyInt        TINYINT            NULL,
    ColBit            BIT                NULL,
    -- Numeric family
    ColDecimal        DECIMAL(18,6)      NULL,
    ColNumeric        NUMERIC(10,2)      NULL,
    ColMoney          MONEY              NULL,
    ColSmallMoney     SMALLMONEY         NULL,
    ColFloat          FLOAT              NULL,
    ColReal           REAL               NULL,
    -- Date / time family
    ColDate           DATE               NULL,
    ColTime           TIME(7)            NULL,
    ColDateTime       DATETIME           NULL,
    ColDateTime2      DATETIME2(7)       NULL,
    ColSmallDateTime  SMALLDATETIME      NULL,
    ColDateTimeOffset DATETIMEOFFSET(7)  NULL,
    -- String family
    ColChar           CHAR(10)           NULL,
    ColNChar          NCHAR(10)          NULL,
    ColVarChar        VARCHAR(200)       NULL,
    ColNVarChar       NVARCHAR(200)      NULL,
    -- Binary family
    ColBinary         BINARY(8)          NULL,
    ColVarBinary      VARBINARY(100)     NULL,
    -- Other
    ColGuid           UNIQUEIDENTIFIER   NULL
    -- NOTE: xml excluded from hash in v1 (LOB), so not in TypeSampler
);
GO

-- Source: insert one row with non-null values for every type
INSERT INTO dbo.TypeSampler (
    ColBigInt, ColSmallInt, ColTinyInt, ColBit,
    ColDecimal, ColNumeric, ColMoney, ColSmallMoney, ColFloat, ColReal,
    ColDate, ColTime, ColDateTime, ColDateTime2, ColSmallDateTime, ColDateTimeOffset,
    ColChar, ColNChar, ColVarChar, ColNVarChar,
    ColBinary, ColVarBinary, ColGuid
) VALUES (
    9223372036854775807, 32767, 255, 1,
    12345678.123456, 9999.99, 99999.9900, 214.7483, 3.14159265358979, CAST(2.71828 AS REAL),
    '2026-08-13', '19:30:00.1234567', '2026-08-13 19:30:00.000', '2026-08-13 19:30:00.1234567',
    '2026-08-13 19:30:00', '2026-08-13 19:30:00.1234567 +05:30',
    'CHAR      ', N'NCHAR     ', 'varchar value', N'nvarchar value — ñoño',
    0x0102030405060708, 0xDEADBEEF01020304,
    'A0EEBC99-9C0B-4EF8-BB6D-6BB9BD380A11'
);
GO


USE artsync_tgt;
GO

CREATE TABLE dbo.TypeSampler (
    SamplerId         INT                NOT NULL PRIMARY KEY IDENTITY(1,1),
    ColBigInt         BIGINT             NULL,
    ColSmallInt       SMALLINT           NULL,
    ColTinyInt        TINYINT            NULL,
    ColBit            BIT                NULL,
    ColDecimal        DECIMAL(18,6)      NULL,
    ColNumeric        NUMERIC(10,2)      NULL,
    ColMoney          MONEY              NULL,
    ColSmallMoney     SMALLMONEY         NULL,
    ColFloat          FLOAT              NULL,
    ColReal           REAL               NULL,
    ColDate           DATE               NULL,
    ColTime           TIME(7)            NULL,
    ColDateTime       DATETIME           NULL,
    ColDateTime2      DATETIME2(7)       NULL,
    ColSmallDateTime  SMALLDATETIME      NULL,
    ColDateTimeOffset DATETIMEOFFSET(7)  NULL,
    ColChar           CHAR(10)           NULL,
    ColNChar          NCHAR(10)          NULL,
    ColVarChar        VARCHAR(200)       NULL,
    ColNVarChar       NVARCHAR(200)      NULL,
    ColBinary         BINARY(8)          NULL,
    ColVarBinary      VARBINARY(100)     NULL,
    ColGuid           UNIQUEIDENTIFIER   NULL
);
GO

-- Seed target TypeSampler with SAME row as source so databases start identical.
-- DataTypeIntegrationTests explicitly empties this table before each test.

SET IDENTITY_INSERT dbo.TypeSampler ON;
INSERT INTO dbo.TypeSampler (
    SamplerId,
    ColBigInt, ColSmallInt, ColTinyInt, ColBit,
    ColDecimal, ColNumeric, ColMoney, ColSmallMoney, ColFloat, ColReal,
    ColDate, ColTime, ColDateTime, ColDateTime2, ColSmallDateTime, ColDateTimeOffset,
    ColChar, ColNChar, ColVarChar, ColNVarChar,
    ColBinary, ColVarBinary, ColGuid
) VALUES (
    1,
    9223372036854775807, 32767, 255, 1,
    12345678.123456, 9999.99, 99999.9900, 214.7483, 3.14159265358979, CAST(2.71828 AS REAL),
    '2026-08-13', '19:30:00.1234567', '2026-08-13 19:30:00.000', '2026-08-13 19:30:00.1234567',
    '2026-08-13 19:30:00', '2026-08-13 19:30:00.1234567 +05:30',
    'CHAR      ', N'NCHAR     ', 'varchar value', N'nvarchar value — ñoño',
    0x0102030405060708, 0xDEADBEEF01020304,
    'A0EEBC99-9C0B-4EF8-BB6D-6BB9BD380A11'
);
SET IDENTITY_INSERT dbo.TypeSampler OFF;
GO

PRINT 'Integration test databases created: artsync_src, artsync_tgt';
GO
