using CrmMes.Api.Services;

namespace CrmMes.Api.Tests;

public class HealthConnectivityCacheTests
{
    [Fact]
    public async Task CallsEntroIlTtl_NonRipetonoIlControlloReale()
    {
        var cache = new DbConnectivityCache(TimeSpan.FromSeconds(30));
        var calls = 0;

        Task<bool> Check(CancellationToken ct)
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(true);
        }

        var first = await cache.IsConnectedAsync(Check, CancellationToken.None);
        var second = await cache.IsConnectedAsync(Check, CancellationToken.None);
        var third = await cache.IsConnectedAsync(Check, CancellationToken.None);

        Assert.True(first);
        Assert.True(second);
        Assert.True(third);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task DopoLaScadenzaDelTtl_IlControlloRealeRipartre()
    {
        var cache = new DbConnectivityCache(TimeSpan.FromMilliseconds(20));
        var calls = 0;

        Task<bool> Check(CancellationToken ct)
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(true);
        }

        await cache.IsConnectedAsync(Check, CancellationToken.None);
        await Task.Delay(50);
        await cache.IsConnectedAsync(Check, CancellationToken.None);

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ChiamateConcorrentiNellaStessaFinestra_CondividonoUnSoloControlloReale()
    {
        var cache = new DbConnectivityCache(TimeSpan.FromSeconds(30));
        var calls = 0;
        var gate = new TaskCompletionSource();

        async Task<bool> Check(CancellationToken ct)
        {
            Interlocked.Increment(ref calls);
            await gate.Task; // tiene il controllo "in corso" finche' non lo si sblocca sotto
            return true;
        }

        var concurrent = Enumerable.Range(0, 10)
            .Select(_ => cache.IsConnectedAsync(Check, CancellationToken.None))
            .ToArray();

        // Da' tempo a tutte le chiamate concorrenti di arrivare nella finestra "controllo in corso"
        // prima di sbloccarlo, cosi' nessuna ne avvia uno proprio.
        await Task.Delay(50);
        gate.SetResult();
        var results = await Task.WhenAll(concurrent);

        Assert.All(results, Assert.True);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task UnControlloCheFallisce_NonVieneMemorizzatoInCache()
    {
        var cache = new DbConnectivityCache(TimeSpan.FromSeconds(30));
        var calls = 0;

        Task<bool> FailingCheck(CancellationToken ct)
        {
            Interlocked.Increment(ref calls);
            throw new InvalidOperationException("connessione rifiutata");
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.IsConnectedAsync(FailingCheck, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.IsConnectedAsync(FailingCheck, CancellationToken.None));

        Assert.Equal(2, calls);
    }
}
