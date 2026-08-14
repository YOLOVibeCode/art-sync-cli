using ArtSync.Abstractions;

namespace ArtSync.Cli;

/// <summary>
/// Routes a <see cref="CommandRequest"/> to the appropriate engine handler based
/// on <see cref="CommandRequest.Operation"/>.  Unknown operation types fall
/// through to exit 10 (defensive close).
/// </summary>
internal sealed class DispatchingHandler : IOperationHandler
{
    private readonly IOperationHandler _schemaHandler;
    private readonly IOperationHandler _dataHandler;

    public DispatchingHandler(
        IOperationHandler schemaHandler,
        IOperationHandler dataHandler)
    {
        _schemaHandler = schemaHandler;
        _dataHandler   = dataHandler;
    }

    public OperationResult Run(CommandRequest request) => request.Operation switch
    {
        OperationType.SchemaCompare => _schemaHandler.Run(request),
        OperationType.DataCompare   => _dataHandler.Run(request),
        _ => new(10, $"Operation {request.Operation} is not handled by the dispatch table.")
    };
}
