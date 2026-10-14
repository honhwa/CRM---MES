namespace CrmMes.Api.Services;

/// <summary>Riscrive internamente `/api/v1/...` in `/api/...` prima che qualunque altra cosa (intestazioni
/// di sicurezza, rate limiting, autenticazione, routing) veda la richiesta: /api/v1 diventa così un
/// contratto stabile per futuri integratori esterni senza duplicare le ~50 route dei controller
/// esistenti e senza cambiare nulla per i client attuali (desktop, web, console), che continuano a
/// chiamare /api/* esattamente come prima. Deve restare il primo middleware della pipeline, prima
/// di qualunque controllo basato sul percorso della richiesta.</summary>
public static class ApiVersionRewriteMiddleware
{
    public static Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (context.Request.Path.StartsWithSegments("/api/v1", out var remaining))
        {
            context.Request.Path = "/api" + remaining;
        }

        return next(context);
    }
}
