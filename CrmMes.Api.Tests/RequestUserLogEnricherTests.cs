using System.Security.Claims;
using CrmMes.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Events;

namespace CrmMes.Api.Tests;

/// <summary>Senza l'arricchimento, un log durante una richiesta non dice quale utente l'ha causata.
/// Qui si verifica che l'arricchimento compaia sia sulle righe emesse durante la richiesta (LogContext,
/// riusato da ogni log successivo nello stesso contesto asincrono) sia sul riepilogo finale di
/// Serilog.AspNetCore (IDiagnosticContext) — e che un utente anonimo non aggiunga nulla.</summary>
public class RequestUserLogEnricherTests
{
    private sealed class FakeDiagnosticContext : IDiagnosticContext
    {
        public Dictionary<string, object?> Properties { get; } = new();
        public void Set(string propertyName, object? value, bool destructureObjects = false) => Properties[propertyName] = value;
        public void SetException(Exception exception) { }
    }

    private sealed class CollectingSink : Serilog.Core.ILogEventSink
    {
        public List<LogEvent> Events { get; } = new();
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    private static DefaultHttpContext AuthenticatedContext(FakeDiagnosticContext diagnosticContext)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "11111111-1111-1111-1111-111111111111"),
            new Claim(ClaimTypes.Email, "operatore@prova.local"),
            new Claim(ClaimTypes.Role, "Operator"),
        };
        var identity = new ClaimsIdentity(claims, authenticationType: "Test");
        var services = new ServiceCollection();
        services.AddSingleton<IDiagnosticContext>(diagnosticContext);

        return new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity),
            RequestServices = services.BuildServiceProvider(),
        };
    }

    [Fact]
    public async Task RichiestaAutenticata_ArricchisceIlRiepilogoFinale()
    {
        var diagnosticContext = new FakeDiagnosticContext();
        var context = AuthenticatedContext(diagnosticContext);

        await RequestUserLogEnricher.InvokeAsync(context, _ => Task.CompletedTask);

        Assert.Equal("11111111-1111-1111-1111-111111111111", diagnosticContext.Properties["UserId"]);
        Assert.Equal("operatore@prova.local", diagnosticContext.Properties["UserEmail"]);
        Assert.Equal("Operator", diagnosticContext.Properties["UserRole"]);
    }

    // Le istanze di logger sono locali al test (mai Serilog.Log.Logger, un campo statico globale):
    // sotto l'esecuzione parallela di xUnit, mutarlo da più test farebbe correre l'uno sull'altro.
    // LogContext invece è AsyncLocal per natura di Serilog, quindi resta isolato per test da solo.
    private static Serilog.Core.Logger BuildLogger(CollectingSink sink) =>
        new LoggerConfiguration().Enrich.FromLogContext().WriteTo.Sink(sink).CreateLogger();

    [Fact]
    public async Task RichiestaAutenticata_ArricchisceOgniLogEmessoDuranteLaRichiesta()
    {
        var sink = new CollectingSink();
        using var logger = BuildLogger(sink);
        var context = AuthenticatedContext(new FakeDiagnosticContext());

        await RequestUserLogEnricher.InvokeAsync(context, _ =>
        {
            logger.Information("Dentro la richiesta");
            return Task.CompletedTask;
        });

        var evt = Assert.Single(sink.Events);
        Assert.True(evt.Properties.TryGetValue("UserId", out var userId));
        Assert.Equal("\"11111111-1111-1111-1111-111111111111\"", userId!.ToString());
    }

    [Fact]
    public async Task RichiestaAnonima_NonAggiungeNienteAlLogContext()
    {
        var sink = new CollectingSink();
        using var logger = BuildLogger(sink);
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity()), // non autenticato
            RequestServices = new ServiceCollection().AddSingleton<IDiagnosticContext>(new FakeDiagnosticContext()).BuildServiceProvider(),
        };

        await RequestUserLogEnricher.InvokeAsync(context, _ =>
        {
            logger.Information("Richiesta anonima");
            return Task.CompletedTask;
        });

        var evt = Assert.Single(sink.Events);
        Assert.False(evt.Properties.ContainsKey("UserId"));
    }

    [Fact]
    public async Task DopoLaRichiesta_LeProprietaNonRestanoInCodaAlContesto()
    {
        var context = AuthenticatedContext(new FakeDiagnosticContext());
        await RequestUserLogEnricher.InvokeAsync(context, _ => Task.CompletedTask);

        var sink = new CollectingSink();
        using var logger = BuildLogger(sink);
        logger.Information("Dopo la richiesta, fuori dal blocco using");

        var evt = Assert.Single(sink.Events);
        Assert.False(evt.Properties.ContainsKey("UserId"));
    }
}
