namespace ArtSync.Cli;

/// <summary>Static text blocks printed by /? and /exitcodes.</summary>
internal static class HelpText
{
    internal const string General = """
        ArtSync - Devart CLI Drop-in Replacement (v1)
        One-way schema and data compare/sync for SQL Server and Azure SQL.

        Usage:
          dbforgesql  /schemacompare [options]
          dbforgesql  /datacompare   [options]
          schemacompare [/schemacompare] [options]
          datacompare   [/datacompare]   [options]

        Common options:
          /source  connection:"<cs>"  |  server:<s> database:<db> [user:<u>] [password:<p>]
          /target  connection:"<cs>"  |  server:<s> database:<db> [user:<u>] [password:<p>]
          /sync               Apply sync to live target
          /sync:<file>        Write sync script only (dry-run)
          /report:<file>      Write comparison report
          /reportformat:HTML|XML|CSV
          /log:<file>         Write execution log
          /argfile:<file>     Load switches from file (CLI switches win)
          /compfile:<file>    .scomp/.dcomp project file
          /filter:<file>      .scflt object filter (schema only)
          /q                  Quiet mode

        Informational:
          /?                  This help
          /exitcodes          List exit codes

        Run '<exe> /schemacompare /?' or '<exe> /datacompare /?' for operation-specific help.
        """;

    internal const string ExitCodes = """
        ArtSync exit codes (matches Devart):

          0    Success — live /sync applied; or /activate, /exitcodes.
          2    Ctrl+Break — user cancelled.
          10   Command-line usage error — bad syntax, missing args, unsupported operation.
          11   Illegal argument duplication — conflicting or duplicate exclusive switches.
          20   Trial expired — NEVER emitted by ArtSync.
          30   Project file corrupted — /compfile unreadable or invalid.
          40   Server connection failed — cannot connect to source or target.
          100  Source and target identical — no differences found.
          101  Source and target not identical — differences exist (compare-only or /sync:file).
          105  Resource unavailable — missing file or path.
          106  I/O error — read/write failure.
          107  Failed to create report — /report could not be written.
          108  No objects to compare — no comparable objects after filters.
          112  No objects to sync — /sync requested but nothing selected.
          114  Filter file error — /filter .scflt unreadable (schema only).
        """;
}
