# ArtSync

> Developed by **[YOLOVibeCode](https://github.com/YOLOVibeCode)**, a subsidiary of **Noxirsoft**.

A free, open-source drop-in replacement for the Devart dbForge Schema Compare and Data Compare CLI tools (`schemacompare.com`, `datacompare.com`, `dbforgesql.com`).

ArtSync accepts the same command-line grammar as Devart, emits the same exit codes, and performs one-way source→target schema and data synchronization — without a Devart license.

---

## Why

Devart's scheduled-job command lines look like this:

```text
schemacompare.com /schemacompare /source connection:"…" /target connection:"…" /sync
datacompare.com /datacompare /argfile:"D:\jobs\dc-prod.txt"
```

ArtSync replaces the executables. Your `.bat` files, Task Scheduler entries, and argfiles keep working unchanged.

---

## Status

| Phase | Deliverable | Status |
|---|---|---|
| 1 | Devart argv parser + CLI dispatch | ✅ Complete |
| 2 | Schema compare / sync via DacFx | ✅ Complete |
| 3 | Data compare / sync via server-side hash streams | ✅ Complete (engine interfaces; live SQL in progress) |
| 4 | `.scflt` object filter support | ✅ Complete |
| 5 | Soak vs Devart; scheduler exe deployment | 🔄 In progress |

---

## Getting started

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download) or later
- SQL Server 2016+ or Azure SQL Database / Managed Instance

### Build and run tests

```bash
dotnet test
```

### Publish the three exe names

**Windows (PowerShell):**

```powershell
.\scripts\publish.ps1
# Output: publish\artsync-win-x64\schemacompare.exe
#                                   datacompare.exe
#                                   dbforgesql.exe
```

**Linux / macOS:**

```bash
./scripts/publish.sh linux-x64
# Output: publish/artsync-linux-x64/schemacompare
#                                    datacompare
#                                    dbforgesql
```

### Drop-in on Windows scheduler

Replace the Devart exe on `PATH`, or update the scheduled task program from `….com` to `….exe` of the same basename.

---

## Usage

ArtSync accepts the same argv grammar as Devart. Examples:

```text
# Schema compare only (exit 100 = identical, 101 = differences)
schemacompare.exe /source connection:"…" /target connection:"…"

# Schema apply
schemacompare.exe /source connection:"…" /target connection:"…" /sync

# Schema script to file
schemacompare.exe /source server:Srv1 database:db1 /target server:Srv2 database:db2 /sync:"C:\out.sql"

# Data compare with argfile
datacompare.exe /datacompare /argfile:"D:\jobs\dc-prod.txt"

# Schema compare with object filter
dbforgesql.exe /schemacompare /source connection:"…" /target connection:"…" /filter:"C:\filters\schema.scflt"
```

**Exit codes** match Devart exactly. Run `/exitcodes` to print the table:

```text
schemacompare.exe /exitcodes
```

---

## Architecture

```
ArtSync.Abstractions   Small interfaces + CommandRequest records
ArtSync.Compat         Devart argv tokenizer, argfile, redaction
ArtSync.Cli            argv[0] dispatch, help, exit codes
ArtSync.Schema         DacFx schema engine behind ISchemaCompare
ArtSync.Data           Hash-stream data engine behind IDataCompare
ArtSync.Reporting      HTML / CSV / XML report writers (planned)
```

All engine code sits behind thin interfaces. `ArtSync.Cli` depends only on `IArgvParser` and `IOperationHandler` — it never imports DacFx or SqlClient directly.

---

## Compatibility

| Feature | Status |
|---|---|
| `/schemacompare` compare + script + apply | ✅ |
| `/datacompare` compare + script + apply | ✅ (interfaces; live hash engine in progress) |
| `/source connection:"…"` | ✅ |
| `/source server:… database:… user:… password:…` | ✅ |
| `/argfile` | ✅ |
| `/filter:<.scflt>` (schema) | ✅ |
| `/sync` / `/sync:<file>` | ✅ |
| `/report` + `/reportformat:HTML\|XML\|CSV` | 🔄 Planned |
| `/compfile` (`.scomp` / `.dcomp`) | 🔄 Planned (exit 10 until a real file is in fixtures) |
| `/source backup:…` | ❌ Exit 10 — out of scope in v1 |
| Azure SQL Database | ✅ |
| Azure SQL Managed Instance | ✅ |
| SQL Server 2016+ | ✅ |

---

## Licenses

- **ArtSync source** — [MIT](LICENSE)
- **Microsoft.SqlServer.DacFx** — Microsoft EULA (free to use, not OSS). Included as a NuGet dependency.
- **Microsoft.Data.SqlClient** — MIT

---

## About

ArtSync is a project by **YOLOVibeCode**, a subsidiary of **Noxirsoft**.

---

## Contributing

Issues and PRs welcome. Please open an issue before large changes to align on scope — this tool intentionally does **not** aim to clone all of dbForge Studio, only the compare/sync command lines.
