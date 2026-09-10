using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IOTSnap.Hmi;
using IOTSnap.Hmi.Data;
using IOTSnap.Hmi.Data.Entities;
using IOTSnap.Hmi.Runtime.OpcUa;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IOTSnap.Hmi.Tests;

public sealed class LiveOpcQualificationTests : IAsyncLifetime
{
    private const string ProcessValueNodeId = "ns=3;s=FastUInt1";
    private const string StartCommandNodeId = "ns=3;s=Plant.Line.Start";
    private readonly string _endpoint = Environment.GetEnvironmentVariable("IOTSNAP_LIVE_OPC_ENDPOINT") ?? "opc.tcp://127.0.0.1:50001";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "iotsnap-live-qualification", Guid.NewGuid().ToString("N"));
    private QualificationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        _factory = new QualificationFactory(_root);
        _ = _factory.CreateClient();
        await SeedDatabaseAsync();
        await WaitForConnectionAsync();
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        Directory.Delete(_root, true);
        return Task.CompletedTask;
    }

    [Fact]
    [Trait("Category", "LiveOpcQualification")]
    public async Task DiscoveryAndBrowse_FindsRepresentativeLiveNodes()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var discovery = scope.ServiceProvider.GetRequiredService<IOpcUaDiscovery>();

        var endpoints = await discovery.DiscoverEndpointsAsync(_endpoint);
        Assert.NotEmpty(endpoints);

        var selectedEndpoint = endpoints.FirstOrDefault(x =>
            x.UseSecurity
            && x.SupportsAnonymous
            && string.Equals(x.SecurityPolicy, "Basic256Sha256", StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.SecurityMode, "SignAndEncrypt", StringComparison.OrdinalIgnoreCase));

        Assert.True(selectedEndpoint is not null, $"No secure anonymous endpoint was discovered. Endpoints: {string.Join(", ", endpoints.Select(x => $"{x.EndpointUrl} [{x.SecurityPolicy}/{x.SecurityMode}] anon={x.SupportsAnonymous}"))}");

        var harnessTags = await discovery.BrowseTagsAsync(new OpcUaBrowseRequest(
            selectedEndpoint!.EndpointUrl,
            selectedEndpoint.UseSecurity,
            selectedEndpoint.SecurityPolicy,
            selectedEndpoint.SecurityMode,
            "Anonymous",
            null,
            null,
            8,
            512,
            "Objects/OpcPlc/Harness"));

        var telemetryTags = await discovery.BrowseTagsAsync(new OpcUaBrowseRequest(
            selectedEndpoint.EndpointUrl,
            selectedEndpoint.UseSecurity,
            selectedEndpoint.SecurityPolicy,
            selectedEndpoint.SecurityMode,
            "Anonymous",
            null,
            null,
            8,
            512,
            "Objects/OpcPlc/Telemetry/Fast"));

        Assert.Contains(harnessTags, x => x.NodeId == StartCommandNodeId && x.IsWritable);
        Assert.Contains(telemetryTags, x => x.NodeId == ProcessValueNodeId && string.Equals(x.DataType, "UInt32", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    [Trait("Category", "LiveOpcQualification")]
    public async Task RepresentativeRuntimeFlow_PassesAgainstLiveOpcEndpoint()
    {
        using var viewer = await LoginAsync("viewer");
        using var operatorClient = await LoginAsync("operator");

        var screenResponse = await viewer.GetAsync("/api/hmi/runtime/screens/main");
        Assert.Equal(HttpStatusCode.OK, screenResponse.StatusCode);
        var screenJson = JsonDocument.Parse(await screenResponse.Content.ReadAsStringAsync());
        var widgets = screenJson.RootElement.GetProperty("widgets");
        Assert.True(widgets.GetArrayLength() >= 2);
        Assert.Contains(widgets.EnumerateArray(), x =>
            x.GetProperty("bindings").EnumerateArray().Any(b => b.GetProperty("sourceKey").GetString() == ProcessValueNodeId));

        var viewerWrite = await viewer.PostAsJsonAsync(
            $"/api/hmi/runtime/write/{Uri.EscapeDataString(StartCommandNodeId)}",
            new
            {
                screenSlug = "main",
                widgetKey = "cmd-start",
                bindingRole = "Action",
                value = "true",
                idempotencyKey = "viewer-live-denied",
                confirmed = true
            });
        var viewerWriteBody = await viewerWrite.Content.ReadAsStringAsync();
        Assert.True(viewerWrite.StatusCode == HttpStatusCode.Forbidden, viewerWriteBody);

        var pending = await operatorClient.PostAsJsonAsync(
            $"/api/hmi/runtime/write/{Uri.EscapeDataString(StartCommandNodeId)}",
            new
            {
                screenSlug = "main",
                widgetKey = "cmd-start",
                bindingRole = "Action",
                value = "true",
                idempotencyKey = "operator-live-command",
                confirmed = false
            });
        var pendingBody = await pending.Content.ReadAsStringAsync();
        Assert.True(pending.StatusCode == HttpStatusCode.Accepted, pendingBody);

        var confirmed = await operatorClient.PostAsJsonAsync(
            $"/api/hmi/runtime/write/{Uri.EscapeDataString(StartCommandNodeId)}",
            new
            {
                screenSlug = "main",
                widgetKey = "cmd-start",
                bindingRole = "Action",
                value = "true",
                idempotencyKey = "operator-live-command",
                confirmed = true
            });
        var confirmedBody = await confirmed.Content.ReadAsStringAsync();
        Assert.True(confirmed.StatusCode == HttpStatusCode.OK, confirmedBody);

        var commandJson = JsonDocument.Parse(confirmedBody);
        var commandStatus = commandJson.RootElement.GetProperty("status").GetString();
        Assert.Contains(commandStatus, new[] { "Accepted", "Confirmed" });

        await WaitForTrendSamplesAsync();

        var fromUtc = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMinutes(-10).ToString("O"));
        var toUtc = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMinutes(1).ToString("O"));
        var trendResponse = await viewer.GetAsync($"/api/hmi/runtime/trends/{Uri.EscapeDataString(ProcessValueNodeId)}?fromUtc={fromUtc}&toUtc={toUtc}&maxPoints=10");
        Assert.Equal(HttpStatusCode.OK, trendResponse.StatusCode);
        var trend = await trendResponse.Content.ReadFromJsonAsync<List<TrendSampleDto>>();
        Assert.NotNull(trend);
        Assert.NotEmpty(trend);

        await using (var scope = _factory.Services.CreateAsyncScope())
        await using (var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<HmiDbContext>>().CreateDbContextAsync())
        {
            var auditEntries = await db.OperatorAuditEntries
                .AsNoTracking()
                .Where(x => x.ActorUsername == "operator" && x.ActionType == "CommandWrite" && x.Resource.Contains(StartCommandNodeId))
                .ToListAsync();
            auditEntries = auditEntries
                .OrderBy(x => x.OccurredUtc)
                .ToList();

            Assert.Contains(auditEntries, x => x.Result == "ConfirmationRequired");
            Assert.Contains(auditEntries, x => x.Result is "Accepted" or "Confirmed");
        }

        await RestartHostAsync();
        await WaitForConnectionAsync();

        using var viewerAfterRestart = await LoginAsync("viewer");
        var trendAfterRestartResponse = await viewerAfterRestart.GetAsync($"/api/hmi/runtime/trends/{Uri.EscapeDataString(ProcessValueNodeId)}?fromUtc={fromUtc}&toUtc={toUtc}&maxPoints=10");
        Assert.Equal(HttpStatusCode.OK, trendAfterRestartResponse.StatusCode);
        var trendAfterRestart = await trendAfterRestartResponse.Content.ReadFromJsonAsync<List<TrendSampleDto>>();
        Assert.NotNull(trendAfterRestart);
        Assert.NotEmpty(trendAfterRestart);
    }

    private async Task SeedDatabaseAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        await using var db = await services.GetRequiredService<IDbContextFactory<HmiDbContext>>().CreateDbContextAsync();
        var passwordHasher = services.GetRequiredService<IPasswordHasher<LocalUser>>();

        db.LocalUsers.RemoveRange(db.LocalUsers);

        foreach (var (username, role, displayName) in new[]
        {
            ("admin", HmiRoles.Admin, "Admin"),
            ("operator", HmiRoles.Operator, "Operator"),
            ("viewer", HmiRoles.Viewer, "Viewer")
        })
        {
            var user = new LocalUser
            {
                Username = username,
                DisplayName = displayName,
                Role = role,
                IsEnabled = true,
                CreatedUtc = DateTimeOffset.UtcNow
            };
            user.PasswordHash = passwordHasher.HashPassword(user, "admin");
            db.LocalUsers.Add(user);
        }

        var profile = await db.OpcUaConnectionProfiles.OrderBy(x => x.Id).FirstAsync();
        profile.Name = "Live Qualification PLC";
        profile.EndpointUrl = _endpoint;
        profile.Enabled = true;
        profile.UseSecurity = true;
        profile.SecurityPolicy = "Basic256Sha256";
        profile.SecurityMode = "SignAndEncrypt";
        profile.AuthenticationMode = "Anonymous";
        profile.UpdatedUtc = DateTimeOffset.UtcNow;

        db.OpcUaNodeMappings.RemoveRange(db.OpcUaNodeMappings);
        db.OpcUaNodeMappings.AddRange(
            new OpcUaNodeMapping
            {
                OpcUaConnectionProfileId = profile.Id,
                DisplayName = "Process Value",
                NodeId = ProcessValueNodeId,
                DataType = "UInt32",
                SamplingIntervalMs = 1000,
                IsWritable = false,
                Area = "Process"
            },
            new OpcUaNodeMapping
            {
                OpcUaConnectionProfileId = profile.Id,
                DisplayName = "Start Command",
                NodeId = StartCommandNodeId,
                DataType = "Boolean",
                SamplingIntervalMs = 250,
                IsWritable = true,
                Area = "Commands"
            });

        await db.SaveChangesAsync();
        await services.GetRequiredService<IOpcUaRuntime>().RefreshNowAsync();
    }

    private async Task RestartHostAsync()
    {
        _factory.Dispose();
        _factory = new QualificationFactory(_root);
        _ = _factory.CreateClient();
        await _factory.Services.GetRequiredService<IOpcUaRuntime>().RefreshNowAsync();
    }

    private async Task WaitForConnectionAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var runtime = scope.ServiceProvider.GetRequiredService<IOpcUaRuntime>();

        for (var attempt = 0; attempt < 20; attempt++)
        {
            await runtime.RefreshNowAsync();
            var status = await runtime.GetStatusAsync();
            var tags = await runtime.GetTagSnapshotsAsync();
            if (status.IsConnected && tags.Any(x => x.NodeId == ProcessValueNodeId) && tags.Any(x => x.NodeId == StartCommandNodeId))
            {
                return;
            }

            await Task.Delay(1000);
        }

        var finalStatus = await runtime.GetStatusAsync();
        throw new Xunit.Sdk.XunitException($"OPC UA runtime did not connect to {_endpoint}. Last detail: {finalStatus.Detail}");
    }

    private async Task WaitForTrendSamplesAsync()
    {
        for (var attempt = 0; attempt < 15; attempt++)
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            await using var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<HmiDbContext>>().CreateDbContextAsync();
            var sampleCount = await db.OpcUaTrendSamples.CountAsync(x => x.NodeId == ProcessValueNodeId);
            if (sampleCount > 0)
            {
                return;
            }

            await Task.Delay(1000);
        }

        await using var failureScope = _factory.Services.CreateAsyncScope();
        var runtime = failureScope.ServiceProvider.GetRequiredService<IOpcUaRuntime>();
        var status = await runtime.GetStatusAsync();
        var tags = await runtime.GetTagSnapshotsAsync();
        await using var failureDb = await failureScope.ServiceProvider.GetRequiredService<IDbContextFactory<HmiDbContext>>().CreateDbContextAsync();
        var trendCounts = await failureDb.OpcUaTrendSamples
            .AsNoTracking()
            .GroupBy(x => x.NodeId)
            .Select(x => new { NodeId = x.Key, Count = x.Count() })
            .OrderByDescending(x => x.Count)
            .Take(10)
            .ToListAsync();

        var tagSummary = string.Join(", ", tags
            .Where(x => x.NodeId == ProcessValueNodeId || x.NodeId == StartCommandNodeId)
            .Select(x => $"{x.NodeId}: value={x.ValueText ?? "<null>"} status={x.StatusCode ?? "<null>"} updated={x.UpdatedUtc:O}"));
        var trendSummary = trendCounts.Count == 0
            ? "<none>"
            : string.Join(", ", trendCounts.Select(x => $"{x.NodeId}={x.Count}"));

        throw new Xunit.Sdk.XunitException(
            $"No persisted trend samples were captured from the live OPC endpoint. " +
            $"Runtime detail: {status.Detail} " +
            $"Tracked tags: {tagSummary} " +
            $"Persisted trend counts: {trendSummary}");
    }

    private async Task<HttpClient> LoginAsync(string username)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        var response = await client.PostAsync("/login/submit", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = username,
            ["password"] = "admin",
            ["returnUrl"] = "/"
        }));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        return client;
    }

    private sealed record TrendSampleDto(string? ValueText, string? StatusCode, DateTimeOffset SampledUtc);

    private sealed class QualificationFactory(string root) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseContentRoot(root);
            builder.UseSetting("ConnectionStrings:Hmi", $"Data Source={Path.Combine(root, "qualification.db")}");
        }
    }
}
