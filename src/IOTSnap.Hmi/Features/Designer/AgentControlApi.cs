using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IOTSnap.Hmi.Data;
using IOTSnap.Hmi.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace IOTSnap.Hmi.Features.Designer;

public static class AgentControlApi
{
    private static readonly string[] SupportedWidgetTypes = ["Numeric", "CommandButton", "TrendChart"];
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapAgentControlApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/hmi/agent")
            .RequireAuthorization(new AuthorizeAttribute { Roles = HmiRoles.Admin });

        group.MapGet("/capabilities", GetCapabilities);
        group.MapGet("/tag-catalog", GetTagCatalogAsync);
        group.MapPost("/import-tags-csv", ImportTagsFromCsvAsync);
        group.MapPost("/sketch-intake", SketchIntakeAsync);
        group.MapPost("/draft-screen", DraftScreenAsync);
        group.MapPost("/draft-preview", DraftPreviewAsync);
        group.MapPost("/apply-draft", ApplyDraftAsync);
        group.MapPost("/apply-draft-safe", ApplyDraftSafeAsync);

        return app;
    }

    private static IResult GetCapabilities()
    {
        var payload = new
        {
            version = "v1",
            routes = new[]
            {
                "GET /api/hmi/agent/capabilities",
                "GET /api/hmi/agent/tag-catalog",
                "POST /api/hmi/agent/import-tags-csv",
                "POST /api/hmi/agent/sketch-intake",
                "POST /api/hmi/agent/draft-screen",
                "POST /api/hmi/agent/draft-preview",
                "POST /api/hmi/agent/apply-draft",
                "POST /api/hmi/agent/apply-draft-safe",
                "GET /api/hmi/designer/screens",
                "GET /api/hmi/designer/screens/{slug}",
                "POST /api/hmi/designer/screens",
                "PUT /api/hmi/designer/screens/{screenId}",
                "POST /api/hmi/designer/screens/{screenId}/publish"
            },
            supportedWidgetTypes = SupportedWidgetTypes,
            supportedBindingSourceTypes = new[] { "OpcTag" },
            notes = "Designed for MCP/agent orchestration. Includes CSV diagnostics, deterministic sketch synthesis, and preview/safe-apply workflows."
        };

        return Results.Ok(payload);
    }

    private static async Task<IResult> GetTagCatalogAsync(IDbContextFactory<HmiDbContext> dbFactory, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var tags = await db.OpcUaNodeMappings
            .AsNoTracking()
            .OrderBy(x => x.Area)
            .ThenBy(x => x.DisplayName)
            .Select(x => new TagCatalogItemDto(x.Id, x.DisplayName, x.NodeId, x.Area, x.DataType, x.IsWritable, x.SamplingIntervalMs))
            .ToListAsync(cancellationToken);

        return Results.Ok(tags);
    }

    private static async Task<IResult> ImportTagsFromCsvAsync(
        CsvImportRequest request,
        IDbContextFactory<HmiDbContext> dbFactory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CsvContent))
        {
            return Results.BadRequest(new { error = "CsvContent is required." });
        }

        var parseResult = ParseCsv(request.CsvContent);
        var diagnostics = parseResult.Diagnostics.ToList();
        var parsed = new List<ParsedCsvTagRow>();

        foreach (var row in parseResult.Rows)
        {
            var nodeId = ReadCsvCell(parseResult.HeaderIndex, row, "nodeid").Trim();
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                diagnostics.Add(new ApiDiagnosticDto("error", "CSV_NODEID_REQUIRED", "NodeId is required.", row.LineNumber, null));
                continue;
            }

            var displayName = ReadCsvCell(parseResult.HeaderIndex, row, "displayname").Trim();
            var area = ReadCsvCell(parseResult.HeaderIndex, row, "area").Trim();
            var dataType = ReadCsvCell(parseResult.HeaderIndex, row, "datatype").Trim();

            var intervalText = ReadCsvCell(parseResult.HeaderIndex, row, "samplingintervalms").Trim();
            var samplingIntervalMs = 1000;
            if (!string.IsNullOrWhiteSpace(intervalText)
                && !int.TryParse(intervalText, NumberStyles.Integer, CultureInfo.InvariantCulture, out samplingIntervalMs))
            {
                diagnostics.Add(new ApiDiagnosticDto(
                    "warning",
                    "CSV_INVALID_SAMPLING_INTERVAL",
                    $"Invalid SamplingIntervalMs '{intervalText}'. Defaulting to 1000.",
                    row.LineNumber,
                    null));
                samplingIntervalMs = 1000;
            }

            var writableText = ReadCsvCell(parseResult.HeaderIndex, row, "iswritable").Trim();
            var (isWritable, writableRecognized) = ParseBooleanLike(writableText);
            if (!writableRecognized && !string.IsNullOrWhiteSpace(writableText))
            {
                diagnostics.Add(new ApiDiagnosticDto(
                    "warning",
                    "CSV_INVALID_ISWRITABLE",
                    $"Invalid IsWritable value '{writableText}'. Defaulting to false.",
                    row.LineNumber,
                    null));
            }

            parsed.Add(new ParsedCsvTagRow(
                string.IsNullOrWhiteSpace(displayName) ? nodeId : displayName,
                nodeId,
                string.IsNullOrWhiteSpace(area) ? "Imported" : area,
                string.IsNullOrWhiteSpace(dataType) ? "Auto" : dataType,
                samplingIntervalMs <= 0 ? 1000 : samplingIntervalMs,
                isWritable,
                row.LineNumber));
        }

        var rejectedCount = diagnostics.Count(x => string.Equals(x.Severity, "error", StringComparison.OrdinalIgnoreCase));

        if (!request.Persist)
        {
            return Results.Ok(new
            {
                imported = 0,
                updated = 0,
                previewCount = parsed.Count,
                rejectedCount,
                diagnostics,
                preview = parsed.Take(200).ToList()
            });
        }

        if (parsed.Count == 0)
        {
            return Results.BadRequest(new
            {
                error = "No valid rows were found in CSV content.",
                diagnostics
            });
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var connectionProfileId = request.ConnectionProfileId
            ?? await db.OpcUaConnectionProfiles
                .AsNoTracking()
                .Where(x => x.Enabled)
                .OrderByDescending(x => x.Id)
                .Select(x => (int?)x.Id)
                .FirstOrDefaultAsync(cancellationToken);

        if (!connectionProfileId.HasValue)
        {
            return Results.BadRequest(new { error = "No enabled connection profile found. Create one first or pass ConnectionProfileId." });
        }

        var existing = await db.OpcUaNodeMappings
            .Where(x => x.OpcUaConnectionProfileId == connectionProfileId.Value)
            .ToDictionaryAsync(x => x.NodeId, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var added = 0;
        var updated = 0;

        foreach (var row in parsed)
        {
            if (existing.TryGetValue(row.NodeId, out var mapping))
            {
                mapping.DisplayName = row.DisplayName;
                mapping.Area = row.Area;
                mapping.DataType = row.DataType;
                mapping.IsWritable = row.IsWritable;
                mapping.SamplingIntervalMs = row.SamplingIntervalMs;
                updated++;
                continue;
            }

            db.OpcUaNodeMappings.Add(new OpcUaNodeMapping
            {
                OpcUaConnectionProfileId = connectionProfileId.Value,
                DisplayName = row.DisplayName,
                NodeId = row.NodeId,
                Area = row.Area,
                DataType = row.DataType,
                IsWritable = row.IsWritable,
                SamplingIntervalMs = row.SamplingIntervalMs
            });
            added++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(new
        {
            imported = added,
            updated,
            profileId = connectionProfileId.Value,
            acceptedCount = parsed.Count,
            rejectedCount,
            diagnostics
        });
    }

    private static async Task<IResult> SketchIntakeAsync(
        SketchIntakeRequest request,
        IDbContextFactory<HmiDbContext> dbFactory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Slug))
        {
            return Results.BadRequest(new { error = "Name and Slug are required." });
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var maxTags = request.MaxTags is > 0 and <= 200 ? request.MaxTags.Value : 24;
        var candidateTags = await db.OpcUaNodeMappings
            .AsNoTracking()
            .OrderBy(x => x.Area)
            .ThenBy(x => x.DisplayName)
            .Take(maxTags)
            .ToListAsync(cancellationToken);

        var diagnostics = new List<ApiDiagnosticDto>();
        var widgets = BuildWidgetsFromSketch(
            request.Width,
            request.Height,
            request.Sketch,
            candidateTags,
            diagnostics);

        var draft = new DraftScreenDto(
            request.Name.Trim(),
            request.Slug.Trim().ToLowerInvariant(),
            Math.Max(320, request.Width),
            Math.Max(240, request.Height),
            request.Prompt?.Trim() ?? string.Empty,
            request.Sketch?.Notes?.Trim() ?? string.Empty,
            widgets);

        var metadata = CreateDraftMetadata(draft);
        return Results.Ok(new { draft, metadata, diagnostics });
    }

    private static async Task<IResult> DraftScreenAsync(
        DraftScreenRequest request,
        IDbContextFactory<HmiDbContext> dbFactory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Slug))
        {
            return Results.BadRequest(new { error = "Name and Slug are required." });
        }

        var widgets = request.Widgets?.ToList() ?? new List<DraftWidgetDto>();
        if (widgets.Count == 0)
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var candidateTags = await db.OpcUaNodeMappings
                .AsNoTracking()
                .OrderBy(x => x.Id)
                .Take(24)
                .ToListAsync(cancellationToken);
            var synthesisDiagnostics = new List<ApiDiagnosticDto>();
            widgets = BuildWidgetsFromSketch(
                request.Width,
                request.Height,
                request.Sketch,
                candidateTags,
                synthesisDiagnostics).ToList();

            if (widgets.Count == 0)
            {
                widgets = BuildAutoGridWidgets(request.Width, request.Height, candidateTags).ToList();
            }
        }

        var draft = new DraftScreenDto(
            request.Name.Trim(),
            request.Slug.Trim().ToLowerInvariant(),
            Math.Max(320, request.Width),
            Math.Max(240, request.Height),
            request.Prompt ?? string.Empty,
            request.SketchNotes ?? string.Empty,
            widgets);

        return Results.Ok(draft);
    }

    private static async Task<IResult> DraftPreviewAsync(
        DraftPreviewRequest request,
        IDbContextFactory<HmiDbContext> dbFactory,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<ApiDiagnosticDto>();
        var normalizedDraft = NormalizeDraft(request.Draft, diagnostics);
        var hasErrors = diagnostics.Any(x => string.Equals(x.Severity, "error", StringComparison.OrdinalIgnoreCase));

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var slugExists = await db.HmiScreens
            .AsNoTracking()
            .AnyAsync(x => x.Slug == normalizedDraft.Slug, cancellationToken);

        if (slugExists && !request.AllowOverwrite)
        {
            diagnostics.Add(new ApiDiagnosticDto(
                "error",
                "DRAFT_SLUG_EXISTS",
                "A screen with this slug already exists. Set AllowOverwrite to true for replacement.",
                null,
                null));
            hasErrors = true;
        }

        var metadata = CreateDraftMetadata(normalizedDraft);
        return Results.Ok(new
        {
            draft = normalizedDraft,
            metadata,
            canApply = !hasErrors,
            diagnostics
        });
    }

    private static async Task<IResult> ApplyDraftAsync(
        DraftScreenDto draft,
        IDbContextFactory<HmiDbContext> dbFactory,
        CancellationToken cancellationToken)
    {
        return await ApplyDraftInternalAsync(
            draft,
            expectedDraftHash: null,
            allowOverwrite: false,
            publish: false,
            dbFactory,
            cancellationToken);
    }

    private static async Task<IResult> ApplyDraftSafeAsync(
        ApplyDraftRequest request,
        IDbContextFactory<HmiDbContext> dbFactory,
        CancellationToken cancellationToken)
    {
        return await ApplyDraftInternalAsync(
            request.Draft,
            request.ExpectedDraftHash,
            request.AllowOverwrite,
            request.Publish,
            dbFactory,
            cancellationToken);
    }

    private static async Task<IResult> ApplyDraftInternalAsync(
        DraftScreenDto draft,
        string? expectedDraftHash,
        bool allowOverwrite,
        bool publish,
        IDbContextFactory<HmiDbContext> dbFactory,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<ApiDiagnosticDto>();
        var normalizedDraft = NormalizeDraft(draft, diagnostics);
        if (diagnostics.Any(x => string.Equals(x.Severity, "error", StringComparison.OrdinalIgnoreCase)))
        {
            return Results.BadRequest(new { error = "Draft validation failed.", diagnostics });
        }

        var metadata = CreateDraftMetadata(normalizedDraft);
        if (!string.IsNullOrWhiteSpace(expectedDraftHash)
            && !string.Equals(expectedDraftHash.Trim(), metadata.DraftHash, StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new
            {
                error = "ExpectedDraftHash does not match the submitted draft.",
                expected = expectedDraftHash,
                actual = metadata.DraftHash
            });
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var existing = await db.HmiScreens
            .Include(x => x.Widgets)
                .ThenInclude(x => x.Bindings)
            .FirstOrDefaultAsync(x => x.Slug == normalizedDraft.Slug, cancellationToken);

        if (existing is not null && !allowOverwrite)
        {
            return Results.BadRequest(new
            {
                error = "A screen with this slug already exists. Use AllowOverwrite for replacement.",
                slug = normalizedDraft.Slug
            });
        }

        RollbackMetadataDto? rollback = null;
        if (existing is not null)
        {
            rollback = new RollbackMetadataDto(
                existing.Id,
                existing.Slug,
                existing.IsPublished,
                existing.UpdatedUtc,
                ToDraft(existing));
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (existing is not null)
        {
            db.HmiScreens.Remove(existing);
            await db.SaveChangesAsync(cancellationToken);
        }

        var screen = ToEntity(normalizedDraft, publish);
        db.HmiScreens.Add(screen);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Results.Created($"/api/hmi/designer/screens/{screen.Slug}", new
        {
            screen.Id,
            screen.Slug,
            screen.IsPublished,
            metadata,
            rollback,
            diagnostics
        });
    }

    private static HmiScreen ToEntity(DraftScreenDto draft, bool publish)
    {
        var now = DateTimeOffset.UtcNow;
        var screen = new HmiScreen
        {
            Name = draft.Name,
            Slug = draft.Slug,
            Width = draft.Width,
            Height = draft.Height,
            IsPublished = publish,
            PublishedUtc = publish ? now : null,
            UpdatedUtc = now
        };

        var order = 0;
        foreach (var widget in draft.Widgets)
        {
            var w = new HmiWidget
            {
                Key = widget.Key,
                WidgetType = widget.WidgetType,
                Title = widget.Title,
                X = widget.X,
                Y = widget.Y,
                Width = widget.Width,
                Height = widget.Height,
                ZIndex = widget.ZIndex <= 0 ? ++order : widget.ZIndex,
                PropertiesJson = widget.PropertiesJson,
                UpdatedUtc = now
            };

            foreach (var binding in widget.Bindings)
            {
                w.Bindings.Add(new HmiWidgetBinding
                {
                    BindingRole = binding.BindingRole,
                    SourceType = "OpcTag",
                    SourceKey = binding.SourceKey,
                    WriteRequiresConfirm = binding.WriteRequiresConfirm,
                    MinRole = NormalizeRole(binding.MinRole),
                    UpdatedUtc = now
                });
            }

            screen.Widgets.Add(w);
        }

        return screen;
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

    private static DraftScreenDto NormalizeDraft(DraftScreenDto input, List<ApiDiagnosticDto> diagnostics)
    {
        var name = input.Name?.Trim() ?? string.Empty;
        var slug = input.Slug?.Trim().ToLowerInvariant() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            diagnostics.Add(new ApiDiagnosticDto("error", "DRAFT_NAME_REQUIRED", "Name is required.", null, null));
        }

        if (string.IsNullOrWhiteSpace(slug))
        {
            diagnostics.Add(new ApiDiagnosticDto("error", "DRAFT_SLUG_REQUIRED", "Slug is required.", null, null));
        }

        var widgets = input.Widgets?.ToList() ?? [];
        if (widgets.Count == 0)
        {
            diagnostics.Add(new ApiDiagnosticDto("error", "DRAFT_WIDGETS_REQUIRED", "At least one widget is required.", null, null));
        }

        var normalizedWidgets = new List<DraftWidgetDto>();
        var index = 0;
        foreach (var widget in widgets)
        {
            index++;
            var widgetType = widget.WidgetType?.Trim() ?? string.Empty;
            if (!SupportedWidgetTypes.Contains(widgetType, StringComparer.OrdinalIgnoreCase))
            {
                diagnostics.Add(new ApiDiagnosticDto("error", "DRAFT_UNSUPPORTED_WIDGET", $"Unsupported widget type '{widgetType}'.", null, null));
                continue;
            }

            var key = string.IsNullOrWhiteSpace(widget.Key) ? $"auto-{index}" : widget.Key.Trim();
            var title = string.IsNullOrWhiteSpace(widget.Title) ? key : widget.Title.Trim();
            var bindings = widget.Bindings?.ToList() ?? [];

            if (bindings.Count == 0)
            {
                diagnostics.Add(new ApiDiagnosticDto("error", "DRAFT_BINDINGS_REQUIRED", $"Widget '{key}' requires at least one binding.", null, null));
                continue;
            }

            var normalizedBindings = new List<DraftBindingDto>();
            foreach (var binding in bindings)
            {
                var sourceKey = binding.SourceKey?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(sourceKey))
                {
                    diagnostics.Add(new ApiDiagnosticDto("error", "DRAFT_BINDING_SOURCEKEY_REQUIRED", $"Widget '{key}' has binding with empty SourceKey.", null, null));
                    continue;
                }

                if (!string.Equals(binding.SourceType, "OpcTag", StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics.Add(new ApiDiagnosticDto("error", "DRAFT_BINDING_SOURCE_UNSUPPORTED", $"Widget '{key}' uses unsupported SourceType '{binding.SourceType}'.", null, null));
                    continue;
                }

                normalizedBindings.Add(new DraftBindingDto(
                    string.IsNullOrWhiteSpace(binding.BindingRole) ? "PrimaryValue" : binding.BindingRole.Trim(),
                    "OpcTag",
                    sourceKey,
                    binding.WriteRequiresConfirm,
                    NormalizeRole(binding.MinRole)));
            }

            if (normalizedBindings.Count == 0)
            {
                continue;
            }

            normalizedWidgets.Add(new DraftWidgetDto(
                key,
                widgetType,
                title,
                Math.Max(0, widget.X),
                Math.Max(0, widget.Y),
                Math.Max(40, widget.Width),
                Math.Max(40, widget.Height),
                widget.ZIndex,
                string.IsNullOrWhiteSpace(widget.PropertiesJson) ? "{}" : widget.PropertiesJson.Trim(),
                normalizedBindings));
        }

        return new DraftScreenDto(
            name,
            slug,
            Math.Max(320, input.Width),
            Math.Max(240, input.Height),
            input.Prompt?.Trim() ?? string.Empty,
            input.SketchNotes?.Trim() ?? string.Empty,
            normalizedWidgets);
    }

    private static DraftMetadataDto CreateDraftMetadata(DraftScreenDto draft)
    {
        var canonical = JsonSerializer.Serialize(draft, CanonicalJsonOptions);
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        var hash = Convert.ToHexString(hashBytes).ToLowerInvariant();
        return new DraftMetadataDto(hash, $"draft:{hash}", draft.Widgets.Count, DateTimeOffset.UtcNow);
    }

    private static DraftScreenDto ToDraft(HmiScreen screen)
    {
        return new DraftScreenDto(
            screen.Name,
            screen.Slug,
            screen.Width,
            screen.Height,
            string.Empty,
            string.Empty,
            screen.Widgets
                .OrderBy(x => x.ZIndex)
                .Select(x => new DraftWidgetDto(
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
                        .Select(b => new DraftBindingDto(
                            b.BindingRole,
                            b.SourceType,
                            b.SourceKey,
                            b.WriteRequiresConfirm,
                            b.MinRole))
                        .ToList()))
                .ToList());
    }

    private static IReadOnlyList<DraftWidgetDto> BuildWidgetsFromSketch(
        int targetWidth,
        int targetHeight,
        SketchLayoutInputDto? sketch,
        IReadOnlyList<OpcUaNodeMapping> candidateTags,
        List<ApiDiagnosticDto> diagnostics)
    {
        if (sketch is null || sketch.Elements.Count == 0)
        {
            return [];
        }

        var sourceWidth = Math.Max(1, sketch.SourceWidth);
        var sourceHeight = Math.Max(1, sketch.SourceHeight);
        var width = Math.Max(320, targetWidth);
        var height = Math.Max(240, targetHeight);

        var sortedElements = sketch.Elements
            .OrderBy(e => e.Y)
            .ThenBy(e => e.X)
            .ThenBy(e => e.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var widgets = new List<DraftWidgetDto>();
        for (var i = 0; i < sortedElements.Count; i++)
        {
            var element = sortedElements[i];
            var widgetType = MapSketchKindToWidgetType(element.Kind);
            var tag = candidateTags.Count == 0 ? null : candidateTags[i % candidateTags.Count];

            var x = Math.Max(0, (int)Math.Round(element.X * width / sourceWidth));
            var y = Math.Max(0, (int)Math.Round(element.Y * height / sourceHeight));
            var w = Math.Max(120, (int)Math.Round(element.Width * width / sourceWidth));
            var h = Math.Max(80, (int)Math.Round(element.Height * height / sourceHeight));

            var title = string.IsNullOrWhiteSpace(element.Label)
                ? (tag?.DisplayName ?? $"Widget {i + 1}")
                : element.Label.Trim();

            var bindings = new List<DraftBindingDto>();
            if (tag is not null)
            {
                bindings.Add(new DraftBindingDto(
                    "PrimaryValue",
                    "OpcTag",
                    tag.NodeId,
                    false,
                    HmiRoles.Viewer));
            }
            else
            {
                diagnostics.Add(new ApiDiagnosticDto(
                    "warning",
                    "SKETCH_NO_TAGS_AVAILABLE",
                    "No OPC tags are available; generated widgets have no bindings.",
                    null,
                    null));
            }

            widgets.Add(new DraftWidgetDto(
                string.IsNullOrWhiteSpace(element.Id) ? $"sketch-{i + 1}" : element.Id.Trim().ToLowerInvariant(),
                widgetType,
                title,
                x,
                y,
                w,
                h,
                i + 1,
                "{}",
                bindings));
        }

        return widgets;
    }

    private static IReadOnlyList<DraftWidgetDto> BuildAutoGridWidgets(
        int targetWidth,
        int targetHeight,
        IReadOnlyList<OpcUaNodeMapping> candidateTags)
    {
        var widgets = new List<DraftWidgetDto>();
        var width = Math.Max(320, targetWidth);
        var height = Math.Max(240, targetHeight);

        var cardWidth = 300;
        var cardHeight = 140;
        var hGap = 24;
        var vGap = 20;
        var startX = 40;
        var startY = 40;
        var maxX = Math.Max(startX, width - cardWidth - 20);

        var x = startX;
        var y = startY;
        var idx = 1;
        foreach (var tag in candidateTags.Take(24))
        {
            widgets.Add(new DraftWidgetDto(
                $"auto-{idx}",
                "Numeric",
                string.IsNullOrWhiteSpace(tag.DisplayName) ? tag.NodeId : tag.DisplayName,
                x,
                y,
                cardWidth,
                cardHeight,
                idx,
                "{}",
                [new DraftBindingDto("PrimaryValue", "OpcTag", tag.NodeId, false, HmiRoles.Viewer)]));

            x += cardWidth + hGap;
            if (x > maxX)
            {
                x = startX;
                y += cardHeight + vGap;
            }

            if (y > height - cardHeight)
            {
                break;
            }

            idx++;
        }

        return widgets;
    }

    private static string MapSketchKindToWidgetType(string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            return "Numeric";
        }

        if (string.Equals(kind, "button", StringComparison.OrdinalIgnoreCase)
            || string.Equals(kind, "command", StringComparison.OrdinalIgnoreCase))
        {
            return "CommandButton";
        }

        if (string.Equals(kind, "trend", StringComparison.OrdinalIgnoreCase)
            || string.Equals(kind, "chart", StringComparison.OrdinalIgnoreCase)
            || string.Equals(kind, "graph", StringComparison.OrdinalIgnoreCase))
        {
            return "TrendChart";
        }

        return "Numeric";
    }

    private static (bool Value, bool Recognized) ParseBooleanLike(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return (false, true);
        }

        if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "y", StringComparison.OrdinalIgnoreCase))
        {
            return (true, true);
        }

        if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "0", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "no", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "n", StringComparison.OrdinalIgnoreCase))
        {
            return (false, true);
        }

        return (false, false);
    }

    private static CsvParseResult ParseCsv(string csvContent)
    {
        var diagnostics = new List<ApiDiagnosticDto>();
        var rows = new List<CsvCellRow>();

        var currentCells = new List<string>();
        var currentCell = new StringBuilder();
        var inQuotes = false;
        var line = 1;
        var rowStartLine = 1;

        for (var i = 0; i < csvContent.Length; i++)
        {
            var ch = csvContent[i];

            if (ch == '"')
            {
                if (inQuotes && i + 1 < csvContent.Length && csvContent[i + 1] == '"')
                {
                    currentCell.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (!inQuotes && ch == ',')
            {
                currentCells.Add(currentCell.ToString());
                currentCell.Clear();
                continue;
            }

            if (!inQuotes && (ch == '\n' || ch == '\r'))
            {
                currentCells.Add(currentCell.ToString());
                currentCell.Clear();

                if (currentCells.Count > 1 || !string.IsNullOrWhiteSpace(currentCells[0]))
                {
                    rows.Add(new CsvCellRow(rowStartLine, currentCells.ToList()));
                }

                currentCells.Clear();

                if (ch == '\r' && i + 1 < csvContent.Length && csvContent[i + 1] == '\n')
                {
                    i++;
                }

                line++;
                rowStartLine = line;
                continue;
            }

            currentCell.Append(ch);
        }

        if (inQuotes)
        {
            diagnostics.Add(new ApiDiagnosticDto("error", "CSV_UNCLOSED_QUOTE", "CSV contains an unclosed quoted value.", line, null));
        }

        if (currentCell.Length > 0 || currentCells.Count > 0)
        {
            currentCells.Add(currentCell.ToString());
            if (currentCells.Count > 1 || !string.IsNullOrWhiteSpace(currentCells[0]))
            {
                rows.Add(new CsvCellRow(rowStartLine, currentCells.ToList()));
            }
        }

        if (rows.Count == 0)
        {
            diagnostics.Add(new ApiDiagnosticDto("error", "CSV_EMPTY", "CSV content did not contain any rows.", null, null));
            return new CsvParseResult(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase), [], diagnostics);
        }

        var headers = rows[0].Cells
            .Select((value, index) => new { Name = value.Trim(), index })
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key.ToLowerInvariant(), x => x.First().index, StringComparer.OrdinalIgnoreCase);

        if (!headers.ContainsKey("nodeid"))
        {
            diagnostics.Add(new ApiDiagnosticDto("error", "CSV_HEADER_NODEID_REQUIRED", "CSV header must include 'NodeId'.", rows[0].LineNumber, null));
        }

        return new CsvParseResult(headers, rows.Skip(1).ToList(), diagnostics);
    }

    private static string ReadCsvCell(IReadOnlyDictionary<string, int> headerIndex, CsvCellRow row, string key)
    {
        if (!headerIndex.TryGetValue(key.ToLowerInvariant(), out var idx) || idx >= row.Cells.Count)
        {
            return string.Empty;
        }

        return row.Cells[idx];
    }

    private sealed record TagCatalogItemDto(
        int Id,
        string DisplayName,
        string NodeId,
        string Area,
        string DataType,
        bool IsWritable,
        int SamplingIntervalMs);

    private sealed record CsvImportRequest(
        string CsvContent,
        bool Persist,
        int? ConnectionProfileId);

    private sealed record ParsedCsvTagRow(
        string DisplayName,
        string NodeId,
        string Area,
        string DataType,
        int SamplingIntervalMs,
        bool IsWritable,
        int SourceLine);

    private sealed record CsvCellRow(
        int LineNumber,
        IReadOnlyList<string> Cells);

    private sealed record CsvParseResult(
        IReadOnlyDictionary<string, int> HeaderIndex,
        IReadOnlyList<CsvCellRow> Rows,
        IReadOnlyList<ApiDiagnosticDto> Diagnostics);

    private sealed record ApiDiagnosticDto(
        string Severity,
        string Code,
        string Message,
        int? Line,
        int? Column);

    private sealed record SketchIntakeRequest(
        string Name,
        string Slug,
        int Width,
        int Height,
        string? Prompt,
        SketchLayoutInputDto? Sketch,
        int? MaxTags);

    private sealed record SketchLayoutInputDto(
        int SourceWidth,
        int SourceHeight,
        string? Notes,
        IReadOnlyList<SketchElementDto> Elements);

    private sealed record SketchElementDto(
        string Id,
        string Kind,
        string Label,
        double X,
        double Y,
        double Width,
        double Height);

    private sealed record DraftScreenRequest(
        string Name,
        string Slug,
        int Width,
        int Height,
        string? Prompt,
        string? SketchNotes,
        SketchLayoutInputDto? Sketch,
        IReadOnlyList<DraftWidgetDto>? Widgets);

    private sealed record DraftPreviewRequest(
        DraftScreenDto Draft,
        bool AllowOverwrite);

    private sealed record ApplyDraftRequest(
        DraftScreenDto Draft,
        string? ExpectedDraftHash,
        bool AllowOverwrite,
        bool Publish);

    private sealed record DraftMetadataDto(
        string DraftHash,
        string ApplyToken,
        int WidgetCount,
        DateTimeOffset GeneratedUtc);

    private sealed record RollbackMetadataDto(
        int ScreenId,
        string Slug,
        bool WasPublished,
        DateTimeOffset CapturedUtc,
        DraftScreenDto Draft);

    private sealed record DraftScreenDto(
        string Name,
        string Slug,
        int Width,
        int Height,
        string Prompt,
        string SketchNotes,
        IReadOnlyList<DraftWidgetDto> Widgets);

    private sealed record DraftWidgetDto(
        string Key,
        string WidgetType,
        string Title,
        int X,
        int Y,
        int Width,
        int Height,
        int ZIndex,
        string PropertiesJson,
        IReadOnlyList<DraftBindingDto> Bindings);

    private sealed record DraftBindingDto(
        string BindingRole,
        string SourceType,
        string SourceKey,
        bool WriteRequiresConfirm,
        string MinRole);
}
