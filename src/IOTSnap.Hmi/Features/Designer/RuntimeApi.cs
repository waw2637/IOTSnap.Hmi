using IOTSnap.Hmi.Data;
using IOTSnap.Hmi.Data.Entities;
using IOTSnap.Hmi.Runtime.OpcUa;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IOTSnap.Hmi.Features.Designer;

public static class RuntimeApi
{
    public static IEndpointRouteBuilder MapRuntimeApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/hmi/runtime")
            .RequireAuthorization();

        group.MapGet("/screens/{slug}", GetPublishedScreenAsync);
        group.MapPost("/write/{nodeId}", WriteTagAsync);
        return app;
    }

    private static async Task<IResult> WriteTagAsync(
        string nodeId,
        [FromBody] WriteTagRequest request,
        IOpcUaRuntime runtime,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Value))
        {
            return Results.BadRequest("A value is required.");
        }

        var result = await runtime.WriteTagAsync(nodeId, request.Value, cancellationToken);
        return result.Succeeded ? Results.Ok(new { result.Message, result.UpdatedUtc }) : Results.BadRequest(new { result.Message, result.UpdatedUtc });
    }

    private static async Task<IResult> GetPublishedScreenAsync(
        string slug,
        IDbContextFactory<HmiDbContext> dbFactory,
        IOpcUaRuntime runtime,
        CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var screen = await db.HmiScreens
            .AsNoTracking()
            .Include(x => x.Widgets)
                .ThenInclude(x => x.Bindings)
            .FirstOrDefaultAsync(x => x.Slug == slug && x.IsPublished, cancellationToken);

        if (screen is null)
        {
            return Results.NotFound();
        }

        var tags = await runtime.GetTagSnapshotsAsync(cancellationToken);
        var tagLookup = tags.ToDictionary(x => x.NodeId, StringComparer.OrdinalIgnoreCase);

        var widgets = screen.Widgets
            .OrderBy(x => x.ZIndex)
            .Select(x =>
            {
                var bindingValues = x.Bindings
                    .Select(b => new RuntimeBindingValueDto(
                        b.BindingRole,
                        b.SourceType,
                        b.SourceKey,
                        b.WriteRequiresConfirm,
                        b.MinRole,
                        x.PropertiesJson,
                        tagLookup.TryGetValue(b.SourceKey, out var tag)
                            ? new RuntimeTagSnapshotDto(
                                tag.NodeId,
                                tag.ValueText,
                                tag.StatusCode,
                                tag.IsStale,
                                tag.HasAlarm,
                                tag.AlarmText,
                                tag.UpdatedUtc)
                            : null))
                    .ToList();

                return new RuntimeWidgetDto(
                    x.Key,
                    x.WidgetType,
                    x.Title,
                    x.X,
                    x.Y,
                    x.Width,
                    x.Height,
                    x.ZIndex,
                    x.PropertiesJson,
                    bindingValues);
            })
            .ToList();

        var payload = new RuntimeScreenPayloadDto(
            screen.Name,
            screen.Slug,
            screen.Width,
            screen.Height,
            screen.PublishedUtc,
            widgets);

        return Results.Ok(payload);
    }

    private sealed record RuntimeScreenPayloadDto(
        string Name,
        string Slug,
        int Width,
        int Height,
        DateTimeOffset? PublishedUtc,
        IReadOnlyList<RuntimeWidgetDto> Widgets);

    private sealed record RuntimeWidgetDto(
        string Key,
        string WidgetType,
        string Title,
        int X,
        int Y,
        int Width,
        int Height,
        int ZIndex,
        string PropertiesJson,
        IReadOnlyList<RuntimeBindingValueDto> Bindings);

    private sealed record RuntimeBindingValueDto(
        string BindingRole,
        string SourceType,
        string SourceKey,
        bool WriteRequiresConfirm,
        string MinRole,
        string PropertiesJson,
        RuntimeTagSnapshotDto? TagSnapshot);

    private sealed record WriteTagRequest(string Value);

    private sealed record RuntimeTagSnapshotDto(
        string NodeId,
        string? ValueText,
        string? StatusCode,
        bool IsStale,
        bool HasAlarm,
        string AlarmText,
        DateTimeOffset UpdatedUtc);
}
