using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Mosaic.Data;

/// <summary>
/// Used only by the EF Core tooling (`dotnet ef migrations`) at design time.
/// The running app builds the context through DI instead.
/// </summary>
public class MosaicDbContextFactory : IDesignTimeDbContextFactory<MosaicDbContext>
{
    public MosaicDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<MosaicDbContext>()
            .UseSqlite("Data Source=mosaic_design.db")
            .Options;
        return new MosaicDbContext(options);
    }
}
