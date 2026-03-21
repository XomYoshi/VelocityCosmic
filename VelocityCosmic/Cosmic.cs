using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;

public static class Cosmic
{
    public static event Action<int> OnClientConnected;
    public static event Action<int> OnClientDisconnected;
    public static event Action<int, int, string> OnOutput;
    public static event Action<int, int, int> OnUserInfo;

    private const int WsPort = 24950;

    private static readonly string cosmicDir = @"C:\Cosmic";

    private static HttpListener _listener;
    private static CancellationTokenSource _cts;
    private static readonly ConcurrentDictionary<int, _ModuleClient> _clients = new ConcurrentDictionary<int, _ModuleClient>();
    private static bool _initialized;

    private static readonly string[] _robloxProcessNames = { "RobloxPlayerBeta", "Windows10Universal" };

    public static bool IsRunning { get; private set; }
    public static int ClientCount => _clients.Count;

    private static readonly HttpClient _http = new HttpClient();
    private static Task _initTask;

    public static bool IsReady => _initTask != null && _initTask.IsCompleted;

    public static async Task Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            Directory.CreateDirectory(cosmicDir);
            Directory.CreateDirectory(Path.Combine(cosmicDir, "Workspace"));
        }
        catch (Exception ex)
        {
            //Console.WriteLine($"[Cosmic] Ошибка при создании директории: {ex.Message}");
        }

        _initTask = Task.WhenAll(
            _DownloadFile("https://auth.cosmic.best/files/dll", Path.Combine(cosmicDir, "Cosmic-Module.dll")),
            _DownloadFile("https://auth.cosmic.best/files/injector", Path.Combine(cosmicDir, "Cosmic-Injector.exe"))
        );

        _cts = new CancellationTokenSource();
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{WsPort}/");
        _listener.Start();
        IsRunning = true;

        Task.Run(() => _AcceptLoop(_cts.Token));

        await _initTask.ConfigureAwait(false);
    }

    private static async Task _DownloadFile(string url, string destPath)
    {
        try
        {
            var bytes = await _http.GetByteArrayAsync(url).ConfigureAwait(false);
            File.WriteAllBytes(destPath, bytes);
        }
        catch (Exception ex)
        {
            //Debug.WriteLine($"Download error: {ex.Message}");
        }
    }

    public static void Shutdown()
    {
        if (!_initialized) return;
        IsRunning = false;
        _initialized = false;
        _cts?.Cancel();

        foreach (var client in _clients.Values)
        {
            try { client.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Shutdown", CancellationToken.None).Wait(1000); }
            catch { }
        }
        _clients.Clear();

        try { _listener?.Stop(); } catch { }
        try { _listener?.Close(); } catch { }
    }

    public static Dictionary<int, int> Attach()
    {
        _EnsureInit();
        var results = new Dictionary<int, int>();
        foreach (int pid in GetRobloxProcesses())
        {
            try { results[pid] = Attach(pid); }
            catch { results[pid] = -1; }
        }
        return results;
    }

    public static int Attach(int pid)
    {
        _EnsureInit();
        string injector = Path.Combine(cosmicDir, "Cosmic-Injector.exe");

        if (!File.Exists(injector))
            throw new FileNotFoundException($"Injector not found at: {injector}");

        using (var proc = Process.Start(new ProcessStartInfo
        {
            FileName = injector,
            Arguments = pid.ToString(),
            WorkingDirectory = cosmicDir,
            UseShellExecute = true,
            Verb = "runas"
        }))
        {
            if (!proc.WaitForExit(60000))
            {
                try { proc.Kill(); } catch { }
                return -1;
            }
            return proc.ExitCode;
        }
    }

    public static List<int> GetRobloxProcesses()
    {
        var pids = new List<int>();
        foreach (var name in _robloxProcessNames)
        {
            try
            {
                foreach (var p in Process.GetProcessesByName(name))
                {
                    pids.Add(p.Id);
                    p.Dispose();
                }
            }
            catch { }
        }
        return pids;
    }

    public static List<int> GetClients()
    {
        _EnsureInit();
        return _clients.Keys.ToList();
    }

    public static async Task WaitForReady()
    {
        if (_initTask != null)
            await _initTask.ConfigureAwait(false);
    }

    public static string GetAttachStatusMessage(int exitCode)
    {
        switch (exitCode)
        {
            case 6: return "Success";
            case -1: return "Initialization failure / Timeout";
            case 0: return "Failed to open Roblox process";
            case 1: return "Roblox version mismatch";
            case 2: return "Module DLL not found or corrupt";
            case 3: return "Memory operation failed";
            case 4: return "PDB download failed";
            case 5: return "Injection timeout";
            default: return $"Unknown exit code: {exitCode}";
        }
    }

    public static void Execute(string script)
    {
        _EnsureInit();
        _BroadcastAsync(script).GetAwaiter().GetResult();
    }

    public static async Task ExecuteAsync(string script)
    {
        _EnsureInit();
        await _BroadcastAsync(script).ConfigureAwait(false);
    }

    public static void Execute(int pid, string script)
    {
        _EnsureInit();
        _SendToAsync(pid, script).GetAwaiter().GetResult();
    }

    public static async Task ExecuteAsync(int pid, string script)
    {
        _EnsureInit();
        await _SendToAsync(pid, script).ConfigureAwait(false);
    }

    public static void SetMaxFps(int fps) => _SendSetting("Max-Fps", fps.ToString());
    public static void SetUnlockFps(bool enabled) => _SendSetting("Unlock-Fps", enabled ? "true" : "false");
    public static void SetAutoExecute(bool enabled) => _SendSetting("Auto-Execute", enabled ? "true" : "false");
    public static void SetConsoleRedirect(bool enabled) => _SendSetting("Console-Redirect", enabled ? "true" : "false");
    public static void SetConsoleFilter(string filter) => _SendSetting("Console-Filter", filter);

    private class _ModuleClient
    {
        public int Pid;
        public WebSocket Socket;
        public int UserId;
        public int GameId;
    }

    private static async Task _AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && IsRunning)
        {
            try
            {
                var context = await _listener.GetContextAsync().ConfigureAwait(false);

                if (!context.Request.IsWebSocketRequest)
                {
                    context.Response.StatusCode = 400;
                    context.Response.Close();
                    continue;
                }

                string pidHeader = context.Request.Headers["process-id"];
                if (!int.TryParse(pidHeader, out int pid))
                {
                    context.Response.StatusCode = 400;
                    context.Response.Close();
                    continue;
                }

                var wsContext = await context.AcceptWebSocketAsync(null).ConfigureAwait(false);
                var client = new _ModuleClient { Pid = pid, Socket = wsContext.WebSocket };
                _clients[pid] = client;

                OnClientConnected?.Invoke(pid);

                _ = Task.Run(() => _HandleClient(client, ct));
            }
            catch (ObjectDisposedException) { break; }
            catch (HttpListenerException) { break; }
            catch { }
        }
    }

    private static async Task _HandleClient(_ModuleClient client, CancellationToken ct)
    {
        var buffer = new byte[8192];
        try
        {
            while (client.Socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await client.Socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);

                if (result.MessageType == WebSocketMessageType.Close)
                    break;

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    string message;
                    if (result.EndOfMessage)
                    {
                        message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    }
                    else
                    {
                        using (var ms = new MemoryStream())
                        {
                            ms.Write(buffer, 0, result.Count);
                            while (!result.EndOfMessage)
                            {
                                result = await client.Socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                                ms.Write(buffer, 0, result.Count);
                            }
                            message = Encoding.UTF8.GetString(ms.ToArray());
                        }
                    }
                    _ProcessMessage(client, message);
                }
            }
        }
        catch { }
        finally
        {
            _clients.TryRemove(client.Pid, out _);
            OnClientDisconnected?.Invoke(client.Pid);
            try { client.Socket.Dispose(); } catch { }
        }
    }

    private static void _ProcessMessage(_ModuleClient client, string message)
    {
        if (string.IsNullOrEmpty(message) || message[0] != '{') return;
        try
        {
            if (message.Contains("\"Status\"") && message.Contains("\"Message\""))
            {
                int status = _JsonInt(message, "Status");
                string msg = _JsonString(message, "Message");
                if (msg != null)
                    OnOutput?.Invoke(client.Pid, status, msg);
            }
            else if (message.Contains("\"UserId\"") && message.Contains("\"GameId\""))
            {
                client.UserId = _JsonInt(message, "UserId");
                client.GameId = _JsonInt(message, "GameId");
                OnUserInfo?.Invoke(client.Pid, client.UserId, client.GameId);
            }
        }
        catch { }
    }

    private static async Task _BroadcastAsync(string message)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        var segment = new ArraySegment<byte>(bytes);
        var tasks = new List<Task>();
        foreach (var client in _clients.Values.ToArray())
        {
            if (client.Socket.State == WebSocketState.Open)
            {
                tasks.Add(client.Socket.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None));
            }
        }
        if (tasks.Count > 0) await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static async Task _SendToAsync(int pid, string message)
    {
        if (_clients.TryGetValue(pid, out var client) && client.Socket.State == WebSocketState.Open)
        {
            var bytes = Encoding.UTF8.GetBytes(message);
            await client.Socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static void _SendSetting(string name, object value)
    {
        _EnsureInit();
        string json = value is bool b
            ? $"{{\"Name\":\"{name}\",\"Value\":{(b ? "true" : "false")}}}"
            : $"{{\"Name\":\"{name}\",\"Value\":\"{value}\"}}";
        _BroadcastAsync(json).GetAwaiter().GetResult();
    }

    private static void _EnsureInit()
    {
        if (!_initialized)
            throw new InvalidOperationException("Cosmic.Initialize() must be called first.");
    }

    private static string _JsonString(string json, string key)
    {
        string search = $"\"{key}\"";
        int idx = json.IndexOf(search, StringComparison.Ordinal);
        if (idx < 0) return null;
        idx = json.IndexOf(':', idx + search.Length);
        if (idx < 0) return null;
        idx = json.IndexOf('"', idx + 1);
        if (idx < 0) return null;
        int end = json.IndexOf('"', idx + 1);
        if (end < 0) return null;
        return json.Substring(idx + 1, end - idx - 1)
            .Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\t", "\t")
            .Replace("\\\"", "\"").Replace("\\\\", "\\");
    }

    private static int _JsonInt(string json, string key)
    {
        string search = $"\"{key}\"";
        int idx = json.IndexOf(search, StringComparison.Ordinal);
        if (idx < 0) return 0;
        idx = json.IndexOf(':', idx + search.Length);
        if (idx < 0) return 0;
        idx++;
        while (idx < json.Length && (json[idx] == ' ' || json[idx] == '\t')) idx++;
        int start = idx;
        while (idx < json.Length && (char.IsDigit(json[idx]) || json[idx] == '-')) idx++;
        if (int.TryParse(json.Substring(start, idx - start), out int val))
            return val;
        return 0;
    }
}