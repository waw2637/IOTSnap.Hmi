using IOTSnap.Hmi.Data;
using IOTSnap.Hmi.Data.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IOTSnap.Hmi.Tests;

public class HmiScreenPublicationTests
{
    [Fact]
    public async Task PublicationSnapshot_PersistsImmutableRollbackCandidate()
    {
        await using var database = new SqliteConnection("Data Source=:memory:");
        await database.OpenAsync();
        var options = new DbContextOptionsBuilder<HmiDbContext>().UseSqlite(database).Options;
        await using (var setup = new HmiDbContext(options))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.HmiScreenPublications.AddRange(
                new HmiScreenPublication { HmiScreenId = 1, Slug = "main", SnapshotJson = "{\"name\":\"Old\"}", PublishedBy = "admin", PublishedUtc = DateTimeOffset.UtcNow.AddMinutes(-1) },
                new HmiScreenPublication { HmiScreenId = 1, Slug = "main", SnapshotJson = "{\"name\":\"New\"}", PublishedBy = "admin", PublishedUtc = DateTimeOffset.UtcNow });
            await setup.SaveChangesAsync();
        }

        await using var verification = new HmiDbContext(options);
        var candidates = (await verification.HmiScreenPublications.AsNoTracking()
            .Where(x => x.HmiScreenId == 1)
            .ToListAsync())
            .OrderByDescending(x => x.PublishedUtc)
            .ToList();

        Assert.Equal(2, candidates.Count);
        Assert.Equal("{\"name\":\"New\"}", candidates[0].SnapshotJson);
        Assert.Equal("admin", candidates[0].PublishedBy);
    }
}
