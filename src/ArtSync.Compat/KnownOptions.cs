namespace ArtSync.Compat;

/// <summary>
/// Registry of every Devart CLI option name (full and short) understood by ArtSync.
/// All lookups are case-insensitive. Short names map to the canonical full name.
/// </summary>
public static class KnownOptions
{
    /// <summary>
    /// Maps every known full/short name (lower-case) to its canonical full name (mixed-case).
    /// </summary>
    private static readonly Dictionary<string, string> _nameMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // ── Schema compare options (SPEC §9.3) ─────────────────────────────────
        ["ignorecase"]                          = "IgnoreCase",
        ["icase"]                               = "IgnoreCase",
        ["ignorewhitespace"]                    = "IgnoreWhiteSpace",
        ["ispace"]                              = "IgnoreWhiteSpace",
        ["ignorecomments"]                      = "IgnoreComments",
        ["icomment"]                            = "IgnoreComments",
        ["ignorecollations"]                    = "IgnoreCollations",
        ["icollate"]                            = "IgnoreCollations",
        ["ignorepermissions"]                   = "IgnorePermissions",
        ["iperm"]                               = "IgnorePermissions",
        ["ignoreuserpermissions"]               = "IgnoreUserPermissions",
        ["iuperm"]                              = "IgnoreUserPermissions",
        ["ignoreforeignkeys"]                   = "IgnoreForeignKeys",
        ["ifk"]                                 = "IgnoreForeignKeys",
        ["ignoreindexes"]                       = "IgnoreIndexes",
        ["iindex"]                              = "IgnoreIndexes",
        ["ignoreprimarykeys"]                   = "IgnorePrimaryKeys",
        ["ipk"]                                 = "IgnorePrimaryKeys",
        ["ignoreuniquekeys"]                    = "IgnoreUniqueKeys",
        ["iuk"]                                 = "IgnoreUniqueKeys",
        ["ignorecheckconstraints"]              = "IgnoreCheckConstraints",
        ["icheck"]                              = "IgnoreCheckConstraints",
        ["ignoredefaultconstraints"]            = "IgnoreDefaultConstraints",
        ["idefault"]                            = "IgnoreDefaultConstraints",
        ["ignoreidentity"]                      = "IgnoreIdentity",
        ["iidentity"]                           = "IgnoreIdentity",
        ["ignoreidentityseedincrement"]         = "IgnoreIdentitySeedIncrementValues",
        ["ignoreidentityseedincrementvalues"]   = "IgnoreIdentitySeedIncrementValues",
        ["iseed"]                               = "IgnoreIdentitySeedIncrementValues",
        ["ignorestatistics"]                    = "IgnoreStatistics",
        ["istat"]                               = "IgnoreStatistics",
        ["ignorefilegroupspartitionschemes"]    = "IgnoreFilegroupsPartitionSchemes",
        ["istorage"]                            = "IgnoreFilegroupsPartitionSchemes",
        ["ignorenotforreplication"]             = "IgnoreNotForReplication",
        ["ireplication"]                        = "IgnoreNotForReplication",
        ["ignorequotedidentifierandansinulls"]  = "IgnoreQuotedIdentifierAndANSINulls",
        ["iquotansi"]                           = "IgnoreQuotedIdentifierAndANSINulls",
        ["ignorewithnocheck"]                   = "IgnoreWithNocheck",
        ["iwnocheck"]                           = "IgnoreWithNocheck",
        ["ignoretabledmltriggers"]              = "IgnoreTableDMLTriggers",
        ["itdmltrig"]                           = "IgnoreTableDMLTriggers",
        ["ignoredropindexes"]                   = "IgnoreDropIndexes",
        ["idropi"]                              = "IgnoreDropIndexes",
        ["ignoredropdmltriggers"]               = "IgnoreDropDMLTriggers",
        ["idropt"]                              = "IgnoreDropDMLTriggers",
        ["ignoretsqltframework"]                = "IgnoreTSQLtFramework",
        ["itsqlt"]                              = "IgnoreTSQLtFramework",
        ["mappingignorecase"]                   = "MappingIgnoreCase",
        ["micase"]                              = "MappingIgnoreCase",
        ["mappingignorespaces"]                 = "MappingIgnoreSpaces",
        ["mispace"]                             = "MappingIgnoreSpaces",
        ["forcecolumnorder"]                    = "ForceColumnOrder",
        ["force"]                               = "ForceColumnOrder",
        ["executeassingletransaction"]          = "ExecuteAsSingleTransaction",
        ["tran"]                                = "ExecuteAsSingleTransaction",
        ["includeusedatabase"]                  = "IncludeUseDatabase",
        ["inud"]                                = "IncludeUseDatabase",
        ["includeprintcomments"]                = "IncludePrintComments",
        ["iprint"]                              = "IncludePrintComments",
        ["excludecomments"]                     = "ExcludeComments",
        ["nocomments"]                          = "ExcludeComments",
        ["addingerrorhandling"]                 = "AddingErrorHandling",
        ["adderrorhandle"]                      = "AddingErrorHandling",
        ["disableddltriggers"]                  = "DisableDdlTriggers",
        ["noddl"]                               = "DisableDdlTriggers",
        ["checkobjectexistence"]                = "CheckObjectExistence",
        ["cexist"]                              = "CheckObjectExistence",
        ["quoteobjectnames"]                    = "QuoteObjectNames",
        ["quote"]                               = "QuoteObjectNames",
        ["deploydatabaseinsingleusermode"]      = "DeployDatabaseInSingleUserMode",
        ["depsingl"]                            = "DeployDatabaseInSingleUserMode",

        // ── Data compare options (SPEC §10.3) ──────────────────────────────────
        ["comparetables"]                       = "CompareTables",
        ["tables"]                              = "CompareTables",
        ["compareviews"]                        = "CompareViews",
        ["views"]                               = "CompareViews",
        ["checkdifferent"]                      = "CheckDifferent",
        ["chkdiff"]                             = "CheckDifferent",
        ["checkidentical"]                      = "CheckIdentical",
        ["chkequal"]                            = "CheckIdentical",
        ["checkonlyinsource"]                   = "CheckOnlyInSource",
        ["chksource"]                           = "CheckOnlyInSource",
        ["checkonlyintarget"]                   = "CheckOnlyInTarget",
        ["chktarget"]                           = "CheckOnlyInTarget",
        ["ignoreleadingspaces"]                 = "IgnoreLeadingSpaces",
        ["ilspaces"]                            = "IgnoreLeadingSpaces",
        ["ignoretrailingspaces"]                = "IgnoreTrailingSpaces",
        ["itspaces"]                            = "IgnoreTrailingSpaces",
        ["ignoreinternalspaces"]                = "IgnoreInternalSpaces",
        ["iispaces"]                            = "IgnoreInternalSpaces",
        ["ignoreendofline"]                     = "IgnoreEndOfLine",
        ["ieol"]                                = "IgnoreEndOfLine",
        ["ispaces"]                             = "IgnoreWhiteSpace",   // data short name for IgnoreWhiteSpace
        ["ignoreidentitycolumns"]               = "IgnoreIdentityColumns",
        ["miident"]                             = "IgnoreIdentityColumns",
        ["ignoretimestampcolumns"]              = "IgnoreTimestampColumns",
        ["mitime"]                              = "IgnoreTimestampColumns",
        ["ignorecomputedcolumns"]               = "IgnoreComputedColumns",
        ["micomput"]                            = "IgnoreComputedColumns",
        ["ignorelobcolumns"]                    = "IgnoreLobColumns",
        ["milob"]                               = "IgnoreLobColumns",
        ["ignorerowguidcolumns"]                = "IgnoreRowguidColumns",
        ["mirowguid"]                           = "IgnoreRowguidColumns",
        ["ignoretemporaltablesyscolumns"]       = "IgnoreTemporalTableSysColumns",
        ["isyscol"]                             = "IgnoreTemporalTableSysColumns",
        ["isemptystringequalsnull"]             = "IsEmptyStringEqualsNull",
        ["emptyeqnull"]                         = "IsEmptyStringEqualsNull",
        ["isignoretime"]                        = "IsIgnoreTime",
        ["itime"]                               = "IsIgnoreTime",
        ["mappingignoreunderscores"]            = "MappingIgnoreUnderscores",
        ["miunder"]                             = "MappingIgnoreUnderscores",
        ["includeobjectsbymask"]                = "IncludeObjectsByMask",
        ["miobjmask"]                           = "IncludeObjectsByMask",
        ["excludeobjectsbymask"]                = "ExcludeObjectsByMask",
        ["meobjmask"]                           = "ExcludeObjectsByMask",
        ["ignorecolumnsbymask"]                 = "IgnoreColumnsByMask",
        ["micolmask"]                           = "IgnoreColumnsByMask",
        ["disableforeignkeys"]                  = "DisableForeignKeys",
        ["nofk"]                                = "DisableForeignKeys",
        ["disabledmltriggers"]                  = "DisableDmlTriggers",
        ["nodml"]                               = "DisableDmlTriggers",
        ["bulkinsert"]                          = "BulkInsert",
        ["bi"]                                  = "BulkInsert",
        ["useschemaprefix"]                     = "UseSchemaNamePrefix",
        ["useschananameprefix"]                 = "UseSchemaNamePrefix",
        ["fullnames"]                           = "UseSchemaNamePrefix",
        ["reseedidentitycolumns"]               = "ReseedIdentityColumns",
        ["reseed"]                              = "ReseedIdentityColumns",

        // ── Report / display options ────────────────────────────────────────────
        // /groupby and /incsettings appear in documented examples; not a compare option.
        // They are handled directly in the parser as non-material (warning only).

        // ── Authorization / replication options (parse-and-map or exit 10) ─────
        ["ignoreauthorization"]                 = "IgnoreAuthorization",
        ["iauth"]                               = "IgnoreAuthorization",
        ["ignoreboundrulesdefs"]                = "IgnoreBoundRulesDefs",
        ["ignoresequences"]                     = "IgnoreSequences",
        ["ignorereplicationobjects"]            = "IgnoreReplicationObjects",
        ["decryptencryptedobjects"]             = "DecryptEncryptedObjects",
        ["dropcreateonlyviewstoresprocedurest"] = "DropCreateOnly",
        ["useschematransfer"]                   = "UseSchemaTransfer",
        ["populateFulltextindexes"]             = "PopulateFullTextIndexes",

        // ── v1-unsupported options that exit 10 when explicitly set (SPEC §10.4) ─
        ["comparecolumnstoretables"]            = "CompareColumnStoreTables",
        ["comparememoryoptimizedtables"]        = "CompareMemoryOptimizedTables",
        ["comparetemporalhistorytable"]         = "CompareTemporalHistoryTable",
        ["compareclrtypesasbinary"]             = "CompareClrTypesAsBinary",
        ["filestoragepath"]                     = "FileStoragePath",
        ["fspath"]                              = "FileStoragePath",
        ["addbackuptype"]                       = "AddBackupType",
        ["backupextension"]                     = "BackupExtension",
        ["createbackupfolder"]                  = "CreateBackupFolder",
        ["needcompressbackup"]                  = "NeedCompressBackup",
        ["dropkeys"]                            = "DropKeys",
        ["dropcheckconstraints"]                = "DropCheckConstraints",

        // ── v1-unsupported schema apply options ────────────────────────────────
        ["backuppath"]                          = "BackupPath",
        ["synchronizeasmviafiles"]              = "SynchronizeAsmViaFiles",
        ["verifytabledata"]                     = "VerifyTableData",
    };

    /// <summary>
    /// Canonical names that MUST NOT be silently ignored when explicitly supplied
    /// because they would change results or apply behaviour.
    /// The parser returns exit 10 for any of these.
    /// </summary>
    private static readonly HashSet<string> _unsupportedInV1 = new(StringComparer.OrdinalIgnoreCase)
    {
        "CompareColumnStoreTables",
        "CompareMemoryOptimizedTables",
        "CompareTemporalHistoryTable",
        "CompareClrTypesAsBinary",
        "FileStoragePath",
        "AddBackupType",
        "BackupExtension",
        "CreateBackupFolder",
        "NeedCompressBackup",
        "DropKeys",
        "DropCheckConstraints",
        "BackupPath",
        "SynchronizeAsmVioFiles",
        "VerifyTableData",
        "DeployDatabaseInSingleUserMode",
    };

    /// <summary>
    /// Switches that appear in documented examples and are accepted with a log
    /// warning rather than an exit 10.  They do not materially affect compare
    /// or apply results (report grouping, embedded settings, etc.).
    /// </summary>
    private static readonly HashSet<string> _warningOnlyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "groupby",
        "incsettings",
        "scriptdiffsstyle",
        "scriptdiffsstyle",
    };

    /// <summary>
    /// Out-of-scope Studio operations that must return exit 10.
    /// </summary>
    private static readonly HashSet<string> _unsupportedOperations = new(StringComparer.OrdinalIgnoreCase)
    {
        "execute",
        "script",
        "scriptsfolder",
        "snapshot",
        "dataexport",
        "dataimport",
        "generatedata",
        "document",
        "datareport",
        "formatsql",
        "findinvalidobjects",
        "testsupport",
        "backup",
    };

    // ── Public API ────────────────────────────────────────────────────────────

    /// <returns>
    /// True when <paramref name="name"/> (full or short, any case) is in the
    /// known option registry. Sets <paramref name="canonical"/> to the
    /// canonical full name.
    /// </returns>
    public static bool TryGetCanonical(string name, out string canonical)
        => _nameMap.TryGetValue(name, out canonical!);

    public static bool IsUnsupportedInV1(string canonicalName)
        => _unsupportedInV1.Contains(canonicalName);

    public static bool IsWarningOnly(string rawSwitchName)
        => _warningOnlyNames.Contains(rawSwitchName);

    public static bool IsUnsupportedOperation(string rawSwitchName)
        => _unsupportedOperations.Contains(rawSwitchName);
}
