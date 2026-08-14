# Specification: Devart CLI Drop-in Replacement

**Status:** Draft for implementation  
**Product working name:** ArtSync  
**Date:** 2026-08-13  
**Scope:** One-way schema and data compare/sync that accepts the same command lines as dbForge Studio for SQL Server / dbForge Schema Compare / dbForge Data Compare.

This document is the requirements baseline. Informal notes in `req.txt` are superseded where they conflict. Two-way / multi-master replication is out of scope.

Primary references:

- Product: [dbForge Studio for SQL Server](https://www.devart.com/dbforge/sql/studio/)
- Schema CLI: [Command-line interface](https://docs.devart.com/studio-for-sql-server/database-compare-and-sync/schema-compare/command-line-automation/command-line-interface.html)
- Schema switches: [Command-line switches](https://docs.devart.com/studio-for-sql-server/comparing-synchronizing-schemas/additional-schemacompare-arguments.html)
- Schema options: [Command-line options](https://docs.devart.com/studio-for-sql-server/comparing-synchronizing-schemas/cmd-options-for-schemacompare.html)
- Schema exit codes: [Exit codes](https://docs.devart.com/studio-for-sql-server/comparing-synchronizing-schemas/exit-codes-used-in-cmd.html)
- Data CLI: [Command-line interface](https://docs.devart.com/studio-for-sql-server/database-compare-and-sync/data-compare/command-line-automation/command-line-interface.html)
- Data switches: [Command-line switches](https://docs.devart.com/studio-for-sql-server/comparing-synchronizing-data/additional-datacompare-arguments.html)
- Data options: [Command-line options](https://docs.devart.com/data-compare-for-sql-server/using-the-command-line/options-used-in-command-line.html)
- Data exit codes: [Exit codes](https://docs.devart.com/studio-for-sql-server/comparing-synchronizing-data/exit-codes-used-in-cmd-for-datacompare.html)

---

## 1. Purpose

Replace Devart’s command-line compare-and-sync jobs with an owned tool so that:

1. Existing `.bat` files, Task Scheduler entries, and `/argfile` texts keep working after the executable is swapped.
2. Schema and table data can be compared and synchronized **one way** (source → target) between on-premises SQL Server and Azure SQL Database / Azure SQL Managed Instance.
3. The Azure SQL Data Sync service is no longer required for this one-way path.

The GUI, AI Assistant, source control, unit tests, documenter, data generator, import/export, and other Studio features are **not** part of this product. They exist on the same `dbforgesql.com` binary in Devart’s world; a true drop-in of *Studio as an IDE* is not the goal. A true drop-in of the **compare/sync command lines** is.

---

## 2. Goals and non-goals

### 2.1 Goals

| ID | Goal |
| --- | --- |
| G1 | Accept Devart argv grammar for `/schemacompare` and `/datacompare`. |
| G2 | Ship binaries whose names can replace `schemacompare.com`, `datacompare.com`, and `dbforgesql.com` for those two operations. |
| G3 | Compare and optionally synchronize schema source → target. |
| G4 | Compare and optionally synchronize table/view data source → target. |
| G5 | Support compare-only, script-only (`/sync:file`), and apply (`/sync`). |
| G6 | Emit Devart-compatible exit codes so existing schedulers keep their meaning. |
| G7 | Write HTML/CSV (data) or HTML/XML (schema) reports and a log file. |
| G8 | Run on Windows as the drop-in host; .NET 8+ so the same build can run on macOS/Linux for development. |

### 2.2 Non-goals

| ID | Non-goal |
| --- | --- |
| NG1 | Two-way or multi-master replication, conflict resolution, Change Tracking sync, SymmetricDS, Kafka/Debezium. |
| NG2 | Byte-identical T-SQL versus Devart. The contract is **end state**: after apply, target matches source for in-scope objects/rows. |
| NG3 | Implementing the rest of Studio CLI (`/dataexport`, `/dataimport`, `/generatedata`, `/document`, `/formatsql`, `/datareport`, `/findinvalidobjects`, `/testsupport`, etc.). |
| NG4 | A Devart GUI, command-line wizard, or license activate/deactivate. |
| NG5 | Using backups, Devart snapshots, or scripts folders as compare endpoints in v1. |
| NG6 | Excel (`.xls`) reports in v1. |
| NG7 | A new POSIX-style CLI as the default interface. Optional later; not required for drop-in. |

---

## 3. Compatibility contract

### 3.1 Definition of drop-in

A command that today is:

```text
"C:\Program Files\Devart\dbForge Studio for SQL Server\dbforgesql.com" /schemacompare /source connection:"…" /target connection:"…" /sync
```

or:

```text
schemacompare.com /schemacompare /argfile:"D:\schema-args.txt" /sync
datacompare.com /datacompare /source … /target … /sync:"D:\data.sql"
```

MUST keep the same arguments. After replacing the executable (or placing ArtSync on `PATH` under the same basename), the process MUST:

1. Parse the argv the same way (see §5).
2. Perform the same **kind** of operation (compare / script / apply / report / log).
3. Return the same **numeric exit code class** (see §8).
4. Leave the target matching the source for objects and rows in scope when `/sync` without a path is used and the run succeeds.

### 3.2 What “the same thing” does not mean

- Generated SQL text need not match Devart.
- Console progress wording need not match, unless a captured production job is shown to parse stdout (then that job becomes an extra acceptance test).
- Exit code `20` (trial expired) MUST never be emitted.
- `/activate` and `/deactivate` MUST be accepted as no-ops that return `0`.

### 3.3 Precedence of inputs

Matches Devart:

1. Switches on the process command line win.
2. Then `/argfile`.
3. Then `/compfile` (`.scomp` / `.dcomp`) defaults.

Documented explicitly: *“Options specified in the command line have higher priority than the options that were given in the argfile.”*

---

## 4. Product identity

### 4.1 Binaries

One codebase, multiple file names. Dispatch on `argv[0]`:

| On-disk name | Implied operation | Notes |
| --- | --- | --- |
| `schemacompare.exe` / `schemacompare` | `/schemacompare` | Standalone dbForge Schema Compare equivalent. Operation switch may be omitted. |
| `datacompare.exe` / `datacompare` | `/datacompare` | Standalone dbForge Data Compare equivalent. Operation switch may be omitted. |
| `dbforgesql.exe` / `dbforgesql` | none | Studio equivalent. First operation switch is required. |

Windows Devart hosts are `.com` files (console). ArtSync ships `.exe`. Drop-in on a scheduler host is achieved by:

- Replacing the Devart install folder entry on `PATH`, or
- Changing the scheduled program from `….com` to `….exe` of the same basename, or
- Shipping a one-line `schemacompare.cmd` / `dbforgesql.cmd` stub if a literal `.com` path is frozen.

### 4.2 Install locations we must not assume

Devart documents two Studio paths:

- `C:\Program Files\Devart\dbForge Studio for SQL Server`
- `C:\Program Files\Devart\dbForge Edge\dbForge Studio for SQL Server`

ArtSync MUST NOT require those paths. It MUST run from any directory. Connection to SQL Server is via connection string / `server`+`database` parameters only.

### 4.3 Runtime

- .NET 8 (or current LTS) console application.
- `Microsoft.Data.SqlClient` for connectivity.
- `Microsoft.SqlServer.DacFx` for schema compare/publish (Microsoft EULA, free to use, not OSS). The wrapper, parser, data engine, and reports MAY be MIT/Apache.
- SQL Server 2016+ and Azure SQL Database / Managed Instance as supported targets. Older on-prem versions are best-effort.

---

## 5. Command-line grammar

Devart is not POSIX. Do not parse this with a stock `System.CommandLine` POSIX tokenizer as the only parser. Implement a dedicated tokenizer with golden tests.

### 5.1 Overall form

```text
<exe> [/argfile:<path>] [/<operation>] [/switch1[:value | [param1:value param2:value …]]] [/switch2 …]
```

The first argument is normally an operation switch (`/schemacompare`, `/datacompare`, …). `/argfile` may appear first; the file then contains the operation switch.

Help:

```text
<exe> /?
<exe> /<operation> /?
<exe> /<operation> /exitcodes
```

Quiet: `/quiet` or `/q` after the operation switch. Suppresses interactive prompts. Progress MAY still go to the log file.

### 5.2 Token forms the parser MUST accept

| Form | Example |
| --- | --- |
| Bare switch | `/schemacompare` `/sync` `/q` `/backup` |
| `/switch:value` | `/icase:yes` `/reportformat:HTML` |
| `/switch:"quoted value"` | `/sync:"D:\out.sql"` `/log:"D:\sync.log"` |
| Grouped parameters on one switch | `/source server:Sql1 database:db1 user:sa password:secret` |
| Connection-string parameter | `/source connection:"Data Source=…;Initial Catalog=…;User ID=…"` |
| Repeatable option switches | `/IgnoreForeignKeys:yes /MappingIgnoreCase:Yes` |
| Mask switches | `/meobjmask:dbo.Audit*` `/micolmask:ModifiedDate,CreatedOn` |

Boolean vocabulary (case-insensitive), documented by Devart:

- On: `Yes`, `Y`, `On`, `True`, `T`
- Off: `No`, `N`, `Off`, `False`, `F`

Option names have a **full name** and a **short name**. Both MUST be accepted (`IgnoreCase` and `icase`).

`/options` is documented as the way to pass those flags; in practice they also appear as `/icase:yes` directly. Accept both.

### 5.3 Unknown or unimplemented switches

| Case | Required behavior |
| --- | --- |
| Unknown switch | Exit `10`, name the switch, do not run. |
| Documented Studio operation that is out of scope (`/dataexport`, …) | Exit `10`, message: operation not supported by this replacement. |
| Documented compare switch that would change results but is not implemented (e.g. `/source backup:…`) | Exit `10`, name the switch. |
| Documented switch that is a no-op for this topology and cannot change compare/sync results | MAY be ignored with a log warning. MUST NOT be silently ignored if it would change the diff or the apply. |
| `/activate` `/deactivate` | No-op, exit `0`. |

---

## 6. Operations catalog

`dbforgesql.com` is Studio’s general automation host. Official docs document at least the following operations. ArtSync implements only the rows marked **in scope**.

| Operation | Devart purpose | ArtSync |
| --- | --- | --- |
| `/schemacompare` | Schema compare and sync | **In scope** |
| `/datacompare` | Data compare and sync | **In scope** |
| `/execute` | Run `.sql` or zip of scripts | **Phase 2 optional** (useful to apply a `/sync:file` script). Not required for drop-in of compare jobs. |
| `/script` | Script a database | Out of scope — exit `10` |
| `/scriptsfolder` | Create a scripts folder | Out of scope — exit `10` |
| `/snapshot` | Create a Devart snapshot | Out of scope — exit `10` |
| `/dataexport` | Export via `.det` | Out of scope — exit `10` |
| `/dataimport` | Import via `.dit` | Out of scope — exit `10` |
| `/generatedata` | Data generator via `.dgen` | Out of scope — exit `10` |
| `/document` | Documenter via `.ddoc` | Out of scope — exit `10` |
| `/datareport` | Data reports via `.rdb` | Out of scope — exit `10` |
| `/formatsql` | Format SQL files | Out of scope — exit `10` |
| `/findinvalidobjects` | Invalid object search | Out of scope — exit `10` |
| `/testsupport` | tSQLt unit tests | Out of scope — exit `10` |
| `/activate` `/deactivate` | License | No-op success |
| `/argfile` (alone) | Load args then dispatch | **In scope** — parse file, then dispatch |

Copy Database has no documented CLI command; it is GUI-only. No work.

---

## 7. Shared compare switches

These apply to both `/schemacompare` and `/datacompare` unless noted.

### 7.1 `/source` and `/target`

Each endpoint is one of:

| Kind | Syntax | v1 |
| --- | --- | --- |
| Live database (split) | `server:<name> database:<db> [user:<u>] [password:<p>]` | **Required.** If `user` is omitted, use integrated security. |
| Live database (ADO.NET) | `connection:"<connection string>"` | **Required.** `/password` on the command overrides Password in the string (Devart documents this pattern on other commands). |
| Snapshot | `snapshot:<file>` | Exit `10` |
| Backup | `backup:<file> [setid:<id>]` (repeatable files) | Exit `10` |
| Scripts folder | `scriptsfolder:<folder>` | Exit `10` |

Connection strings in Devart examples look like:

```text
Data Source=<server>;Initial Catalog=<db>;Integrated Security=False;User ID=<user>;Encrypt=False
```

ArtSync MUST accept standard SQL Client connection strings, including `Encrypt`, `TrustServerCertificate`, `Authentication=Active Directory …` for Azure. Integrated security when `user` is omitted.

### 7.2 `/sync[:filepath]`

| Invocation | Meaning |
| --- | --- |
| (omitted) | Compare only. Do not change the target. Do not write a sync script. |
| `/sync` | Apply synchronization to the live target. |
| `/sync:<filepath>` | Generate a T-SQL script only. Do not apply. |

This fork is load-bearing. It is how Devart implements dry-run.

### 7.3 Files

| Switch | Meaning | v1 |
| --- | --- | --- |
| `/argfile:<path>` | Text file of the same switches. CLI wins on conflict. | Required |
| `/compfile:<path>` | `.scomp` (schema) or `.dcomp` (data) project XML | Required **if** captured jobs use it; otherwise exit `10` with a request for a sample file until the parser exists |
| `/filter:<path>` | Schema object filter `.scflt` XML | Schema only; see §9.4 |
| `/report:<path>` | Comparison report | Required |
| `/reportformat:<fmt>` | See §7.4 | Required for HTML/CSV/XML; XLS → exit `10` in v1 |
| `/log:<path>` | Execution log | Required |
| `/backup[:dir]` | Backup target DB before apply | Exit `10` on Azure SQL DB. On Managed Instance / on-prem, implement only if a captured job uses it. |

### 7.4 Report formats

Schema (`/schemacompare`): `HTML` | `XLS` | `XML` | `XMLFOREXCEL`. Extension may imply format.

Data (`/datacompare`): `HTML` | `XLS` | `CSV`.

v1 MUST implement HTML and XML (schema) and HTML and CSV (data). Infer from extension when `/reportformat` is omitted.

`/includeobjects`:

- Schema: `All` | `Filtered` | `Diff` | `SelectForSync`
- Data: `All` | `Diff` | `SelectForSync`

Default for reports when unspecified: `Diff`.

Schema-only: `/ScriptDiffsStyle:<removeadd|cross>` — implement `removeadd` in v1; `cross` may fall back to `removeadd` with a log warning.

---

## 8. Exit codes

Documented for both `/schemacompare` and `/datacompare`. Schedulers that treat `100` as “nothing to do” will break if the tool always returns `0`.

| Code | Status | When ArtSync MUST return it |
| --- | --- | --- |
| 0 | Success | Successful **apply** (`/sync` with no path) after differences were synchronized. Also `/exitcodes`, `/activate`, optional `/execute`. Devart’s own PowerShell scheduler treats 0 as “sync succeeded.” |
| 2 | Ctrl+Break | User cancelled. |
| 10 | Command-line usage error | Bad syntax, missing required args, out-of-scope operation, unimplemented material switch. |
| 11 | Illegal argument duplication | Duplicate exclusive switches, or a switch used without its dependency. |
| 20 | Trial expired | **Never emit.** |
| 30 | Project file corrupted | `/compfile` unreadable or invalid. |
| 40 | Server connection fail | Cannot connect to source or target. |
| 100 | Source and target identical | Compare found no differences in scope. |
| 101 | Source and target not identical | Differences exist. Returned for **compare-only** and for `/sync:file`. After a successful live `/sync`, return **0** (see Devart scheduled-job pattern in Appendix D). |
| 105 | Resource unavailable | Missing file/path. |
| 106 | I/O error | Read/write failure (including “file already exists” if Devart treats that as 106). |
| 107 | Failed to create report | `/report` could not be written. |
| 108 | No objects to compare | No comparable objects after filters. |
| 112 | No objects to sync | `/sync` requested but nothing selected (identical, or filters/options excluded all diffs). |
| 114 | Filter file error | Schema `/filter` unreadable. Data compare docs do not list 114; do not invent it for data. |

`/exitcodes` MUST print this table and return `0`.

Observed scheduler pattern ([Devart blog](https://www.devart.com/blog/how-to-automatically-synchronize-data-in-two-sql-server-databases-on-a-schedule.html)):

1. Run compare **without** `/sync`. Treat `100` as nothing to do, `101` as diffs exist, anything else as error.
2. If `101`, run the same command **with** `/sync`. Treat `0` as apply succeeded, `100` as nothing left to do.

A 2019 blog comment mentions `/rece` returning `102` when sources are equal. That switch does **not** appear in current `/exitcodes` docs or in the published script body. Do not implement `102` unless a captured job uses `/rece`.

---

## 9. `/schemacompare` requirements

### 9.1 Functional

| ID | Requirement |
| --- | --- |
| SC-1 | Compare schema of source vs target (live databases in v1). |
| SC-2 | Include typical SQL Server objects: tables, views, procedures, functions, triggers, indexes, constraints, types, schemas, sequences, synonyms, and related objects that DacFx models. |
| SC-3 | Apply ignore/mapping options from §9.3 that are present on the command line. |
| SC-4 | Generate an update script that makes the target schema consistent with the source (`/sync:file`). |
| SC-5 | Publish that script to the live target (`/sync`). |
| SC-6 | Honor `/filter` `.scflt` when provided. |
| SC-7 | Write `/report` and `/log` as requested. |
| SC-8 | Return 100 / 101 / 108 / 112 as specified. |

### 9.2 Engine

Use `Microsoft.SqlServer.Dac.Compare.SchemaComparison` (DacFx):

- Endpoints: `SchemaCompareDatabaseEndpoint` for live databases.
- `GenerateScript` for `/sync:file`.
- `PublishChangesToTarget` (or equivalent publish) for `/sync`.
- Map Devart ignore flags onto `SchemaComparison.Options` / `DacDeployOptions`.
- Optional later: Microsoft `.scmp` files. These are **not** Devart `.scomp` files.

Do not write a SQL parser. Do not use OpenDBDiff as the engine.

### 9.3 Options — parse all, implement by mapping

The parser MUST accept every full and short name in the Studio schema options list. Implementation status:

**Must map in v1** (common, and they change diffs):

| Full name | Short | Effect |
| --- | --- | --- |
| IgnoreCase | icase | Ignore case in object bodies |
| IgnoreWhiteSpace | ispace | Ignore whitespace in bodies |
| IgnoreComments | icomment | Ignore comments in bodies |
| IgnoreCollations | icollate | Ignore collations |
| IgnorePermissions | iperm | Ignore permissions |
| IgnoreUserPermissions | iuperm | Ignore user permissions |
| IgnoreForeignKeys | ifk | Ignore FKs |
| IgnoreIndexes | iindex | Ignore indexes |
| IgnorePrimaryKeys | ipk | Ignore PKs |
| IgnoreUniqueKeys | iuk | Ignore unique keys |
| IgnoreCheckConstraints | icheck | Ignore CHECK |
| IgnoreDefaultConstraints | idefault | Ignore defaults |
| IgnoreIdentity | iidentity | Ignore IDENTITY |
| IgnoreIdentitySeedIncrementValues | iseed | Ignore seed/increment |
| IgnoreStatistics | istat | Ignore statistics |
| IgnoreFilegroupsPartitionSchemes | istorage | Ignore filegroups / partition schemes (Devart default ON) |
| IgnoreNotForReplication | ireplication | Ignore NOT FOR REPLICATION |
| IgnoreQuotedIdentifierAndANSINulls | iquotansi | Ignore quoted ident / ANSI_NULLS |
| IgnoreWithNocheck | iwnocheck | Ignore WITH NOCHECK |
| IgnoreTableDMLTriggers | itdmltrig | Ignore table DML triggers |
| IgnoreDropIndexes | idropi | Do not drop extra target indexes |
| IgnoreDropDMLTriggers | idropt | Do not drop extra target DML triggers |
| IgnoreTSQLtFramework | itsqlt | Ignore tSQLt |
| MappingIgnoreCase | micase | Name mapping |
| MappingIgnoreSpaces | mispace | Name mapping |
| ForceColumnOrder | force | Preserve column order |
| ExecuteAsSingleTransaction | tran | Wrap apply in a transaction |
| IncludeUseDatabase | inud | Emit `USE` |
| IncludePrintComments | iprint | Print comments in script |
| ExcludeComments | nocomments | Strip comments from script |
| AddingErrorHandling | adderrorhandle | Error handling in script |
| DisableDdlTriggers | noddl | Disable DDL triggers during apply |
| CheckObjectExistence | cexist | Existence checks |
| QuoteObjectNames | quote | Bracket names |
| DeployDatabaseInSingleUserMode | depsingl | **Exit 10 on Azure SQL DB**; allowed on MI / on-prem if requested |

**Parse and map if DacFx has an equivalent; otherwise exit 10 when the flag is explicitly set away from DacFx default:**

All remaining names in the official table (IgnoreAuthorization, IgnoreBoundRulesDefaults, sequence options, replication objects, DecryptEncryptedObjects, DropCreateOnly*, VerifyTableData, backup-related options, SynchronizeAsmViaFiles, UseSchemaTransfer, MappingSimilar, PopulateFullTextIndexes, …).

Backup-related schema options (`AddBackupType`, `BackupPath`, `BackupExtension`, `CreateBackupFolder`, `NeedCompressBackup`, `BackupType`) are apply-time. On Azure SQL Database they MUST exit `10`.

A living mapping table `docs/dac-option-map.md` SHALL be kept in the repo: Devart name → DacFx property → implemented | passthrough-fail | ignored-because-default.

### 9.4 `/filter` (`.scflt`)

Devart `.scflt` is XML with a `FiltersCollection`. Each entry has:

- `ObjectName` — object type
- `Checked` — type included (`True`/`False`)
- `Filter` — name mask
- `Include` — whether the mask includes or excludes

v1 MUST load this file and apply type inclusion plus name masks. Invalid file → exit `114`.

### 9.5 `/compfile` (`.scomp`)

`.scomp` is undocumented XML storing source/target, options, skipped objects, and filter. It does not store comparison results.

v1:

- If no captured job uses `/compfile`, exit `10` with a clear message.
- Once a real `.scomp` is in the repo under `tests/fixtures/`, implement a parser for that schema and treat missing/unknown nodes as exit `30`.

Do not confuse with Microsoft `.scmp`.

---

## 10. `/datacompare` requirements

### 10.1 Functional

| ID | Requirement |
| --- | --- |
| DC-1 | Compare table (and optionally view) data source vs target, keyed by primary key, else a unique constraint/index, else an explicit mapping from `/compfile` when that exists. |
| DC-2 | Refuse heaps with no usable comparison key (skip object + warning in report/log; do not invent a key). |
| DC-3 | Classify rows: only in source, only in target, different, identical. |
| DC-4 | Honor `CheckOnlyInSource`, `CheckOnlyInTarget`, `CheckDifferent`, `CheckIdentical` for what is selected into the sync set. |
| DC-5 | Generate DML that makes selected target rows match source (`/sync:file`). |
| DC-6 | Apply that DML to the live target (`/sync`). |
| DC-7 | Honor include/exclude object masks and ignore-column masks. |
| DC-8 | Write `/report` and `/log`. |
| DC-9 | Return 100 / 101 / 108 / 112 as specified. |

### 10.2 Engine (normative algorithm)

Do not pull full tables across the WAN.

1. **Discover** comparable tables/views from both catalogs. Apply `CompareTables` / `CompareViews` (defaults: tables on, views off unless set).
2. **Map** names using `MappingIgnoreCase`, `MappingIgnoreSpaces`, `MappingIgnoreUnderscores`.
3. **Exclude** objects matching `/meobjmask`; if `/miobjmask` is set, only include matches.
4. **Choose key** per pair (PK, else unique).
5. **Hash on each server:** `SELECT <pk…>, HASHBYTES('SHA2_256', <canonical payload>) … ORDER BY <pk>`. Stream `(pk, hash)` only.
6. **Merge-join** the two streams on the CLI host. Classify rows.
7. **Fetch payloads** only for keys that will be inserted or updated.
8. **Script** INSERT / UPDATE / DELETE (or MERGE) in FK-safe order. Deletes in reverse dependency order; inserts in dependency order. Circular FKs: disable FKs for the apply window when `DisableForeignKeys` is on (Devart default behavior for that option).
9. **Apply** in batches with retry on Azure transient errors (error numbers 40613, 40197, 40501, 10928, 10929, 10053, 10054, 10060, 233, 64, and `SqlException.IsTransient` when available).

Canonical payload rules:

- Respect ignore-column options (identity, rowversion/timestamp, computed, LOB, ROWGUID, temporal sys columns, column masks).
- NULL is distinct from empty string unless `IsEmptyStringEqualsNull` is on.
- Datetime comparison may ignore time if `IsIgnoreTime` is on.
- Numeric tolerance if `ToleranceInterval` is set; float rounding if `RoundFloatTypes` is on.
- String compares may ignore case / leading / trailing / internal spaces / EOL per flags.
- LOBs: if not ignored, hash `DATALENGTH` plus `HASHBYTES` of a bounded prefix; never load unbounded LOBs into CLI memory. Values > 1 MB and `FileStoragePath` (`fspath`) are deferred: exit `10` if a captured job requires `OPENROWSET` file staging.

### 10.3 Options — must implement in v1

| Full name | Short | Role |
| --- | --- | --- |
| CompareTables | tables | Include tables |
| CompareViews | views | Include views |
| CheckDifferent | chkdiff | Select different rows |
| CheckIdentical | chkequal | Include identical in results (not in sync) |
| CheckOnlyInSource | chksource | Select source-only (inserts) |
| CheckOnlyInTarget | chktarget | Select target-only (deletes) |
| IgnoreCase | icase | Value compare |
| IgnoreLeadingSpaces / IgnoreTrailingSpaces / IgnoreInternalSpaces / IgnoreEndOfLine / IgnoreWhiteSpace | ilspaces / itspaces / iispaces / ieol / ispaces | Value compare |
| IgnoreIdentityColumns | miident | Exclude from hash/DML |
| IgnoreTimestampColumns | mitime | Exclude rowversion |
| IgnoreComputedColumns | micomput | Exclude |
| IgnoreLobColumns | milob | Exclude LOBs from compare |
| IgnoreRowguidColumns | mirowguid | Exclude |
| IgnoreTemporalTableSysColumns | isyscol | Exclude |
| IsEmptyStringEqualsNull | emptyeqnull | NULL vs `''` |
| IsIgnoreTime | itime | Datetime date-only |
| MappingIgnoreCase / Spaces / Underscores | micase / mispace / miunder | Name map |
| IncludeObjectsByMask / ExcludeObjectsByMask | miobjmask / meobjmask | Object filter |
| IgnoreColumnsByMask | micolmask | Column filter |
| DisableForeignKeys | nofk | Apply wrapper |
| DisableDmlTriggers | nodml | Apply wrapper |
| DisableDdlTriggers | noddl | Apply wrapper |
| ExecuteAsSingleTransaction | tran | Apply wrapper |
| BulkInsert | bi | Multi-row INSERT |
| IncludeUseDatabase | inud | Script `USE` |
| ExcludeComments | nocomments | Script comments |
| IncludePrintComments | iprint | Print comments |
| UseSchemaNamePrefix | fullnames | Schema-qualify names |
| ReseedIdentityColumns | reseed | Reseed after apply |
| AddingErrorHandling | adderrorhandle | Script error handling |

### 10.4 Options — exit 10 if explicitly requested in v1

| Full name | Why |
| --- | --- |
| CompareColumnStoreTables / CompareMemoryOptimizedTables / CompareTemporalHistoryTable / CompareClrTypesAsBinary | Extra engine work; enable when a job needs them |
| FileStoragePath | Requires a share both sides can see |
| DeployDatabaseInSingleUserMode | Invalid on Azure SQL DB |
| AddBackupType / BackupExtension / CreateBackupFolder / NeedCompressBackup | Backup apply path |
| DropKeys / DropCheckConstraints | Destructive apply strategy; implement only if a job sets them |

### 10.5 `/compfile` (`.dcomp`)

Same policy as `.scomp`: implement once a real file is in fixtures. Until then, `/compfile` → exit `10` or `30`.

---

## 11. One-way semantics

Every run has exactly one source and one target. Source is the system of record for that run.

To update Azure from on-prem:

```text
/source …on-prem… /target …azure… /sync
```

To update on-prem from Azure, swap the two endpoints in a **separate** command. That is still one-way. ArtSync MUST NOT infer a reverse pass.

Schema and data are separate operations. A typical job is two process invocations, schema then data, because data sync assumes compatible schema.

Recommended operator order:

1. Schema compare script (`/sync:file`) and review.
2. Schema apply (`/sync`) if accepted.
3. Data compare script and review.
4. Data apply.

The tool MUST NOT silently skip review; `/sync` without a path is an explicit apply, matching Devart.

---

## 12. Platforms and Azure constraints

| Topic | Requirement |
| --- | --- |
| Source/target | SQL Server (on-prem or VM), Azure SQL Database, Azure SQL Managed Instance |
| Encryption | Default `Encrypt=true` is acceptable; honor the connection string |
| Single-user mode | Exit `10` on Azure SQL Database |
| Native backup `/backup` | Exit `10` on Azure SQL Database |
| Transient faults | Retry apply with exponential backoff |
| WAN | Hash on the servers; do not download full tables to the CLI host |
| Permissions | Document required permissions: schema read on both, DDL on target for schema sync, DML on target for data sync, `VIEW DATABASE STATE` / CDC not required |

---

## 13. Security

| ID | Requirement |
| --- | --- |
| SEC-1 | Accept passwords on the command line because Devart does. Log redaction: never write password values to `/log`, reports, or console. |
| SEC-2 | Prefer connection strings from environment variables when operators choose; not required for drop-in. |
| SEC-3 | Do not persist passwords from `/compfile` into reports. |
| SEC-4 | Scripts written by `/sync:file` MAY contain data values (INSERT literals). Treat output paths as sensitive. |
| SEC-5 | No telemetry. |

---

## 14. Logging and console

- `/log` receives a chronological trace: start time, endpoints (no secrets), options in effect, per-object counts, errors, exit code.
- Without `/q`, console MAY show progress (tables hashed, objects compared).
- With `/q`, console is silent except fatal errors to stderr.
- Do not require stdout to match Devart.

---

## 15. Repository shape (implementation)

```text
ArtSync.Cli          argv[0] dispatch, help, exit codes
ArtSync.Compat       Devart tokenizer, argfile, option bag, boolean vocabulary
ArtSync.Schema       DacFx wrapper + option map + .scflt
ArtSync.Data         discover, hash, classify, script, apply
ArtSync.Reporting    HTML/CSV/XML reports
ArtSync.CompFiles    .scomp / .dcomp parsers (when fixtures exist)
tests/golden-argv    real and documented command lines
tests/fixtures       SQL databases / dacpacs for engine tests
docs/dac-option-map.md
```

---

## 16. Acceptance tests

A release is drop-in complete for a job when all of the following pass against that job’s command line.

### 16.1 Parser (no database)

1. Every captured production command line tokenizes without exit `10` unless it uses an out-of-scope operation.
2. Documented examples from Devart schema and data CLI pages tokenize.
3. `/argfile` + CLI override: CLI value wins.
4. Boolean synonyms all parse.
5. Short and long option names are equivalent.
6. `/schemacompare /?` and `/datacompare /exitcodes` succeed.

### 16.2 Schema engine

1. Two databases identical → exit `100`, no script (or empty/no-op script), no target change.
2. Extra table on source → `/sync:file` contains CREATE; `/sync` creates it; exit `101`.
3. Extra table on target (and drop not ignored) → drop or warning per options.
4. `/IgnoreForeignKeys:yes` does not flag FK-only diffs.
5. `/sync:file` does not modify the target.
6. Broken connection → exit `40`.

### 16.3 Data engine

1. Identical tables → exit `100`.
2. Row only in source → INSERT on apply.
3. Row only in target → DELETE if `CheckOnlyInTarget` is on (Devart default includes it unless turned off).
4. Same PK, different column → UPDATE.
5. Ignored identity/rowversion columns do not count as differences.
6. Masked-out table is not compared.
7. Hash streams do not materialize full non-diff rows on the CLI host (assert via instrumentation in tests).

### 16.4 Soak versus Devart (when a license remains)

Run the same command line on Devart and ArtSync against restored copies of the same pair:

- Exit codes MUST match.
- Set of changed object names (schema) MUST match, modulo objects DacFx cannot model and that are listed as known gaps.
- Set of changed `(table, pk)` (data) MUST match.
- Script text MAY differ.

---

## 17. Phased delivery

| Phase | Deliverable | Exit criterion |
| --- | --- | --- |
| 0 | Inventory of live command lines, argfiles, `.scomp`/`.dcomp`/`.scflt` | Files checked into `tests/golden-argv` |
| 1 | Compat parser + exit codes + argv[0] dispatch | Golden argv tests green |
| 2 | `schemacompare` live-db compare / script / apply / HTML+XML report / log | Schema acceptance tests green; mapped v1 ignore flags work |
| 3 | `datacompare` hash compare / script / apply / HTML+CSV report | Data acceptance tests green |
| 4 | Filter + mask + remaining mapped options from captured jobs | Captured jobs parse and run |
| 5 | Parallel soak vs Devart; replace exe on the scheduler | Exit codes and row/object sets match |

`/execute`, `.scomp`/`.dcomp`, and backup endpoints are unscheduled until Phase 0 proves they are used.

---

## 18. Open items (block implementation details, not the spec)

1. **Paste the live command lines.** Public examples are catalogued in [docs/cli-examples.md](docs/cli-examples.md); they are not a substitute for this estate’s jobs.
2. Real `.scomp` / `.dcomp` / `.scflt` / argfile samples.
3. Whether jobs invoke `dbforgesql.com`, `schemacompare.com`, `datacompare.com`, or all three.
4. Whether stdout is parsed by a wrapper script.
5. Table sizes, LOB columns, heaps without PKs, views used as data compare sources.
6. Azure SQL Database vs Managed Instance on the cloud side (single-user and backup differ).

---

## 19. Appendix A — Documented `/schemacompare` examples that MUST parse

From Devart Studio docs:

```text
dbforgesql /schemacompare /source connection:"Data Source=…;Initial Catalog=…;Integrated Security=False;User ID=…" /target connection:"…" /MappingIgnoreSpaces:Yes /MappingIgnoreCase:Yes /sync

dbforgesql /schemacompare /argfile:"D:\FileWithArguments.txt"

dbforgesql /schemacompare /source server:SqlServer1 user:sa password:sa database:db1 /target server:SqlServer2 user:sa password:sa database:db2 /sync:"D:\compare_result.sql"

dbforgesql /schemacompare /compfile:"file_name.scomp" /icase:yes /IgnoreForeignKeys:yes /report:"report.html" /reportformat:HTML /groupby:objecttype /incsettings:T /sync

dbforgesql /schemacompare /compfile:"file_name.scomp" /log:"D:\log_file.log"
```

`/groupby:objecttype` appears in a documented example. Parser MUST accept it. If report grouping is unimplemented, HTML MAY be ungrouped with a log warning; MUST NOT exit `10` solely for this switch if a captured job uses it.

---

## 20. Appendix B — Documented `/datacompare` examples that MUST parse

```text
dbforgesql /datacompare /source connection:"…" /target connection:"…" /MappingIgnoreCase:Yes /MappingIgnoreUnderscores:Yes /sync:"C:\….sql"

datacompare.com /datacompare /compfile:"D:\workDir\DC1vsDC2.dcomp"

datacompare.com /datacompare /source connection:"…" /target connection:"…" /sync /log:"D:\sync.log"

datacompare.com /datacompare /compfile:"DC1vsDC2.dcomp" /nocomments:yes /nodml:yes /report:"report.html" /reportformat:HTML /sync

datacompare.com /datacompare /compfile:"D:\workDir\DC1vsDC2.dcomp" /fspath:"\\SqlHost\Temp" /sync
```

The last example requires `FileStoragePath`. Until implemented, that specific command exits `10` naming `/fspath`. Other examples MUST run.

---

## 21. Appendix C — Studio CLI we explicitly will not clone

These are real `dbforgesql.com` operations. A “full Studio CLI replica” would include them. This product will not.

- `/execute` (unless Phase 2 optional)
- `/dataexport` / `/dataimport` (`.det` / `.dit`, 14 formats)
- `/generatedata` (`.dgen`)
- `/document` (`.ddoc`, HTML/PDF/Markdown)
- `/datareport` (`.rdb`)
- `/formatsql`
- `/findinvalidobjects`
- `/testsupport`
- `/script` `/scriptsfolder` `/snapshot`
- Command-line wizard GUI
- AI Assistant, debugger, profiler, source control

Trying to clone that surface is a second product. Drop-in success is: **the schema and data sync command lines you already schedule keep working.**

---

## 22. Appendix D — Public CLI examples collected 2026-08-13

Independent GitHub/Stack Overflow usage of the **SQL Server** CLI is scarce. Almost every copy-pasteable command is from Devart docs, blogs, forums, or DevOps walkthroughs. The recurring shapes (parser golden tests) are listed in [docs/cli-examples.md](docs/cli-examples.md). Summary:

| Pattern | How common | v1 |
| --- | --- | --- |
| `/compfile:"….scomp\|.dcomp"` + `/sync` | Dominant for scheduled jobs | Required once a sample file exists; until then parse and exit 10/30 |
| `/source connection:"…"` `/target connection:"…"` `/sync` or `/sync:file` | Dominant for CI (Azure DevOps, Jenkins) | Required |
| `/source server:… database:… user:… password:…` | Docs + forums | Required |
| `/argfile:"….txt"` | Docs + forum (SQL Agent jobs) | Required |
| `/filter:"….scflt"` with or without `/compfile` | Docs | Required |
| `/report` + `/reportformat:HTML` + `/groupby:objecttype` | Docs + forums | Parse; HTML required |
| `/IgnorePermissions:Yes /IgnoreUserPermissions:Yes` | Documented prod/Azure-ish pattern | Required |
| Timestamped `/log` and `/sync:file` in `.bat` | Official Data Compare docs | Required |
| Compare first (100/101), then `/sync` if 101 | Official PowerShell blog | Required exit-code behavior |
| `/source backup:"….bak"` | Official Data Compare docs | Exit 10 in v1 |
| `/fspath:"\\share\Temp"` | Official Data Compare docs | Exit 10 in v1 |
| `/execute` then `/schemacompare /compfile /sync` | Jenkins blog | `/execute` optional; compare required |
| Azure DevOps `$(JSourceServer)` in connection string | Official DevOps page | Required (variable expansion is the caller’s job) |

Install paths that appear in the wild (basename dispatch must not care about folder):

- `C:\Program Files\Devart\dbForge Studio for SQL Server\dbforgesql.com`
- `C:\Program Files\Devart\dbForge Edge\dbForge Studio for SQL Server\dbforgesql.com`
- `C:\Program Files\Devart\dbForge Schema Compare for SQL Server\schemacompare.com`
- `C:\Program Files\Devart\dbForge SQL Tools Professional\dbForge Schema Compare for SQL Server\schemacompare.com`
- `C:\Program Files\Devart\dbForge SQL Tools Professional\dbForge Data Compare for SQL Server\datacompare.com`
- `C:\Program Files\Devart\Compare Bundle for SQL Server\dbForge Data Compare for SQL Server\datacompare.com`
- `C:\Program Files\Devart\Compare Bundle for SQL Server Professional\dbForge Data Compare for SQL Server\datacompare.com`
