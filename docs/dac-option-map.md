# DacFx option mapping — ArtSync v1

Living document.  Update whenever `DacFxOptionMap.cs` changes.
Columns: Devart canonical name | DacFx property | Status

| Devart name | Short | DacFx `DacDeployOptions` property | Status |
|---|---|---|---|
| IgnoreWhiteSpace | ispace | `IgnoreWhitespace` | ✅ mapped |
| IgnoreComments | icomment | `IgnoreComments` | ✅ mapped |
| IgnorePermissions | iperm | `IgnorePermissions` | ✅ mapped |
| IgnoreUserPermissions | iuperm | `IgnoreRoleMembership` | ✅ mapped (partial — role membership only) |
| IgnoreCollations | icollate | `IgnoreColumnCollation` | ✅ mapped (column collations only) |
| IgnoreIdentitySeedIncrementValues | iseed | `IgnoreIdentitySeed` + `IgnoreIncrement` | ✅ mapped |
| IgnoreFilegroupsPartitionSchemes | istorage | `IgnoreFilegroupPlacement` + `IgnorePartitionSchemes` + `IgnoreTablePartitionOptions` + `IgnoreObjectPlacementOnPartitionScheme` | ✅ mapped |
| IgnoreNotForReplication | ireplication | `IgnoreNotForReplication` | ✅ mapped |
| IgnoreQuotedIdentifierAndANSINulls | iquotansi | `IgnoreQuotedIdentifiers` + `IgnoreAnsiNulls` | ✅ mapped |
| IgnoreDropIndexes | idropi | `DropIndexesNotInSource = !value` | ✅ mapped |
| IgnoreDropDMLTriggers | idropt | `DropDmlTriggersNotInSource = !value` | ✅ mapped |
| IgnoreAuthorization | iauth | `IgnoreAuthorizer` | ✅ mapped |
| ForceColumnOrder | force | `IgnoreColumnOrder = !value` | ✅ mapped |
| DisableDdlTriggers | noddl | `DisableAndReenableDdlTriggers` | ✅ mapped |
| DeployDatabaseInSingleUserMode | depsingl | `DeployDatabaseInSingleUserMode` | ✅ mapped (but exit 10 on Azure SQL DB — enforced at parse level) |
| IgnoreIndexes | iindex | `IgnoreIndexOptions` + `IgnoreIndexPadding` | ⚠️ partial (no single "ignore indexes" toggle) |
| IgnoreStatistics | istat | `DropStatisticsNotInSource = !value` | ⚠️ partial |
| IgnoreTableDMLTriggers | itdmltrig | `IgnoreDmlTriggerOrder` + `IgnoreDmlTriggerState` | ⚠️ partial |
| IgnoreCase | icase | — | ⚠️ no-op (DacFx names are case-insensitive by default; body comparison ignores case via SQL Server collation) |
| IgnoreForeignKeys | ifk | post-compare `_result.Exclude` of `ForeignKeyConstraint` | ✅ mapped |
| IgnorePrimaryKeys | ipk | post-compare exclude `PrimaryKeyConstraint` | ✅ mapped |
| IgnoreUniqueKeys | iuk | post-compare exclude `UniqueConstraint` | ✅ mapped |
| IgnoreCheckConstraints | icheck | post-compare exclude `CheckConstraint` | ✅ mapped |
| IgnoreDefaultConstraints | idefault | post-compare exclude `DefaultConstraint` | ✅ mapped |
| IgnoreIdentity | iidentity | — | ⚠️ no-op |
| IgnoreWithNocheck | iwnocheck | — | ⚠️ no-op |
| IgnoreTSQLtFramework | itsqlt | — | ⚠️ no-op |
| MappingIgnoreCase | micase | — | ⚠️ no-op (DacFx maps by name case-insensitively) |
| MappingIgnoreSpaces | mispace | — | ⚠️ no-op |
| ExecuteAsSingleTransaction | tran | — | ⚠️ no-op (DacFx handles transactions internally) |
| IncludeUseDatabase | inud | — | ⚠️ no-op |
| IncludePrintComments | iprint | — | ⚠️ no-op |
| ExcludeComments | nocomments | — | ⚠️ no-op |
| AddingErrorHandling | adderrorhandle | — | ⚠️ no-op |
| CheckObjectExistence | cexist | — | ⚠️ no-op |
| QuoteObjectNames | quote | — | ⚠️ no-op (DacFx always brackets names) |

## Known gaps

- **IgnoreIndexes** — DacFx has no single "ignore all indexes" toggle; only index options/padding are suppressed. Index *objects* still appear as diffs.
- **IgnoreIdentity** — DacFx has no toggle to ignore the IDENTITY property itself (seed/increment is mapped separately).
- **ExecuteAsSingleTransaction** — DacFx wraps apply scripts in its own transaction model; there is no user-exposed single-transaction toggle.
- **DropObjectsNotInSource** — remains **false**. Extra tables on the target are not dropped unless a future explicit flag is added. This is intentional: enabling drop-by-default is unsafe for a drop-in scheduler replacement.
- **`/compfile` (`.scomp` / `.dcomp`)** — exit 10 until a captured fixture exists. Do not invent XML.

## Options that always exit 10 in v1

See `KnownOptions._unsupportedInV1`:
`CompareColumnStoreTables`, `CompareMemoryOptimizedTables`, `CompareTemporalHistoryTable`, `CompareClrTypesAsBinary`, `FileStoragePath`, `AddBackupType`, `BackupExtension`, `CreateBackupFolder`, `NeedCompressBackup`, `DropKeys`, `DropCheckConstraints`, `BackupPath`, `SynchronizeAsmViaFiles`, `VerifyTableData`, `DeployDatabaseInSingleUserMode`.
