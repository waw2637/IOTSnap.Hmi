using IOTSnap.Hmi.Data;
using IOTSnap.Hmi.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

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
        var screen = await db.HmiScreens.FirstOrDefaultAsync(x => x.Id == screenId, cancellationToken);
        if (screen is null)
        {
            return Results.NotFound();
        }

        db.HmiScreens.Remove(screen);
        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> PublishScreenAsync(int screenId, IDbContextFactory<HmiDbContext> dbFactory, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var screen = await db.HmiScreens.FirstOrDefaultAsync(x => x.Id == screenId, cancellationToken);
        if (screen is null)
        {
            return Results.NotFound();
        }

        screen.IsPublished = true;
        screen.PublishedUtc = DateTimeOffset.UtcNow;
        screen.UpdatedUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

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
