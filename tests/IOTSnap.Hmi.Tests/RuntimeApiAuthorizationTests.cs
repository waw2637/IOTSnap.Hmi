using System.Net;
using System.Net.Http.Json;
using IOTSnap.Hmi;
using IOTSnap.Hmi.Data;
using IOTSnap.Hmi.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IOTSnap.Hmi.Tests;

public class RuntimeApiAuthorizationTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "iotsnap-tests", Guid.NewGuid().ToString("N"));
    private TestHostFactory _factory = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        _factory = new TestHostFactory(_root);
        _ = _factory.CreateClient();
        await using var scope = _factory.Services.CreateAsyncScope();
        await using var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<HmiDbContext>>().CreateDbContextAsync();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<LocalUser>>();
        var viewer = new LocalUser { Username = "viewer", DisplayName = "Viewer", Role = HmiRoles.Viewer, IsEnabled = true };
        viewer.PasswordHash = hasher.HashPassword(viewer, "password");
        var admin = new LocalUser { Username = "admin", DisplayName = "Admin", Role = HmiRoles.Admin, IsEnabled = true };
        admin.PasswordHash = hasher.HashPassword(admin, "password");
        db.LocalUsers.Add(viewer);
        db.LocalUsers.Add(admin);
        db.OpcUaAlarmTransitions.Add(new OpcUaAlarmTransition
        {
            NodeId = "ns=3;s=Plant.Line.Start",
            Transition = "Raised",
            Severity = 700,
            Detail = "Line start fault",
            OccurredUtc = DateTimeOffset.UtcNow
        });
        db.OperatorAuditEntries.AddRange(
            new OperatorAuditEntry
            {
                ActorUsername = "admin",
                ActorRole = HmiRoles.Admin,
                ActionType = "ScreenPublished",
                Resource = "main",
                Result = "Succeeded",
                OccurredUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
            },
            new OperatorAuditEntry
            {
                ActorUsername = "admin",
                ActorRole = HmiRoles.Admin,
                ActionType = "ScreenRolledBack",
                Resource = "main",
                Result = "Succeeded",
                OccurredUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
            },
            new OperatorAuditEntry
            {
                ActorUsername = "viewer",
                ActorRole = HmiRoles.Viewer,
                ActionType = "RuntimeRead",
                Resource = "runtime",
                Result = "Succeeded",
                OccurredUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
            });
        db.OpcUaTrendSamples.AddRange(
            new OpcUaTrendSample { NodeId = "ns=2;s=Demo.Static.Scalar.Double", ValueText = "10", StatusCode = "Good", SampledUtc = DateTimeOffset.UtcNow.AddMinutes(-2) },
            new OpcUaTrendSample { NodeId = "ns=2;s=Demo.Static.Scalar.Double", ValueText = "20", StatusCode = "Good", SampledUtc = DateTimeOffset.UtcNow.AddMinutes(-1) },
            new OpcUaTrendSample { NodeId = "ns=2;s=Demo.Static.Scalar.Double", ValueText = "30", StatusCode = "Good", SampledUtc = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() { _factory.Dispose(); Directory.Delete(_root, true); return Task.CompletedTask; }

    [Fact]
    public async Task RuntimeWrite_RejectsUnauthenticatedAndViewerRequests()
    {
        using var unauthenticated = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var unauthenticatedResponse = await unauthenticated.PostAsJsonAsync("/api/hmi/runtime/write/node", new { value = "true", screenSlug = "main", widgetKey = "start", bindingRole = "Action", idempotencyKey = "x" });
        Assert.True(unauthenticatedResponse.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Redirect or HttpStatusCode.Found or HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Viewer_LoginReturnsAuthenticationCookie()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        var response = await client.PostAsync("/login/submit", new FormUrlEncodedContent(new Dictionary<string, string> { ["username"] = "viewer", ["password"] = "password" }));
        Assert.True(response.StatusCode == HttpStatusCode.Found, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Viewer_CanReadRuntimeScreensAndDirectMutationIsRejected()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        var login = await client.PostAsync("/login/submit", new FormUrlEncodedContent(new Dictionary<string, string> { ["username"] = "viewer", ["password"] = "password" }));
        Assert.Equal(HttpStatusCode.Found, login.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/hmi/runtime/screens")).StatusCode);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/hmi/runtime/write/node")
        {
            Content = JsonContent.Create(new { value = "true", screenSlug = "main", widgetKey = "start", bindingRole = "Action", idempotencyKey = "viewer" })
        };
        var write = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    [Fact]
    public async Task Viewer_CanQueryBoundedAlarmHistory()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        var login = await client.PostAsync("/login/submit", new FormUrlEncodedContent(new Dictionary<string, string> { ["username"] = "viewer", ["password"] = "password" }));
        Assert.Equal(HttpStatusCode.Found, login.StatusCode);

        var response = await client.GetAsync($"/api/hmi/runtime/alarms/history?fromUtc={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("O"))}&toUtc={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(1).ToString("O"))}&take=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var history = await response.Content.ReadFromJsonAsync<List<AlarmHistoryResponse>>();
        var entry = Assert.Single(history!);
        Assert.Equal("Raised", entry.Transition);
    }

    [Fact]
    public async Task Viewer_CanQueryDownsampledPersistedTrendRange()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        await client.PostAsync("/login/submit", new FormUrlEncodedContent(new Dictionary<string, string> { ["username"] = "viewer", ["password"] = "password" }));

        var response = await client.GetAsync($"/api/hmi/runtime/trends/{Uri.EscapeDataString("ns=2;s=Demo.Static.Scalar.Double")}?fromUtc={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(-1).ToString("O"))}&toUtc={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(1).ToString("O"))}&maxPoints=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var samples = await response.Content.ReadFromJsonAsync<List<TrendSampleResponse>>();
        Assert.Equal(2, samples!.Count);
        Assert.Equal("10", samples[0].ValueText);
        Assert.Equal("30", samples[1].ValueText);
    }

    [Fact]
    public async Task Admin_CanQueryFilteredAuditHistoryOnSqlite()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        var login = await client.PostAsync("/login/submit", new FormUrlEncodedContent(new Dictionary<string, string> { ["username"] = "admin", ["password"] = "password" }));
        Assert.Equal(HttpStatusCode.Found, login.StatusCode);

        var fromUtc = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(-1).ToString("O"));
        var toUtc = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(1).ToString("O"));
        var response = await client.GetAsync($"/api/hmi/designer/audit?actionType=ScreenRolledBack&actor=admin&fromUtc={fromUtc}&toUtc={toUtc}&page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<AuditQueryResponse>();
        Assert.NotNull(payload);
        Assert.Equal(1, payload.Total);
        var item = Assert.Single(payload.Items);
        Assert.Equal("ScreenRolledBack", item.ActionType);
        Assert.Equal("admin", item.ActorUsername);
    }

    [Fact]
    public async Task HealthEndpoints_ExposeLivenessAndBoundedReadiness()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var health = await client.GetAsync("/healthz");
        var ready = await client.GetAsync("/readyz");
        var readyBody = await ready.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.Contains("degraded", readyBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EndpointUrl", readyBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password", readyBody, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetCookie(HttpResponseMessage response) => response.Headers.GetValues("Set-Cookie").First().Split(';')[0];

    private sealed record AlarmHistoryResponse(string NodeId, string Transition, int Severity);
    private sealed record TrendSampleResponse(string? ValueText, string? StatusCode, DateTimeOffset SampledUtc);
    private sealed record AuditQueryResponse(int Total, int Page, int PageSize, List<AuditEntryResponse> Items);
    private sealed record AuditEntryResponse(long Id, string ActorUsername, string ActorRole, string ActionType, string Resource, string Result, string? CorrelationId, string? Detail, DateTimeOffset OccurredUtc);

    private sealed class TestHostFactory(string root) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseContentRoot(root);
            builder.UseSetting("ConnectionStrings:Hmi", $"Data Source={Path.Combine(root, "test.db")}");
        }
    }
}
