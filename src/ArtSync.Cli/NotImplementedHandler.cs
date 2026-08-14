using ArtSync.Abstractions;

namespace ArtSync.Cli;

/// <summary>
/// Placeholder handler used in Increment 1 before any engine is wired up.
/// Returns exit 10 so the CLI dispatch can be tested end-to-end without SQL.
/// Replaced by real handlers in Phase 2 (schema) and Phase 3 (data).
/// </summary>
public sealed class NotImplementedHandler : IOperationHandler
{
    public OperationResult Run(CommandRequest request)
        => new(10,
            $"Operation {request.Operation} is parsed but not yet implemented. " +
            "Schema and data engines will be added in Phase 2 and Phase 3.");
}
