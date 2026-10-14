namespace CrmMes.Api.Services;

/// <summary>Condivide l'esito dell'ultimo controllo di connettività al database per un breve TTL,
/// cosi' le chiamate ravvicinate a /health (Render che interroga l'healthCheckPath, il client
/// desktop che distingue "server giù" da "problema di rete") non aprono ciascuna una connessione
/// reale: la trovata del test di carico del 14/10/2026 (~145ms a chiamata contro ~27ms delle altre
/// rotte). Deduplica anche i controlli concorrenti all'interno della stessa finestra: tutte le
/// richieste in arrivo mentre un controllo è già in corso attendono lo stesso risultato invece di
/// aprirne uno a testa.</summary>
public sealed class DbConnectivityCache
{
    private readonly TimeSpan _ttl;
    private readonly object _lock = new();
    private Task<bool>? _pending;
    private bool _lastResult;
    private DateTime _lastCheckedUtc = DateTime.MinValue;

    public DbConnectivityCache(TimeSpan? ttl = null) => _ttl = ttl ?? TimeSpan.FromSeconds(4);

    public async Task<bool> IsConnectedAsync(Func<CancellationToken, Task<bool>> check, CancellationToken cancellationToken)
    {
        Task<bool> task;
        lock (_lock)
        {
            if (_pending is { IsCompleted: false } inFlight)
            {
                task = inFlight;
            }
            else if (DateTime.UtcNow - _lastCheckedUtc < _ttl)
            {
                return _lastResult;
            }
            else
            {
                task = check(cancellationToken);
                _pending = task;
            }
        }

        bool result;
        try
        {
            result = await task.ConfigureAwait(false);
        }
        finally
        {
            lock (_lock)
            {
                if (ReferenceEquals(_pending, task))
                {
                    _pending = null;
                }
            }
        }

        lock (_lock)
        {
            _lastResult = result;
            _lastCheckedUtc = DateTime.UtcNow;
        }

        return result;
    }
}
