internal static class CheckpointTransferEndpoints
{
    public static void MapCheckpointTransfers(this WebApplication app, GameDatabase database)
    {
        var routes = app.MapGroup("/v1/towns/{id:guid}/checkpoint-uploads").WithMetadata(ApiAccess.DeviceOnly);
        routes.MapPost("", (HttpContext context, Guid id, CheckpointOffer offer) =>
            database.StartCheckpointUpload(Owner(context, id), offer, context.RequestAborted));
        routes.MapGet("/{upload:guid}", (HttpContext context, Guid id, Guid upload) =>
            database.CheckpointUploadStatus(Owner(context, id), upload, context.RequestAborted));
        routes.MapPut("/{upload:guid}", (HttpContext context, Guid id, Guid upload, int offset) =>
            database.StreamCheckpointUpload(Owner(context, id), upload, offset, context.Request));
        routes.MapDelete("/{upload:guid}", async (HttpContext context, Guid id, Guid upload) =>
        {
            await database.AbandonCheckpointUpload(Owner(context, id), upload, context.RequestAborted);
            return Results.NoContent();
        });
    }

    static DevicePrincipal Owner(HttpContext context, Guid account)
    {
        if (context.Items["device"] is not DevicePrincipal device)
            throw new GameError(401, "device-credential-required");
        if (device.Player != account)
            throw new GameError(403, "owner-only");
        return device;
    }
}

sealed record CheckpointOffer(int Length, string Sha256, long? ParentRevision = null);
sealed record CheckpointStatus(Guid Id, int Length, int Offset, long? Revision, string? Town)
{
    public bool Completed => Revision.HasValue;
}
