using IOTSnap.Hmi.Data;
using IOTSnap.Hmi.Data.Entities;
using IOTSnap.Hmi.Runtime.OpcUa;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;
using Xunit;

namespace IOTSnap.Hmi.Tests;

public class OpcUaRuntimeAlarmTests
{
    [Fact]
    public async Task GetAlarmsAsync_OrdersActiveAlarmsBySeverity()
    {
        await using var database = new SqliteConnection("Data Source=:memory:");
        await database.OpenAsync();
        var options = new DbContextOptionsBuilder<HmiDbContext>().UseSqlite(database).Options;
        await using (var setup = new HmiDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.OpcUaAlarmStates.AddRange(
                Alarm("low", true, false, 100),
                Alarm("high", true, false, 900),
                Alarm("cleared", false, false, 1000));
            await setup.SaveChangesAsync();
        }

        var alarms = await CreateRuntime(options).GetAlarmsAsync();

        Assert.Equal(["high", "low", "cleared"], alarms.Select(x => x.NodeId));
    }

    [Fact]
    public async Task AcknowledgeAlarmAsync_IsIdempotentAndPersistsTransitionAcrossRuntimeInstances()
    {
        await using var database = new SqliteConnection("Data Source=:memory:");
        await database.OpenAsync();
        var options = new DbContextOptionsBuilder<HmiDbContext>().UseSqlite(database).Options;
        await using (var setup = new HmiDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.OpcUaAlarmStates.Add(Alarm("line-start", true, false, 700));
            await setup.SaveChangesAsync();
        }

        var runtime = CreateRuntime(options);
        var first = await runtime.AcknowledgeAlarmAsync("line-start", "operator1");
        var duplicate = await runtime.AcknowledgeAlarmAsync("line-start", "operator1");

        Assert.True(first.Succeeded);
        Assert.True(duplicate.Succeeded);
        await using var verification = new HmiDbContext(options);
        var state = await verification.OpcUaAlarmStates.SingleAsync();
        var transition = await verification.OpcUaAlarmTransitions.SingleAsync();
        Assert.True(state.IsAcknowledged);
        Assert.Equal("operator1", state.AcknowledgedBy);
        Assert.Equal("Acknowledged", transition.Transition);
        Assert.Equal("operator1", transition.ActorUsername);

        var afterRestart = await CreateRuntime(options).GetAlarmsAsync();
        Assert.Single(afterRestart);
        Assert.True(afterRestart[0].IsAcknowledged);
    }

    [Fact]
    public async Task AcknowledgeAlarmAsync_AcknowledgesClearedUnacknowledgedAlarm()
    {
        await using var database = new SqliteConnection("Data Source=:memory:");
        await database.OpenAsync();
        var options = new DbContextOptionsBuilder<HmiDbContext>().UseSqlite(database).Options;
        await using (var setup = new HmiDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.OpcUaAlarmStates.Add(Alarm("cleared", false, false, 500));
            await setup.SaveChangesAsync();
        }

        var result = await CreateRuntime(options).AcknowledgeAlarmAsync("cleared", "operator1");

        Assert.True(result.Succeeded);
        await using var verification = new HmiDbContext(options);
        var state = await verification.OpcUaAlarmStates.SingleAsync();
        Assert.False(state.IsActive);
        Assert.True(state.IsAcknowledged);
        Assert.NotNull(state.AcknowledgedUtc);
    }

    [Fact]
    public async Task AlarmSynchronization_PersistsRaiseClearAndReraiseLifecycle()
    {
        await using var database = new SqliteConnection("Data Source=:memory:");
        await database.OpenAsync();
        var options = new DbContextOptionsBuilder<HmiDbContext>().UseSqlite(database).Options;
        await using (var setup = new HmiDbContext(options)) await setup.Database.EnsureCreatedAsync();
        var runtime = CreateRuntime(options);

        await SynchronizeAsync(runtime, Snapshot(hasAlarm: true));
        await runtime.AcknowledgeAlarmAsync("line-start", "operator1");
        await SynchronizeAsync(runtime, Snapshot(hasAlarm: false));
        await SynchronizeAsync(runtime, Snapshot(hasAlarm: true));

        await using var verification = new HmiDbContext(options);
        var transitions = await verification.OpcUaAlarmTransitions.OrderBy(x => x.Id).Select(x => x.Transition).ToListAsync();
        var state = await verification.OpcUaAlarmStates.SingleAsync();
        Assert.Equal(["Raised", "Acknowledged", "Cleared", "ReRaised"], transitions);
        Assert.True(state.IsActive);
        Assert.False(state.IsAcknowledged);
        Assert.Null(state.AcknowledgedBy);
    }

    private static OpcUaAlarmState Alarm(string nodeId, bool isActive, bool isAcknowledged, int severity) => new()
    {
        NodeId = nodeId,
        DisplayName = nodeId,
        Severity = severity,
        IsActive = isActive,
        IsAcknowledged = isAcknowledged,
        AlarmText = isActive ? "Active" : "Cleared",
        LastUpdatedUtc = DateTimeOffset.UtcNow
    };

    private static OpcUaTagSnapshot Snapshot(bool hasAlarm) => new()
    {
        NodeId = "line-start",
        DisplayName = "Line Start",
        StatusCode = hasAlarm ? "Bad" : "Good",
        AlarmText = hasAlarm ? "Line start fault" : string.Empty,
        IsStale = false,
        HasAlarm = hasAlarm,
        UpdatedUtc = DateTimeOffset.UtcNow
    };

    private static async Task SynchronizeAsync(OpcUaRuntimeService runtime, OpcUaTagSnapshot snapshot)
    {
        var snapshots = (Dictionary<string, OpcUaTagSnapshot>)typeof(OpcUaRuntimeService)
            .GetField("_tagSnapshots", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(runtime)!;
        snapshots[snapshot.NodeId] = snapshot;
        var method = typeof(OpcUaRuntimeService).GetMethod("SynchronizeAlarmStateAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)method.Invoke(runtime, [CancellationToken.None])!;
    }

    private static OpcUaRuntimeService CreateRuntime(DbContextOptions<HmiDbContext> options)
    {
        var root = Path.Combine(Path.GetTempPath(), "iotsnap-runtime-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return new(
            new TestDbContextFactory(options),
            NullLogger<OpcUaRuntimeService>.Instance,
            DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")))),
            new TestHostEnvironment(root));
    }

    private sealed class TestDbContextFactory(DbContextOptions<HmiDbContext> options) : IDbContextFactory<HmiDbContext>
    {
        public HmiDbContext CreateDbContext() => new(options);
        public Task<HmiDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(new HmiDbContext(options));
    }

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "IOTSnap.Hmi.Tests";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(contentRootPath);
    }
}
