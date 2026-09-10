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

public class OpcUaRuntimeConnectivityTests
{
    [Fact]
    public async Task WriteTagAsync_RejectsWriteWithoutConnectedSession()
    {
        await using var database = new SqliteConnection("Data Source=:memory:");
        await database.OpenAsync();
        var runtime = CreateRuntime(new DbContextOptionsBuilder<HmiDbContext>().UseSqlite(database).Options);

        var result = await runtime.WriteTagAsync("ns=3;s=Plant.Line.Start", "true");

        Assert.False(result.Succeeded);
        Assert.Equal("OPC UA session is not connected.", result.Message);
    }

    [Fact]
    public async Task RefreshNowAsync_ReportsDisabledProfileAsNotConnected()
    {
        await using var database = new SqliteConnection("Data Source=:memory:");
        await database.OpenAsync();
        var options = new DbContextOptionsBuilder<HmiDbContext>().UseSqlite(database).Options;
        await using (var setup = new HmiDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.OpcUaConnectionProfiles.AddRange(
                new OpcUaConnectionProfile { Name = "Older", EndpointUrl = "opc.tcp://localhost:4840", Enabled = false, UpdatedUtc = DateTimeOffset.UtcNow.AddMinutes(-1) },
                new OpcUaConnectionProfile { Name = "Disabled", EndpointUrl = "opc.tcp://localhost:4840", Enabled = false, UpdatedUtc = DateTimeOffset.UtcNow });
            await setup.SaveChangesAsync();
        }

        var runtime = CreateRuntime(options);
        await runtime.RefreshNowAsync();
        var status = await runtime.GetStatusAsync();

        Assert.False(status.IsConnected);
        Assert.Equal("Disabled", status.ActiveProfileName);
        Assert.NotNull(status.Detail);
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
