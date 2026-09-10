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
            .RequireAuthorization()
            .DisableAntiforgery();

        group.MapGet("/screens", GetPublishedScreensAsync);
        group.MapGet("/screens/{slug}", GetPublishedScreenAsync);
        group.MapGet("/alarms", GetAlarmsAsync);
        group.MapGet("/alarms/history", GetAlarmHistoryAsync);
        group.MapGet("/trends/{nodeId}", GetTrendAsync);
        group.MapPost("/alarms/{nodeId}/acknowledge", AcknowledgeAlarmAsync);
        group.MapPost("/write/{nodeId}", WriteTagAsync);
        return app;
    }

    private static async Task<IResult> GetAlarmsAsync(IOpcUaRuntime runtime, CancellationToken cancellationToken)
    {
        var alarms = await runtime.GetAlarmsAsync(includeCleared: false, cancellationToken);
        var payload = alarms.Select(x => new RuntimeAlarmDto(
            x.NodeId,
            x.DisplayName,
            x.Area,
            x.StatusCode,
            x.AlarmText,
            x.Severity,
            x.IsActive,
            x.IsAcknowledged,
            x.LastRaisedUtc,
            x.AcknowledgedUtc,
            x.AcknowledgedBy)).ToList();

        return Results.Ok(payload);
    }

    private static async Task<IResult> GetTrendAsync(string nodeId, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, int? maxPoints, IDbContextFactory<HmiDbContext> dbFactory, CancellationToken cancellationToken)
    {
        var end = toUtc ?? DateTimeOffset.UtcNow;
        var start = fromUtc ?? end.AddHours(-1);
        if (string.IsNullOrWhiteSpace(nodeId) || start > end || end - start > TimeSpan.FromDays(31))
        {
            return Results.BadRequest("Use a node ID and a range no greater than 31 days.");
        }

        var limit = Math.Clamp(maxPoints ?? 300, 2, 1000);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var samples = await db.OpcUaTrendSamples.AsNoTracking()
            .Where(x => x.NodeId == nodeId)
            .ToListAsync(cancellationToken);
        var range = samples
            .Where(x => x.SampledUtc >= start && x.SampledUtc <= end)
            .OrderBy(x => x.SampledUtc)
            .Select(x => new RuntimeTrendSampleDto(x.ValueText, x.StatusCode, x.SampledUtc))
            .ToList();
        return Results.Ok(RuntimeTrendDownsampling.TakeEvenly(range, limit));
    }

    private static async Task<IResult> GetAlarmHistoryAsync(DateTimeOffset? fromUtc, DateTimeOffset? toUtc, int? take, IDbContextFactory<HmiDbContext> dbFactory, CancellationToken cancellationToken)
    {
        var end = toUtc ?? DateTimeOffset.UtcNow;
        var start = fromUtc ?? end.AddDays(-7);
        if (start > end || end - start > TimeSpan.FromDays(31)) return Results.BadRequest("Use a range no greater than 31 days.");
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var history = await db.OpcUaAlarmTransitions.AsNoTracking().ToListAsync(cancellationToken);
        return Results.Ok(history
            .Where(x => x.OccurredUtc >= start && x.OccurredUtc <= end)
            .OrderByDescending(x => x.OccurredUtc)
            .Take(Math.Clamp(take ?? 200, 1, 1000))
            .ToList());
    }

    private static async Task<IResult> AcknowledgeAlarmAsync(
        string nodeId,
        [FromBody] AcknowledgeAlarmRequest request,
        HttpContext httpContext,
        IDbContextFactory<HmiDbContext> dbFactory,
        IOpcUaRuntime runtime,
        RuntimeAuditService auditService,
        CancellationToken cancellationToken)
    {
        var actor = RuntimeAccessPolicy.ResolveActor(httpContext.User);
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var bindings = await db.HmiWidgetBindings
            .AsNoTracking()
            .Where(x => x.SourceKey == nodeId && x.Widget.Key == request.WidgetKey && x.Widget.Screen.Slug == request.ScreenSlug)
            .Select(x => new RuntimeBindingAccess(
                x.Widget.Screen.Slug,
                x.Widget.Key,
                x.BindingRole,
                x.SourceType,
                x.SourceKey,
                x.MinRole,
                x.Widget.Screen.IsPublished,
                true))
            .ToListAsync(cancellationToken);

        var target = new RuntimeActionTarget(request.ScreenSlug, request.WidgetKey, request.BindingRole, nodeId);
        if (RuntimeAccessPolicy.ResolveAlarmAccess(bindings, target, actor.Role) is null)
        {
            await auditService.RecordAsync(
                actor,
                "AlarmAcknowledged",
                $"{target.ScreenSlug}/{target.WidgetKey}/{target.BindingRole}/{target.NodeId}",
                "Denied",
                null,
                "The authenticated role is not authorized for this published binding.",
                cancellationToken);
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var result = await runtime.AcknowledgeAlarmAsync(nodeId, actor.Username, cancellationToken);
        await auditService.RecordAsync(
            actor,
            "AlarmAcknowledged",
            nodeId,
            result.Succeeded ? "Succeeded" : "Failed",
            null,
            result.Message,
            cancellationToken);
        return result.Succeeded ? Results.Ok(new { result.Message, result.UpdatedUtc }) : Results.BadRequest(new { result.Message, result.UpdatedUtc });
    }

    private static async Task<IResult> WriteTagAsync(
        string nodeId,
        [FromBody] WriteTagRequest request,
        HttpContext httpContext,
        IDbContextFactory<HmiDbContext> dbFactory,
        RuntimeCommandService commandService,
        RuntimeAuditService auditService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Value) || string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest("A value and idempotency key are required.");
        }

        var actor = RuntimeAccessPolicy.ResolveActor(httpContext.User);
        if (actor is null)
        {
            return Results.Unauthorized();
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var nodeIsWritable = await db.OpcUaNodeMappings
            .AsNoTracking()
            .AnyAsync(x => x.NodeId == nodeId && x.IsWritable, cancellationToken);

        var bindings = await db.HmiWidgetBindings
            .AsNoTracking()
            .Where(x => x.SourceKey == nodeId && x.Widget.Key == request.WidgetKey && x.Widget.Screen.Slug == request.ScreenSlug)
            .Select(x => new RuntimeBindingAccess(
                x.Widget.Screen.Slug,
                x.Widget.Key,
                x.BindingRole,
                x.SourceType,
                x.SourceKey,
                x.MinRole,
                x.Widget.Screen.IsPublished,
                nodeIsWritable,
                x.WriteRequiresConfirm))
            .ToListAsync(cancellationToken);

        var target = new RuntimeActionTarget(request.ScreenSlug, request.WidgetKey, request.BindingRole, nodeId);
        var access = RuntimeAccessPolicy.ResolveWriteAccess(bindings, target, actor.Role);
        if (access is null)
        {
            await auditService.RecordAsync(
                actor,
                "CommandWrite",
                $"{target.ScreenSlug}/{target.WidgetKey}/{target.BindingRole}/{target.NodeId}",
                "Denied",
                null,
                "The authenticated role is not authorized for this published writable binding.",
                cancellationToken);
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var result = await commandService.ExecuteAsync(
            actor,
            target,
            request.Value,
            access.WriteRequiresConfirm,
            request.IdempotencyKey.Trim(),
            request.Confirmed,
            cancellationToken);

        await auditService.RecordAsync(
            actor,
            "CommandWrite",
            $"{target.ScreenSlug}/{target.WidgetKey}/{target.BindingRole}/{target.NodeId}",
            result.Status,
            result.CommandId,
            result.Message,
            cancellationToken);

        return result.Status switch
        {
            "ConfirmationRequired" => Results.Accepted($"/api/hmi/runtime/commands/{result.CommandId}", result),
            "Failed" => Results.BadRequest(result),
            _ => Results.Ok(result)
        };
    }

    private static async Task<IResult> GetPublishedScreensAsync(IDbContextFactory<HmiDbContext> dbFactory, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var screens = await db.HmiScreens
            .AsNoTracking()
            .Where(x => x.IsPublished)
            .OrderBy(x => x.Name)
            .Select(x => new RuntimeScreenSummaryDto(x.Slug))
            .ToListAsync(cancellationToken);

        return Results.Ok(screens);
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

    private sealed record RuntimeScreenSummaryDto(string Slug);

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

    private sealed record WriteTagRequest(
        string Value,
        string ScreenSlug,
        string WidgetKey,
        string BindingRole,
        string IdempotencyKey,
        bool Confirmed = false);

    private sealed record AcknowledgeAlarmRequest(string ScreenSlug, string WidgetKey, string BindingRole);

    private sealed record RuntimeTagSnapshotDto(
        string NodeId,
        string? ValueText,
        string? StatusCode,
        bool IsStale,
        bool HasAlarm,
        string AlarmText,
        DateTimeOffset UpdatedUtc);

    private sealed record RuntimeAlarmDto(
        string NodeId,
        string DisplayName,
        string Area,
        string StatusCode,
        string AlarmText,
        int Severity,
        bool IsActive,
        bool IsAcknowledged,
        DateTimeOffset LastRaisedUtc,
        DateTimeOffset? AcknowledgedUtc,
        string? AcknowledgedBy);

    private sealed record RuntimeTrendSampleDto(string? ValueText, string? StatusCode, DateTimeOffset SampledUtc);
}
