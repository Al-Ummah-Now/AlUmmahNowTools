using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AlUmmahNow.Data.Database
{
    /// <summary>
    /// Design-time factory for creating ImportDbContext instances during migrations
    /// </summary>
    public class ImportDbContextFactory : IDesignTimeDbContextFactory<ImportDbContext>
    {
        public ImportDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<ImportDbContext>();
            
            // Use a default connection string for migrations
            // This will be overridden at runtime by the actual application configuration
            optionsBuilder.UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=AlUmmahNowFileScan;Trusted_Connection=true");

            return new ImportDbContext(optionsBuilder.Options);
        }
    }
}
