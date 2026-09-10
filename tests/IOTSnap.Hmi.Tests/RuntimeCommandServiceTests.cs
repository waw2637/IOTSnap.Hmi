using IOTSnap.Hmi.Data;
using IOTSnap.Hmi.Data.Entities;
using IOTSnap.Hmi.Features.Designer;
using IOTSnap.Hmi.Runtime.OpcUa;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IOTSnap.Hmi.Tests;

public class RuntimeCommandServiceTests
{
    [Fact]
    public async Task ExecuteAsync_RequiresThenHonorsConfirmationWithSameIdempotencyKey()
    {
        await using var database = new SqliteConnection("Data Source=:memory:");
        await database.OpenAsync();
        var options = new DbContextOptionsBuilder<HmiDbContext>().UseSqlite(database).Options;
        await using (var setup = new HmiDbContext(options)) await setup.Database.EnsureCreatedAsync();
        var factory = new TestDbContextFactory(options);
        var runtime = new FakeRuntime { Value = "true" };
        var service = new RuntimeCommandService(factory, runtime);
        var actor = new RuntimeActor("operator", "Operator", HmiRoles.Operator);
        var target = new RuntimeActionTarget("main", "start", "Action", "ns=2;s=start");

        var pending = await service.ExecuteAsync(actor, target, "true", true, "request-1", false, CancellationToken.None);
        var confirmed = await service.ExecuteAsync(actor, target, "true", true, "request-1", true, CancellationToken.None);

        Assert.Equal("ConfirmationRequired", pending.Status);
        Assert.Equal("Confirmed", confirmed.Status);
        Assert.Equal(1, runtime.WriteCount);
    }

    [Fact]
    public async Task ExecuteAsync_ExpiresUnconfirmedCommandWithoutWriting()
    {
        await using var database = new SqliteConnection("Data Source=:memory:"); await database.OpenAsync();
        var options = new DbContextOptionsBuilder<HmiDbContext>().UseSqlite(database).Options;
        await using (var setup = new HmiDbContext(options)) { await setup.Database.EnsureCreatedAsync(); setup.OperatorCommands.Add(new OperatorCommand { ActorUsername = "operator", IdempotencyKey = "expired", RequiresConfirmation = true, Status = "ConfirmationRequired", ConfirmationExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(-1) }); await setup.SaveChangesAsync(); }
        var runtime = new FakeRuntime();
        var result = await new RuntimeCommandService(new TestDbContextFactory(options), runtime).ExecuteAsync(new RuntimeActor("operator", "Operator", HmiRoles.Operator), new RuntimeActionTarget("main", "start", "Action", "node"), "true", true, "expired", true, CancellationToken.None);
        Assert.Equal("TimedOut", result.Status); Assert.Equal(0, runtime.WriteCount);
    }

    [Fact]
    public async Task ExecuteAsync_RecordsFailedTransportAndObservedMismatch()
    {
        await using var database = new SqliteConnection("Data Source=:memory:"); await database.OpenAsync();
        var options = new DbContextOptionsBuilder<HmiDbContext>().UseSqlite(database).Options;
        await using (var setup = new HmiDbContext(options)) await setup.Database.EnsureCreatedAsync();
        var service = new RuntimeCommandService(new TestDbContextFactory(options), new FakeRuntime { Value = "false" });
        var actor = new RuntimeActor("operator", "Operator", HmiRoles.Operator);
        var target = new RuntimeActionTarget("main", "start", "Action", "ns=2;s=start");
        var mismatch = await service.ExecuteAsync(actor, target, "true", false, "mismatch", false, CancellationToken.None);
        Assert.Equal("Accepted", mismatch.Status);

        var failed = await new RuntimeCommandService(new TestDbContextFactory(options), new FakeRuntime { Succeeds = false }).ExecuteAsync(actor, target, "true", false, "failed", false, CancellationToken.None);
        Assert.Equal("Failed", failed.Status);
    }

    [Fact]
    public async Task ExecuteAsync_HonorsCancellation()
    {
        await using var database = new SqliteConnection("Data Source=:memory:"); await database.OpenAsync();
        var options = new DbContextOptionsBuilder<HmiDbContext>().UseSqlite(database).Options;
        await using (var setup = new HmiDbContext(options)) await setup.Database.EnsureCreatedAsync();
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new RuntimeCommandService(new TestDbContextFactory(options), new FakeRuntime()).ExecuteAsync(new RuntimeActor("operator", "Operator", HmiRoles.Operator), new RuntimeActionTarget("main", "start", "Action", "node"), "true", false, "cancelled", false, cts.Token));
    }

    private sealed class TestDbContextFactory(DbContextOptions<HmiDbContext> options) : IDbContextFactory<HmiDbContext>
    { public HmiDbContext CreateDbContext() => new(options); public Task<HmiDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(new HmiDbContext(options)); }
    private sealed class FakeRuntime : IOpcUaRuntime
    {
        public string Value { get; init; } = string.Empty; public bool Succeeds { get; init; } = true; public int WriteCount { get; private set; }
        public Task<OpcUaRuntimeStatus> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult(new OpcUaRuntimeStatus());
        public Task<IReadOnlyList<OpcUaTagSnapshot>> GetTagSnapshotsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<OpcUaTagSnapshot>>([new() { NodeId = "ns=2;s=start", ValueText = Value }]);
        public Task<IReadOnlyList<OpcUaAlarmSnapshot>> GetAlarmsAsync(bool includeCleared = false, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<OpcUaAlarmSnapshot>>([]);
        public Task<OpcUaTagWriteResult> WriteTagAsync(string nodeId, string valueText, CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); WriteCount++; return Task.FromResult(new OpcUaTagWriteResult { Succeeded = Succeeds, Message = Succeeds ? "ok" : "transport failed" }); }
        public Task<OpcUaAlarmCommandResult> AcknowledgeAlarmAsync(string nodeId, string acknowledgedBy, CancellationToken cancellationToken = default) => Task.FromResult(new OpcUaAlarmCommandResult());
        public Task RefreshNowAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
