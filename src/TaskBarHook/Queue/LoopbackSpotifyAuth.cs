using System.Diagnostics;
using System.Net;
using System.Text;

namespace TaskBarHook.Queue;

public sealed class LoopbackSpotifyAuth : ISpotifyInteractiveAuth
{
    public async Task<string> GetAuthorizationCodeAsync(
        Uri authorizeUrl,
        Uri redirectUri,
        string expectedState,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(redirectUri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("O redirect PKCE deve ser 127.0.0.1.");
        }

        var prefix = $"http://127.0.0.1:{redirectUri.Port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();
        try
        {
            OpenBrowser(authorizeUrl);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(2));
            var contextTask = listener.GetContextAsync();
            var completed = await Task.WhenAny(contextTask, Task.Delay(Timeout.Infinite, timeout.Token));
            if (completed != contextTask)
            {
                throw new TimeoutException("A autorização do Spotify expirou.");
            }

            var context = await contextTask;
            var query = context.Request.QueryString;
            var html = "<html><body style='font-family:Segoe UI,sans-serif'>Pode voltar ao TaskBarHook.</body></html>";
            var bytes = Encoding.UTF8.GetBytes(html);
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes, timeout.Token);
            context.Response.OutputStream.Close();

            if (!string.IsNullOrEmpty(query["error"]))
            {
                throw new SpotifyAuthException(query["error"]!);
            }

            var state = query["state"];
            if (!string.Equals(state, expectedState, StringComparison.Ordinal))
            {
                throw new SpotifyAuthException("State OAuth inválido.");
            }

            var code = query["code"];
            if (string.IsNullOrWhiteSpace(code))
            {
                throw new SpotifyAuthException("A autorização não devolveu um código.");
            }

            return code;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static void OpenBrowser(Uri url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url.ToString(),
            UseShellExecute = true
        });
    }
}
