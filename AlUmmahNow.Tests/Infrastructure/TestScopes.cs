using AlUmmahNow.Data.Database;
using AlUmmahNow.FileScanService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AlUmmahNow.Tests.Infrastructure;

/// <summary>
/// DI scopes like the FileScanService host builds them: a new <see cref="ImportDbContext"/> per scope over the
/// given options, plus the given approval service (or the real one, scoped, when null).
/// </summary>
public static class TestScopes
{
    public static IServiceScopeFactory For(DbContextOptions<ImportDbContext> options, IApprovalService? approvals = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => new ImportDbContext(options));
        if (approvals != null)
        {
            services.AddSingleton(approvals);
        }
        else
        {
            services.AddScoped<IApprovalService, ApprovalService>();
        }
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }
}
