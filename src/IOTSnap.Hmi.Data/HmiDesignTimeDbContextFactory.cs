using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace IOTSnap.Hmi.Data;

public sealed class HmiDesignTimeDbContextFactory : IDesignTimeDbContextFactory<HmiDbContext>
{
    public HmiDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<HmiDbContext>();
        optionsBuilder.UseSqlite("Data Source=iotsnap-hmi.design.db");
        return new HmiDbContext(optionsBuilder.Options);
    }
}
