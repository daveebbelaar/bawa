using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace __APP_NAME__.Agent;

/// A named pipe that only this Windows user can open. The app listens; the CLI connects,
/// sends one JSON line, and reads one JSON line back.
///   __APP_NAME__-agent-<session>                the real app
///   __APP_NAME__-agent-<session>-<store hash>   a test run (APP_STORE)
///   APP_AGENT_PIPE=<name>                       any other name
/// Shared: the app and the __APP_NAME__.Cli project both compile this file.
public static class AgentPipe
{
    public const string AppName = "__APP_NAME__";
    public const string CliName = "__CLI_NAME__";

    private static readonly UTF8Encoding Utf8 = new(false);

    /// Pipe names are global to the PC, so the name holds the session (two people signed in)
    /// and, for a test run, a hash of the store path (a test copy next to the real app).
    public static string Name
    {
        get
        {
            var custom = Environment.GetEnvironmentVariable("APP_AGENT_PIPE");
            if (!string.IsNullOrEmpty(custom)) return custom;
            var name = $"{AppName}-agent-{Process.GetCurrentProcess().SessionId}";
            var store = Environment.GetEnvironmentVariable("APP_STORE");
            if (string.IsNullOrEmpty(store)) return name;
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(store).ToLowerInvariant()));
            return $"{name}-{Convert.ToHexString(hash)[..12].ToLowerInvariant()}";
        }
    }

    /// Where build.ps1 installs the app. APP_EXE points somewhere else.
    public static string AppExe =>
        Environment.GetEnvironmentVariable("APP_EXE") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName, AppName + ".exe");

    // App side

    /// Answers requests until the process ends. `handle` gets one request line and returns one
    /// reply line. Throws when another copy of the app already listens on this pipe.
    public static async Task ListenAsync(Func<string, Task<string>> handle)
    {
        var first = true;
        while (true)
        {
            // CurrentUserOnly: the pipe's access list holds only this user.
            // FirstPipeInstance on the first one: fail if another process owns the name.
            var options = PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | (first ? PipeOptions.FirstPipeInstance : PipeOptions.None);
            var server = new NamedPipeServerStream(Name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte, options);
            first = false;
            await server.WaitForConnectionAsync();
            _ = ServeAsync(server, handle);
        }
    }

    private static async Task ServeAsync(NamedPipeServerStream pipe, Func<string, Task<string>> handle)
    {
        await using (pipe)
        {
            try
            {
                using var reader = new StreamReader(pipe, Utf8, false, 4096, leaveOpen: true);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var request = await reader.ReadLineAsync(timeout.Token);
                if (request is null) return;
                var reply = await handle(request);
                await pipe.WriteAsync(Utf8.GetBytes(reply + "\n"));
                await pipe.FlushAsync();
                // Closing the server end throws away bytes the client has not read yet.
                pipe.WaitForPipeDrain();
            }
            catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException)
            {
                // The client went away. Nothing to answer.
            }
        }
    }

    // CLI side

    /// Sends one request and returns the reply. Starts the app when it is not running,
    /// unless `launch` is false.
    public static async Task<JsonObject> SendAsync(JsonObject request, bool launch)
    {
        if (await TryExchangeAsync(request, connectMs: 300) is { } reply) return reply;
        if (!launch) throw AgentError.Unavailable($"{AppName} is not running. Open it, or leave out --no-launch");
        StartApp();
        return await TryExchangeAsync(request, connectMs: 20_000)
            ?? throw AgentError.Unavailable($"{AppName} did not answer within 20 seconds on pipe {Name}");
    }

    /// The reply, or null when no app listens on the pipe within `connectMs`.
    public static async Task<JsonObject?> TryExchangeAsync(JsonObject request, int connectMs)
    {
        using var pipe = new NamedPipeClientStream(".", Name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            // Connect keeps trying while the pipe does not exist yet, so this also waits for a starting app.
            await pipe.ConnectAsync(connectMs);
        }
        catch (TimeoutException)
        {
            return null;
        }
        await pipe.WriteAsync(Utf8.GetBytes(request.ToJsonString(AgentJson.Options) + "\n"));
        await pipe.FlushAsync();
        using var reader = new StreamReader(pipe, Utf8, false, 4096, leaveOpen: true);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        string? line;
        try { line = await reader.ReadLineAsync(timeout.Token); }
        catch (OperationCanceledException) { throw AgentError.Unavailable($"{AppName} did not answer within 60 seconds"); }
        if (line is null) throw AgentError.Unavailable($"{AppName} closed the connection without an answer");
        try
        {
            return JsonNode.Parse(line) as JsonObject ?? throw AgentError.Invalid("the app sent no answer");
        }
        catch (JsonException)
        {
            throw AgentError.Invalid("the app sent an answer that is not JSON");
        }
    }

    /// Starts the app in the background with this process's environment, so a test run
    /// (APP_STORE) starts a test copy, never the real one.
    private static void StartApp()
    {
        var exe = AppExe;
        if (!File.Exists(exe)) throw AgentError.Unavailable($"{AppName} is not installed at {exe}. Run ./build.ps1");
        // UseShellExecute: the app gets this environment but none of this process's handles.
        // An inherited stdout handle would keep an agent's shell (or an MCP client) waiting until the app quits.
        try
        {
            Process.Start(new ProcessStartInfo(exe, "--agent") { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe)! });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw AgentError.Unavailable($"could not start {AppName}: {e.Message}");
        }
    }
}
