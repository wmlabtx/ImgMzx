using FFMpegCore;
using System.Collections.Concurrent;
using System.Net;

namespace ImgMzx;

public static class AppVideoServer
{
    private static HttpListener? _listener;
    private static int _port;
    private static readonly ConcurrentDictionary<string, byte[]> _tempCache = new();
    private static readonly ConcurrentDictionary<string, byte[]> _panelCache = new();

    public static void CachePanel(string hash, byte[] data) => _panelCache[hash] = data;
    public static void UncachePanel(string hash) => _panelCache.TryRemove(hash, out _);

    public static void Start()
    {
        GlobalFFOptions.Configure(o => o.BinaryFolder = AppDomain.CurrentDomain.BaseDirectory);
        _port = GetFreePort();
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{_port}/");
        _listener.Start();
        Task.Run(ServeAsync);
    }

    public static void Stop()
    {
        _listener?.Stop();
    }

    public static string GetUrl(string hash) => $"http://localhost:{_port}/{hash}";

    public static string RegisterTemp(string key, byte[] data)
    {
        _tempCache[key] = data;
        return $"http://localhost:{_port}/{key}";
    }

    public static void UnregisterTemp(string key) => _tempCache.TryRemove(key, out _);

    private static async Task ServeAsync()
    {
        while (_listener!.IsListening) {
            HttpListenerContext ctx;
            try {
                ctx = await _listener.GetContextAsync();
            }
            catch {
                break;
            }
            _ = Task.Run(() => HandleRequest(ctx));
        }
    }

    private static void HandleRequest(HttpListenerContext ctx)
    {
        try {
            var key = ctx.Request.Url!.LocalPath.TrimStart('/');
            var data = _tempCache.TryGetValue(key, out var cached) ? cached
                     : _panelCache.TryGetValue(key, out var panel) ? panel
                     : AppFile.ReadMex(key);
            if (data == null) {
                ctx.Response.StatusCode = 404;
                return;
            }

            ctx.Response.ContentType = "video/mp4";
            ctx.Response.Headers["Accept-Ranges"] = "bytes";

            var rangeHeader = ctx.Request.Headers["Range"];
            if (rangeHeader != null && rangeHeader.StartsWith("bytes=")) {
                var parts = rangeHeader[6..].Split('-');
                var start = long.Parse(parts[0]);
                var end = parts[1].Length > 0 ? long.Parse(parts[1]) : data.Length - 1;
                var length = end - start + 1;

                ctx.Response.StatusCode = 206;
                ctx.Response.ContentLength64 = length;
                ctx.Response.Headers["Content-Range"] = $"bytes {start}-{end}/{data.Length}";
                ctx.Response.OutputStream.Write(data, (int)start, (int)length);
            }
            else {
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentLength64 = data.Length;
                ctx.Response.OutputStream.Write(data, 0, data.Length);
            }
        }
        catch { }
        finally {
            ctx.Response.Close();
        }
    }

    private static int GetFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
