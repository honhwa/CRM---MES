using CrmMes.Core.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CrmMes.Api.Services;

public class DatabaseHealthCheck : IHealthCheck
{
    private readonly ApplicationDbContext _dbContext;
    private readonly DbConnectivityCache _cache;

    public DatabaseHealthCheck(ApplicationDbContext dbContext, DbConnectivityCache cache)
    {
        _dbContext = dbContext;
        _cache = cache;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var connected = await _cache.IsConnectedAsync(ct => _dbContext.Database.CanConnectAsync(ct), cancellationToken);
            return connected
                ? HealthCheckResult.Healthy("Connessione al database riuscita.")
                : HealthCheckResult.Unhealthy("Impossibile connettersi al database.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Eccezione durante la connessione al database.", ex);
        }
    }
}
