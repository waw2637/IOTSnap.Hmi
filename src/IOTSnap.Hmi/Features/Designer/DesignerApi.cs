using IOTSnap.Hmi.Data;
using IOTSnap.Hmi.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace IOTSnap.Hmi.Features.Designer;

public static class DesignerApi
{
    public static IEndpointRouteBuilder MapDesignerApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/hmi/designer")
            .RequireAuthorization(new AuthorizeAttribute { Roles = HmiRoles.Admin });

        group.MapGet("/screens", GetScreensAsync);
        group.MapGet("/screens/{slug}", GetScreenBySlugAsync);
        group.MapPost("/screens", CreateScreenAsync);
        group.MapPut("/screens/{screenId:int}", UpdateScreenAsync);
        group.MapDelete("/screens/{screenId:int}", DeleteScreenAsync);
        group.MapPost("/screens/{screenId:int}/publish", PublishScreenAsync);
        group.MapPost("/screens/{screenId:int}/rollback/{publicationId:long}", RollbackScreenAsync);
        group.MapGet("/audit", GetAuditAsync);

        return app;
    }

    private static async Task<IResult> GetScreensAsync(IDbContextFactory<HmiDbContext> dbFactory, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var screens = await db.HmiScreens
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new ScreenSummaryDto(
                x.Id,
                x.Name,
                x.Slug,
                x.Width,
                x.Height,
                x.IsPublished,
                x.PublishedUtc,
                x.UpdatedUtc,
                x.Widgets.Count))
            .ToListAsync(cancellationToken);

        return Results.Ok(screens);
    }

    private static async Task<IResult> GetScreenBySlugAsync(string slug, IDbContextFactory<HmiDbContext> dbFactory, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var screen = await db.HmiScreens
            .AsNoTracking()
            .Include(x => x.Widgets)
                .ThenInclude(x => x.Bindings)
            .FirstOrDefaultAsync(x => x.Slug == slug, cancellationToken);

        if (screen is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(ToScreenDetail(screen));
    }

    private static async Task<IResult> CreateScreenAsync(CreateScreenRequest request, IDbContextFactory<HmiDbContext> dbFactory, CancellationToken cancellationToken)
    {
        var validationError = ValidateScreenRequest(request);
        if (validationError is not null)
        {
            return Results.BadRequest(new { error = validationError });
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var slug = request.Slug.Trim().ToLowerInvariant();

        if (await db.HmiScreens.AnyAsync(x => x.Slug == slug, cancellationToken))
        {
            return Results.BadRequest(new { error = "A screen with this slug already exists." });
        }

        var now = DateTimeOffset.UtcNow;
        var screen = new HmiScreen
        {
            Name = request.Name.Trim(),
            Slug = slug,
            Width = Math.Max(320, request.Width),
            Height = Math.Max(240, request.Height),
            UpdatedUtc = now
        };

        foreach (var widgetDto in request.Widgets)
        {
            var widgetValidation = ValidateWidgetRequest(widgetDto);
            if (widgetValidation is not null)
            {
                return Results.BadRequest(new { error = widgetValidation });
            }

            var widget = new HmiWidget
            {
                Key = widgetDto.Key.Trim(),
                WidgetType = widgetDto.WidgetType.Trim(),
                Title = widgetDto.Title.Trim(),
                X = Math.Max(0, widgetDto.X),
                Y = Math.Max(0, widgetDto.Y),
                Width = Math.Max(40, widgetDto.Width),
                Height = Math.Max(40, widgetDto.Height),
                ZIndex = widgetDto.ZIndex,
                PropertiesJson = string.IsNullOrWhiteSpace(widgetDto.PropertiesJson) ? "{}" : widgetDto.PropertiesJson.Trim(),
                UpdatedUtc = now
            };

            foreach (var bindingDto in widgetDto.Bindings)
            {
                var bindingValidation = ValidateBindingRequest(bindingDto);
                if (bindingValidation is not null)
                {
                    return Results.BadRequest(new { error = bindingValidation });
                }

                widget.Bindings.Add(new HmiWidgetBinding
                {
                    BindingRole = bindingDto.BindingRole.Trim(),
                    SourceType = bindingDto.SourceType.Trim(),
                    SourceKey = bindingDto.SourceKey.Trim(),
                    WriteRequiresConfirm = bindingDto.WriteRequiresConfirm,
                    MinRole = NormalizeRole(bindingDto.MinRole),
                    UpdatedUtc = now
                });
            }

            screen.Widgets.Add(widget);
        }

        db.HmiScreens.Add(screen);
        await db.SaveChangesAsync(cancellationToken);

        return Results.Created($"/api/hmi/designer/screens/{screen.Slug}", new { screen.Id, screen.Slug });
    }

    private static async Task<IResult> UpdateScreenAsync(int screenId, UpdateScreenRequest request, IDbContextFactory<HmiDbContext> dbFactory, CancellationToken cancellationToken)
    {
        var validationError = ValidateScreenRequest(request);
        if (validationError is not null)
        {
            return Results.BadRequest(new { error = validationError });
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var screen = await db.HmiScreens
            .Include(x => x.Widgets)
                .ThenInclude(x => x.Bindings)
            .FirstOrDefaultAsync(x => x.Id == screenId, cancellationToken);

        if (screen is null)
        {
            return Results.NotFound();
        }

        var slug = request.Slug.Trim().ToLowerInvariant();
        var slugExists = await db.HmiScreens.AnyAsync(x => x.Slug == slug && x.Id != screenId, cancellationToken);
        if (slugExists)
        {
            return Results.BadRequest(new { error = "A screen with this slug already exists." });
        }

        var now = DateTimeOffset.UtcNow;
        screen.Name = request.Name.Trim();
        screen.Slug = slug;
        screen.Width = Math.Max(320, request.Width);
        screen.Height = Math.Max(240, request.Height);
        screen.UpdatedUtc = now;
        screen.IsPublished = false;

        db.HmiWidgetBindings.RemoveRange(screen.Widgets.SelectMany(x => x.Bindings));
        db.HmiWidgets.RemoveRange(screen.Widgets);
        screen.Widgets.Clear();

        foreach (var widgetDto in request.Widgets)
        {
            var widgetValidation = ValidateWidgetRequest(widgetDto);
            if (widgetValidation is not null)
            {
                return Results.BadRequest(new { error = widgetValidation });
            }

            var widget = new HmiWidget
            {
                Key = widgetDto.Key.Trim(),
                WidgetType = widgetDto.WidgetType.Trim(),
                Title = widgetDto.Title.Trim(),
                X = Math.Max(0, widgetDto.X),
                Y = Math.Max(0, widgetDto.Y),
                Width = Math.Max(40, widgetDto.Width),
                Height = Math.Max(40, widgetDto.Height),
                ZIndex = widgetDto.ZIndex,
                PropertiesJson = string.IsNullOrWhiteSpace(widgetDto.PropertiesJson) ? "{}" : widgetDto.PropertiesJson.Trim(),
                UpdatedUtc = now
            };

            foreach (var bindingDto in widgetDto.Bindings)
            {
                var bindingValidation = ValidateBindingRequest(bindingDto);
                if (bindingValidation is not null)
                {
                    return Results.BadRequest(new { error = bindingValidation });
                }

                widget.Bindings.Add(new HmiWidgetBinding
                {
                    BindingRole = bindingDto.BindingRole.Trim(),
                    SourceType = bindingDto.SourceType.Trim(),
                    SourceKey = bindingDto.SourceKey.Trim(),
                    WriteRequiresConfirm = bindingDto.WriteRequiresConfirm,
                    MinRole = NormalizeRole(bindingDto.MinRole),
                    UpdatedUtc = now
                });
            }

            screen.Widgets.Add(widget);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(new { screen.Id, screen.Slug, screen.UpdatedUtc });
    }

    private static async Task<IResult> DeleteScreenAsync(int screenId, IDbContextFactory<HmiDbContext> dbFactory, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var screen = await db.HmiScreens.Include(x => x.Widgets).ThenInclude(x => x.Bindings).FirstOrDefaultAsync(x => x.Id == screenId, cancellationToken);
        if (screen is null)
        {
            return Results.NotFound();
        }

        var publishError = await ValidatePublishAsync(screen, db, cancellationToken);
        if (publishError is not null)
        {
            return Results.BadRequest(new { error = publishError });
        }

        db.HmiScreens.Remove(screen);
        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> PublishScreenAsync(int screenId, HttpContext httpContext, IDbContextFactory<HmiDbContext> dbFactory, RuntimeAuditService auditService, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var screen = await db.HmiScreens
            .Include(x => x.Widgets)
                .ThenInclude(x => x.Bindings)
            .FirstOrDefaultAsync(x => x.Id == screenId, cancellationToken);
        if (screen is null)
        {
            return Results.NotFound();
        }

        var publishError = await ValidatePublishAsync(screen, db, cancellationToken);
        if (publishError is not null)
        {
            return Results.BadRequest(new { error = publishError });
        }

        screen.IsPublished = true;
        screen.PublishedUtc = DateTimeOffset.UtcNow;
        screen.UpdatedUtc = DateTimeOffset.UtcNow;

        var actor = RuntimeAccessPolicy.ResolveActor(httpContext.User);
        db.HmiScreenPublications.Add(new HmiScreenPublication
        {
            HmiScreenId = screen.Id,
            Slug = screen.Slug,
            SnapshotJson = JsonSerializer.Serialize(ToScreenDetail(screen)),
            PublishedBy = actor?.Username,
            PublishedUtc = screen.PublishedUtc.Value
        });
        await db.SaveChangesAsync(cancellationToken);

        if (actor is not null)
        {
            await auditService.RecordAsync(actor, "ScreenPublished", screen.Slug, "Succeeded", null, null, cancellationToken);
        }

        return Results.Ok(new { screen.Id, screen.Slug, screen.PublishedUtc });
    }

    private static async Task<IResult> GetAuditAsync(string? actionType, string? actor, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, int? page, int? pageSize, IDbContextFactory<HmiDbContext> dbFactory, CancellationToken cancellationToken)
    {
        var start = fromUtc ?? DateTimeOffset.UtcNow.AddDays(-7);
        var end = toUtc ?? DateTimeOffset.UtcNow;
        if (start > end || end - start > TimeSpan.FromDays(31)) return Results.BadRequest(new { error = "Use a range no greater than 31 days." });
        var currentPage = Math.Max(1, page ?? 1);
        var size = Math.Clamp(pageSize ?? 100, 1, 500);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.OperatorAuditEntries.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(actionType)) query = query.Where(x => x.ActionType == actionType);
        if (!string.IsNullOrWhiteSpace(actor)) query = query.Where(x => x.ActorUsername == actor);
        var filtered = (await query.ToListAsync(cancellationToken))
            .Where(x => x.OccurredUtc >= start && x.OccurredUtc <= end)
            .OrderByDescending(x => x.OccurredUtc)
            .ToList();
        var total = filtered.Count;
        var items = filtered.Skip((currentPage - 1) * size).Take(size).ToList();
        return Results.Ok(new { total, page = currentPage, pageSize = size, items });
    }

    private static async Task<IResult> RollbackScreenAsync(int screenId, long publicationId, HttpContext httpContext, IDbContextFactory<HmiDbContext> dbFactory, RuntimeAuditService auditService, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var screen = await db.HmiScreens.Include(x => x.Widgets).ThenInclude(x => x.Bindings).FirstOrDefaultAsync(x => x.Id == screenId, cancellationToken);
        var publication = await db.HmiScreenPublications.FirstOrDefaultAsync(x => x.Id == publicationId && x.HmiScreenId == screenId, cancellationToken);
        if (screen is null || publication is null) return Results.NotFound();

        ScreenDetailDto? snapshot;
        try { snapshot = JsonSerializer.Deserialize<ScreenDetailDto>(publication.SnapshotJson); }
        catch (JsonException) { return Results.BadRequest(new { error = "The selected publication snapshot is invalid." }); }
        if (snapshot is null) return Results.BadRequest(new { error = "The selected publication snapshot is empty." });

        db.HmiWidgetBindings.RemoveRange(screen.Widgets.SelectMany(x => x.Bindings));
        db.HmiWidgets.RemoveRange(screen.Widgets);
        screen.Widgets.Clear();
        screen.Name = snapshot.Name;
        screen.Slug = snapshot.Slug;
        screen.Width = snapshot.Width;
        screen.Height = snapshot.Height;
        screen.IsPublished = true;
        screen.PublishedUtc = DateTimeOffset.UtcNow;
        screen.UpdatedUtc = screen.PublishedUtc.Value;

        foreach (var widgetSnapshot in snapshot.Widgets)
        {
            var widget = new HmiWidget
            {
                Key = widgetSnapshot.Key,
                WidgetType = widgetSnapshot.WidgetType,
                Title = widgetSnapshot.Title,
                X = widgetSnapshot.X,
                Y = widgetSnapshot.Y,
                Width = widgetSnapshot.Width,
                Height = widgetSnapshot.Height,
                ZIndex = widgetSnapshot.ZIndex,
                PropertiesJson = widgetSnapshot.PropertiesJson,
                UpdatedUtc = screen.UpdatedUtc
            };
            foreach (var bindingSnapshot in widgetSnapshot.Bindings)
            {
                widget.Bindings.Add(new HmiWidgetBinding
                {
                    BindingRole = bindingSnapshot.BindingRole,
                    SourceType = bindingSnapshot.SourceType,
                    SourceKey = bindingSnapshot.SourceKey,
                    WriteRequiresConfirm = bindingSnapshot.WriteRequiresConfirm,
                    MinRole = bindingSnapshot.MinRole,
                    UpdatedUtc = screen.UpdatedUtc
                });
            }
            screen.Widgets.Add(widget);
        }

        var validationError = await ValidatePublishAsync(screen, db, cancellationToken);
        if (validationError is not null) return Results.BadRequest(new { error = validationError });
        var actor = RuntimeAccessPolicy.ResolveActor(httpContext.User);
        db.HmiScreenPublications.Add(new HmiScreenPublication { HmiScreenId = screen.Id, Slug = screen.Slug, SnapshotJson = JsonSerializer.Serialize(snapshot), PublishedBy = actor?.Username, PublishedUtc = screen.PublishedUtc.Value });
        await db.SaveChangesAsync(cancellationToken);
        if (actor is not null) await auditService.RecordAsync(actor, "ScreenRolledBack", screen.Slug, "Succeeded", publication.Id.ToString(), null, cancellationToken);
        return Results.Ok(new { screen.Id, screen.Slug, screen.PublishedUtc });
    }

    private static ScreenDetailDto ToScreenDetail(HmiScreen screen)
    {
        return new ScreenDetailDto(
            screen.Id,
            screen.Name,
            screen.Slug,
            screen.Width,
            screen.Height,
            screen.IsPublished,
            screen.PublishedUtc,
            screen.UpdatedUtc,
            screen.Widgets
                .OrderBy(x => x.ZIndex)
                .Select(x => new WidgetDetailDto(
                    x.Id,
                    x.Key,
                    x.WidgetType,
                    x.Title,
                    x.X,
                    x.Y,
                    x.Width,
                    x.Height,
                    x.ZIndex,
                    x.PropertiesJson,
                    x.Bindings
                        .OrderBy(b => b.BindingRole)
                        .Select(b => new WidgetBindingDto(
                            b.BindingRole,
                            b.SourceType,
                            b.SourceKey,
                            b.WriteRequiresConfirm,
                            b.MinRole))
                        .ToList()))
                .ToList());
    }

    private static string? ValidateScreenRequest(BaseScreenRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return "Screen name is required.";
        }

        if (string.IsNullOrWhiteSpace(request.Slug))
        {
            return "Screen slug is required.";
        }

        if (!request.Widgets.Any())
        {
            return "At least one widget is required.";
        }

        if (request.Width < 320 || request.Height < 240)
        {
            return "Screen dimensions must be at least 320 by 240.";
        }

        if (request.Widgets.GroupBy(x => x.Key.Trim(), StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1))
        {
            return "Widget keys must be unique within a screen.";
        }

        return null;
    }

    private static string? ValidateWidgetRequest(WidgetUpsertDto widget)
    {
        if (string.IsNullOrWhiteSpace(widget.Key))
        {
            return "Widget key is required.";
        }

        var supportedTypes = new[] { "Numeric", "CommandButton", "TrendChart" };
        if (!supportedTypes.Contains(widget.WidgetType, StringComparer.OrdinalIgnoreCase))
        {
            return $"Widget type '{widget.WidgetType}' is not supported in v1.";
        }

        if (widget.Width <= 0 || widget.Height <= 0)
        {
            return "Widget width and height must be greater than zero.";
        }

        try
        {
            using var _ = JsonDocument.Parse(string.IsNullOrWhiteSpace(widget.PropertiesJson) ? "{}" : widget.PropertiesJson);
        }
        catch (JsonException)
        {
            return $"Widget '{widget.Key}' has invalid property JSON.";
        }

        var properties = HmiWidgetProperties.Parse(widget.PropertiesJson);
        if (properties.ValueMin is not null && properties.ValueMax is not null && properties.ValueMin > properties.ValueMax)
        {
            return $"Widget '{widget.Key}' has an invalid value range.";
        }

        if (properties.InputMin is not null && properties.InputMax is not null && properties.InputMin > properties.InputMax)
        {
            return $"Widget '{widget.Key}' has an invalid input range.";
        }

        return null;
    }

    private static string? ValidateBindingRequest(WidgetBindingDto binding)
    {
        if (string.IsNullOrWhiteSpace(binding.BindingRole))
        {
            return "Binding role is required.";
        }

        if (string.IsNullOrWhiteSpace(binding.SourceType))
        {
            return "Binding source type is required.";
        }

        if (string.IsNullOrWhiteSpace(binding.SourceKey))
        {
            return "Binding source key is required.";
        }

        if (!string.Equals(binding.SourceType, "OpcTag", StringComparison.OrdinalIgnoreCase))
        {
            return "Only 'OpcTag' source type is supported in v1.";
        }

        return null;
    }

    private static async Task<string?> ValidatePublishAsync(HmiScreen screen, HmiDbContext db, CancellationToken cancellationToken)
    {
        if (screen.Widgets.Count == 0) return "A published screen requires at least one widget.";
        if (screen.Widgets.GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1)) return "Widget keys must be unique within a screen.";

        foreach (var widget in screen.Widgets)
        {
            if (widget.X < 0 || widget.Y < 0 || widget.X + widget.Width > screen.Width || widget.Y + widget.Height > screen.Height)
                return $"Widget '{widget.Key}' is outside the screen bounds.";

            if (widget.Bindings.GroupBy(x => x.BindingRole, StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1))
                return $"Widget '{widget.Key}' has duplicate binding roles.";

            try { using var _ = JsonDocument.Parse(widget.PropertiesJson); }
            catch (JsonException) { return $"Widget '{widget.Key}' has invalid property JSON."; }

            foreach (var binding in widget.Bindings)
            {
                var mapping = await db.OpcUaNodeMappings.AsNoTracking().FirstOrDefaultAsync(x => x.NodeId == binding.SourceKey, cancellationToken);
                if (mapping is null) return $"Binding '{binding.SourceKey}' is not a configured OPC UA mapping.";
                if (widget.WidgetType.Equals("CommandButton", StringComparison.OrdinalIgnoreCase) && !mapping.IsWritable)
                    return $"Command widget '{widget.Key}' requires a writable mapping.";
            }
        }
        return null;
    }

    private static string NormalizeRole(string role)
    {
        if (string.Equals(role, HmiRoles.Admin, StringComparison.OrdinalIgnoreCase))
        {
            return HmiRoles.Admin;
        }

        if (string.Equals(role, HmiRoles.Operator, StringComparison.OrdinalIgnoreCase))
        {
            return HmiRoles.Operator;
        }

        return HmiRoles.Viewer;
    }

    private abstract record BaseScreenRequest(string Name, string Slug, int Width, int Height, IReadOnlyList<WidgetUpsertDto> Widgets);

    private sealed record CreateScreenRequest(
        string Name,
        string Slug,
        int Width,
        int Height,
        IReadOnlyList<WidgetUpsertDto> Widgets) : BaseScreenRequest(Name, Slug, Width, Height, Widgets);

    private sealed record UpdateScreenRequest(
        string Name,
        string Slug,
        int Width,
        int Height,
        IReadOnlyList<WidgetUpsertDto> Widgets) : BaseScreenRequest(Name, Slug, Width, Height, Widgets);

    private sealed record WidgetUpsertDto(
        string Key,
        string WidgetType,
        string Title,
        int X,
        int Y,
        int Width,
        int Height,
        int ZIndex,
        string PropertiesJson,
        IReadOnlyList<WidgetBindingDto> Bindings);

    private sealed record WidgetBindingDto(
        string BindingRole,
        string SourceType,
        string SourceKey,
        bool WriteRequiresConfirm,
        string MinRole);

    private sealed record ScreenSummaryDto(
        int Id,
        string Name,
        string Slug,
        int Width,
        int Height,
        bool IsPublished,
        DateTimeOffset? PublishedUtc,
        DateTimeOffset UpdatedUtc,
        int WidgetCount);

    private sealed record ScreenDetailDto(
        int Id,
        string Name,
        string Slug,
        int Width,
        int Height,
        bool IsPublished,
        DateTimeOffset? PublishedUtc,
        DateTimeOffset UpdatedUtc,
        IReadOnlyList<WidgetDetailDto> Widgets);

    private sealed record WidgetDetailDto(
        int Id,
        string Key,
        string WidgetType,
        string Title,
        int X,
        int Y,
        int Width,
        int Height,
        int ZIndex,
        string PropertiesJson,
        IReadOnlyList<WidgetBindingDto> Bindings);
}
