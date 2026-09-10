using IOTSnap.Hmi.Data;
using IOTSnap.Hmi.Data.Entities;
using IOTSnap.Hmi.Features.Designer;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IOTSnap.Hmi.Tests;

public class RuntimeAuditServiceTests
{
    [Fact]
    public async Task RecordAsync_PersistsAuthenticatedActorAndCommandCorrelation()
    {
        await using var database = new SqliteConnection("Data Source=:memory:");
        await database.OpenAsync();
        var options = new DbContextOptionsBuilder<HmiDbContext>().UseSqlite(database).Options;
        await using (var setup = new HmiDbContext(options)) await setup.Database.EnsureCreatedAsync();

        var service = new RuntimeAuditService(new TestDbContextFactory(options));
        var actor = new RuntimeActor("operator1", "Casey Operator", HmiRoles.Operator);
        await service.RecordAsync(actor, "CommandWrite", "main/start/Action/ns=3;s=Plant.Line.Start", "Confirmed", "command-123", "Write confirmed.", CancellationToken.None);

        await using var verification = new HmiDbContext(options);
        var entry = await verification.OperatorAuditEntries.SingleAsync();
        Assert.Equal("operator1", entry.ActorUsername);
        Assert.Equal(HmiRoles.Operator, entry.ActorRole);
        Assert.Equal("CommandWrite", entry.ActionType);
        Assert.Equal("command-123", entry.CorrelationId);
        Assert.Equal("Confirmed", entry.Result);
    }

    [Theory]
    [InlineData("password=PLC-password-123")]
    [InlineData("Token: bearer-secret")]
    public async Task RecordAsync_RedactsSensitiveDetails(string detail)
    {
        await using var database = new SqliteConnection("Data Source=:memory:");
        await database.OpenAsync();
        var options = new DbContextOptionsBuilder<HmiDbContext>().UseSqlite(database).Options;
        await using (var setup = new HmiDbContext(options)) await setup.Database.EnsureCreatedAsync();

        await new RuntimeAuditService(new TestDbContextFactory(options)).RecordAsync(
            new RuntimeActor("admin", "Admin", HmiRoles.Admin),
            "OpcUaProfileUpdated", "profile-1", "Succeeded", null, detail, CancellationToken.None);

        await using var verification = new HmiDbContext(options);
        var entry = await verification.OperatorAuditEntries.SingleAsync();
        Assert.DoesNotContain("PLC-password-123", entry.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("bearer-secret", entry.Detail, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", entry.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecordAsync_BoundsDetailToDatabaseContract()
    {
        await using var database = new SqliteConnection("Data Source=:memory:");
        await database.OpenAsync();
        var options = new DbContextOptionsBuilder<HmiDbContext>().UseSqlite(database).Options;
        await using (var setup = new HmiDbContext(options)) await setup.Database.EnsureCreatedAsync();

        await new RuntimeAuditService(new TestDbContextFactory(options)).RecordAsync(
            new RuntimeActor("admin", "Admin", HmiRoles.Admin), "ScreenPublished", "main", "Succeeded", null, new string('x', 600), CancellationToken.None);

        await using var verification = new HmiDbContext(options);
        Assert.Equal(512, (await verification.OperatorAuditEntries.SingleAsync()).Detail!.Length);
    }

    private sealed class TestDbContextFactory(DbContextOptions<HmiDbContext> options) : IDbContextFactory<HmiDbContext>
    {
        public HmiDbContext CreateDbContext() => new(options);
        public Task<HmiDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(new HmiDbContext(options));
    }
}
