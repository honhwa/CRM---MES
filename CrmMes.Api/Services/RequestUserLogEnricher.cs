using System.Security.Claims;
using Serilog.Context;

namespace CrmMes.Api.Services;

/// <summary>Senza questo, capire quale utente ha causato un errore in produzione significa incrociare
/// a mano l'orario del log con la cronologia dei login: niente nei log stessi lo dice. Aggiunge
/// UserId/email/ruolo a ogni riga di log emessa durante la richiesta di un utente autenticato
/// (anche le query EF Core, non solo la riga di riepilogo di Serilog.AspNetCore), cosi' un filtro
/// per utente su Grafana Loki (o sui file locali) basta a isolare cosa ha fatto un cliente specifico.
/// Pipeline: va dopo UseAuthentication (serve context.User popolato) e dentro UseSerilogRequestLogging
/// (cosi' il riepilogo finale la vede ancora, prima che il blocco `using` la rimuova).</summary>
public static class RequestUserLogEnricher
{
    public static async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = context.User.FindFirstValue(ClaimTypes.Email) ?? context.User.FindFirstValue(ClaimTypes.Name);
        var role = context.User.FindFirstValue(ClaimTypes.Role);

        var diagnosticContext = context.RequestServices.GetService<Serilog.IDiagnosticContext>();
        if (userId is not null) diagnosticContext?.Set("UserId", userId);
        if (email is not null) diagnosticContext?.Set("UserEmail", email);
        if (role is not null) diagnosticContext?.Set("UserRole", role);

        using (userId is null ? null : LogContext.PushProperty("UserId", userId))
        using (email is null ? null : LogContext.PushProperty("UserEmail", email))
        using (role is null ? null : LogContext.PushProperty("UserRole", role))
        {
            await next(context);
        }
    }
}
