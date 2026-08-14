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
GO

-- Seed target with SAME data as source (so data tests start from identical state).

SET IDENTITY_INSERT dbo.Customers ON;
INSERT INTO dbo.Customers (CustomerId, Name, Email) VALUES
    (1, N'Alice Turing',  N'alice@example.com'),
    (2, N'Bob Lovelace',  N'bob@example.com'),
    (3, N'Carol Shannon', NULL);
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
GO

PRINT 'Integration test databases created: artsync_src, artsync_tgt';
GO
