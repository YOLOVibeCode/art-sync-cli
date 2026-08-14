# Implementation plan (analysis only)

**Status:** Recommend, do not build yet  
**Reads:** [SPEC.md](../SPEC.md), [docs/cli-examples.md](cli-examples.md)  
**Decision:** Implement ArtSync as a **.NET 8 C#** console product. Not Go, not Rust, not Node, not a sqlpackage wrapper script.

This plan is how to realize the spec. It does not change the compatibility contract.

---

## 1. What to implement it in

### 1.1 Recommendation

| Layer | Choice | Why |
| --- | --- | --- |
| Language | **C# / .NET 8 LTS** (move to 10 LTS when it is the shop default) | DacFx, SqlClient, Windows exe, Azure SQL, and this team’s stack align here. |
| App model | SDK-style console, `OutputType=Exe` | Drop-in process, no service host. |
| Schema engine | **Microsoft.SqlServer.DacFx** (library, not `sqlpackage` CLI) | Structured diffs, option objects, script vs publish. `sqlpackage` is a subprocess escape hatch only. |
| Data access | **Microsoft.Data.SqlClient** | Azure SQL, Entra, `Encrypt`, transient retries. Do not use `System.Data.SqlClient`. |
| Parser | Hand-written tokenizer in C# | Devart grammar is not POSIX. Do not use `System.CommandLine` as the Devart front door. |
| Tests | xUnit + FluentAssertions + Testcontainers.MsSql | Parser tests need no DB. Engine tests need two SQL instances. |
| Publish | `dotnet publish -r win-x64 --self-contained false` (framework-dependent) or self-contained if the scheduler host has no runtime | Copy the same output to `schemacompare.exe`, `datacompare.exe`, `dbforgesql.exe`. |
| License | MIT/Apache for our code; **DacFx remains Microsoft EULA** | Honest dual-license. Do not pretend the schema engine is OSS. |

### 1.2 Why not the alternatives

| Stack | Verdict |
| --- | --- |
| **Go** | Excellent CLIs, single static binary, fine for hashing. **No DacFx.** Schema would shell out to `sqlpackage`, losing structured diffs, option mapping, and one-process error handling. Two runtimes on the scheduler host. Reject. |
| **Rust** | Same DacFx hole, slower to ship, no team leverage. Reject. |
| **Node / TypeScript** | Weak SQL Server + Azure story, ugly Windows exe story, no DacFx. Reject. |
| **Python** | Fine for a prototype hash script, not for a Windows drop-in next to Task Scheduler. Reject. |
| **sqlpackage + PowerShell only** | Solves schema, **zero data**, different argv. That is not a Devart drop-in. Reject as the product; keep sqlpackage as an emergency schema back door. |
| **Hybrid (Go parser + C# engines)** | Two languages, two publish pipelines, no benefit. The parser is small. One C# solution. |

The original `req.txt` already called this a .NET shop. DacFx makes that call non-negotiable if schema quality is to match “replace Schema Compare.”

### 1.3 What we will not pull in

- Entity Framework / Dapper as the compare engine (Dapper is acceptable for catalog `SELECT`s if it stays thin).
- MediatR, generic host, background workers.
- `System.CommandLine` for `/source connection:"…"`.
- Excel libraries (XLS reports are out of v1).
- A GUI.

Optional later: Spectre.Console for non-quiet progress only.

---

## 2. Architecture

```text
argv ──► ArtSync.Cli (argv[0] dispatch, Environment.Exit(code))
            │
            ▼
       ArtSync.Compat        tokenize → CommandRequest
            │                argfile merge, option bag, redaction
            ▼
     ┌──────┴──────┐
     ▼             ▼
ArtSync.Schema   ArtSync.Data
  DacFx            hash stream + DML
     │             │
     └──────┬──────┘
            ▼
     ArtSync.Reporting     HTML / CSV / XML
            ▼
     ExitCode mapper       0 / 10 / 40 / 100 / 101 / 112 / …
```

`CommandRequest` is the internal model. YAML is not a user CLI. If we persist anything, it is for tests.

`ArtSync.CompFiles` stays empty until a real `.scomp`/`.dcomp` exists. The parser still accepts `/compfile` and returns 10/30.

---

## 3. Solution layout

```text
dev-art-sync-cli/
  SPEC.md
  IMPLEMENTATION.md          this file
  docs/cli-examples.md
  docs/dac-option-map.md     living Devart → DacFx table (create in Phase 2)
  ArtSync.sln
  src/
    ArtSync.Cli/             Program.cs, publish as three names
    ArtSync.Compat/
    ArtSync.Schema/
    ArtSync.Data/
    ArtSync.Reporting/
  tests/
    ArtSync.Compat.Tests/    golden argv from docs/cli-examples.md
    ArtSync.Schema.Tests/
    ArtSync.Data.Tests/
    fixtures/                .sql seed scripts, later .scomp/.scflt
```

Single `ArtSync.Cli` project. CI publish step:

```bash
dotnet publish src/ArtSync.Cli -c Release -r win-x64 -o dist/win
cp dist/win/ArtSync.dll …   # whatever the host needs
cp dist/win/ArtSync.exe dist/win/schemacompare.exe
cp dist/win/ArtSync.exe dist/win/datacompare.exe
cp dist/win/ArtSync.exe dist/win/dbforgesql.exe
```

On Unix, `argv[0]` is `schemacompare` without `.exe`. Dispatch on filename without extension, case-insensitive.

---

## 4. Module responsibilities

### 4.1 ArtSync.Compat (Phase 1 — first code)

Hand-written lexer:

- Tokens: `/switch`, `/switch:value`, `/switch:"quoted"`, grouped `param:value` after `/source` and `/target`.
- Boolean vocabulary: Yes/Y/On/True/T and No/N/Off/False/F.
- Full name + short name lookup tables copied from the spec option lists.
- Merge order: command line > argfile > (later) compfile.
- Unknown switch → `UsageError` (exit 10).
- Out-of-scope operation (`/dataexport`, `/script`, …) → 10 with operation name.
- `/activate` `/deactivate` → success 0, no work.
- Redact `password` / `Password=` before any log/report object is built.

Golden tests: every command in `docs/cli-examples.md` that is in scope must parse. The `/sync log:` forum bug must fail with 10. `/source backup:` must parse and be flagged unimplemented.

No database references in this project.

### 4.2 ArtSync.Cli

- Read `argv[0]` basename → implied operation.
- Call Compat; on failure print stderr (unless `/q`) and exit.
- Switch on operation → Schema or Data.
- Map engine result → spec exit codes (`0` after successful live apply, `100`/`101` for compare and `/sync:file`).
- `/q` silences stdout; stderr only for fatals.

### 4.3 ArtSync.Schema (Phase 2)

- `Microsoft.SqlServer.Dac.Compare.SchemaComparison` with two `SchemaCompareDatabaseEndpoint`s.
- Build `DacDeployOptions` / comparison options from the Compat option bag via `docs/dac-option-map.md`.
- Unmapped option set away from our default → exit 10, do not silently differ from Devart.
- `/sync:file` → `GenerateScript`.
- `/sync` → publish to target.
- `/filter` `.scflt`: XML load, type include/exclude, name masks.
- Azure SQL DB + `DeployDatabaseInSingleUserMode` → 10.

### 4.4 ArtSync.Data (Phase 3)

Normative algorithm is SPEC §10.2. Implementation notes:

- Catalog via `sys.tables` / `sys.views` / `sys.indexes` / `sys.columns`.
- Key: PK, else unique index. No key → skip + warning, do not invent.
- Hash SQL generated per table, executed **on each server**, `CommandBehavior.SequentialAccess`, ordered by PK.
- Merge-join two `IAsyncEnumerable<(pk, hash)>` on the host.
- Fetch payloads with `WHERE pk IN (…)` batched.
- Script: parameterized batches at apply time; `/sync:file` emits literals (sensitive).
- Apply: optional FK/trigger disable, `IDENTITY_INSERT`, bulk multi-row INSERT, SqlClient retry on transients.
- Parallelism: one table at a time in v1 (correctness first). Parallel tables in v1.1 if soak shows wall-clock pain.

Canonicalization is the risk. Budget a fixture matrix: NULL vs `''`, datetime/datetime2, float, nvarchar collation, computed, rowversion, uniqueidentifier, geography (ignore or exit 10).

### 4.5 ArtSync.Reporting

- Schema HTML + XML; data HTML + CSV.
- No XLS in v1.
- `/includeobjects` defaults to Diff.
- `/groupby:objecttype`: parse; if grouping is late, ungrouped HTML + log warning is allowed.

---

## 5. Phased work packages

Do not start Phase 2 until Phase 1 golden argv is green. Do not start Phase 3 until a schema apply round-trip works on two local SQL databases.

| Phase | Duration (one senior) | Output | Done when |
| --- | --- | --- | --- |
| **0 Inventory** | 0.5–2 days | Copy live `.bat` / argfiles / `.scomp` into `tests/fixtures/` | At least one real schema job and one real data job, or written confirmation they match public examples only |
| **1 Parser** | 3–5 days | Compat + Cli help/exitcodes/dispatch | All in-scope lines in `docs/cli-examples.md` parse; unused operations exit 10 |
| **2 Schema** | 8–12 days | DacFx compare/script/apply + HTML report + option map | SPEC §16.2; mapped v1 ignore flags |
| **3 Data** | 15–25 days | Hash compare/script/apply + CSV/HTML | SPEC §16.3; type fixture matrix |
| **4 Filters & job flags** | 3–5 days | `.scflt`, masks, remaining flags from captured jobs | Captured jobs run |
| **5 Soak** | 5–10 calendar days | Parallel Devart vs ArtSync on restored copies | Exit codes and object/row sets match; then swap exe |

Calendar: about **6–10 weeks** to a scheduler cutover if inventory arrives in week 1 and data hashing does not hit a pathological type. Data is the long pole.

### Phase 0 is still the cheapest risk cut

If live jobs are 100% `/compfile`, Phase 2/3 cannot drop in until `.scomp`/`.dcomp` are reverse-engineered. If they are connection-string jobs, Phase 1–3 drop in without XML. That single fact changes the critical path. Get the files.

---

## 6. Testing strategy

| Layer | How |
| --- | --- |
| Parser | Theory + inline data from `docs/cli-examples.md`. No IO except argfile temp files. |
| Schema | Testcontainers SQL Server ×2, or one instance two databases. Apply a known delta, assert object exists after `/sync`. |
| Data | Same containers. Seed PK tables with NULL/unicode/datetime rows. Assert row counts and values, not script text. |
| Soak | Nightly (or on-demand) against restored backups of the real pair, with Devart still licensed. |

CI: GitHub Actions `windows-latest` for publish; Linux runners can run parser tests. Engine tests need Docker SQL Server (Linux) or a self-hosted Windows agent with local SQL. Prefer Testcontainers on Linux agents for cost; validate Windows+Azure once in soak.

---

## 7. Security and ops

- Redact passwords in logs (SPEC SEC-1).
- Self-contained publish if the Task Scheduler host must not take a shared .NET runtime.
- Document target permissions: `VIEW DEFINITION` / data reader on source, `db_ddladmin` or equivalent for schema apply, `db_datawriter` for data apply.
- No installer required for v1: zip of three exe names + deps.
- Do not register a Windows service. This is a scheduled console process, like Devart.

---

## 8. Risks (architect’s list)

| Risk | Mitigation |
| --- | --- |
| DacFx ignore flags ≠ Devart ignore flags | Living map; fail closed (exit 10) on unknown explicit flags |
| Hash canonicalization bugs | Type fixture matrix before any production apply |
| `/compfile` XML undocumented | Blocked on a real file; do not guess |
| WAN + large tables | Server-side hash only; v1 serial tables; measure before parallel |
| Azure SQL vs on-prem option drift | Exit 10 for single-user and native backup |
| Exit code wrappers | Implement the compare-then-sync 100/101/0 pattern from SPEC §8 |
| “Open source” vs DacFx EULA | Document in README; data engine is the OSS value |

---

## 9. What not to do in v1

- Two-way, Change Tracking, SymmetricDS.
- New POSIX CLI as default.
- Backup/snapshot/scripts-folder endpoints.
- `/execute`, documenter, import/export.
- Rewriting a SQL parser.
- Starting with the data engine before the parser exists (no way to accept the drop-in argv).

---

## 10. Immediate next actions (when implementation is approved)

1. Confirm stack: **.NET 8 C#** (this document).
2. Drop live command lines into `tests/fixtures/` if they exist.
3. `dotnet new sln` + class libraries as in §3.
4. Implement Compat tokenizer with golden tests from `docs/cli-examples.md` only — no SQL yet.

Do not open DacFx or write MERGE SQL until the parser is boringly correct.
