using System.Net;
using System.Net.Sockets;
using System.Text;
using CrmMes.Desktop;

namespace CrmMes.Desktop.Tests;

/// <summary>Regression test for the production incident of 9 October 2026: a 500 from a real server (missing
/// migration) was shown to the user as "Server non raggiungibile", which sent them chasing a network problem
/// that did not exist. LoginAsync must now report a server error distinctly from a real connection failure.</summary>
public class ApiClientServerErrorTests
{
    [Fact]
    public async Task LoginAsync_OnServerError_ReportsServerErrorNotNetworkUnreachable()
    {
        var prefix = $"http://127.0.0.1:{GetFreePort()}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        var serverTask = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            context.Response.StatusCode = 500;
            context.Response.ContentType = "application/json";
            var bytes = Encoding.UTF8.GetBytes("""{"message":"colonna mancante nel database"}""");
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.OutputStream.Close();
        });

        var client = new ApiClient();
        client.SetBaseUrl(prefix);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.LoginAsync("a@b.it", "x"));

        await serverTask;
        listener.Stop();

        Assert.Contains("errore (500)", exception.Message);
        Assert.Contains("colonna mancante nel database", exception.Message);
        Assert.DoesNotContain("non raggiungibile", exception.Message);
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
