using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace __APP_NAME__.Agent;

/// The app side of the agent CLI: listens on the pipe and runs each request on the UI thread,
/// where the window's buttons run. App only (the CLI project does not compile it).
/// App.xaml.cs calls Activate and Start in OnLaunched.
public static class AgentHost
{
    /// The CLI starts the app with --agent when it is not running.
    public static bool StartedByCli => Environment.GetCommandLineArgs().Contains("--agent");

    /// Started by the CLI: the window shows without taking the focus, so the agent's terminal
    /// keeps it. Otherwise the normal Activate. Never minimize here: a WinUI window minimized
    /// before its first frame crashed the template app (COMException 0x80040111).
    public static void Activate(Window window)
    {
        if (StartedByCli) window.AppWindow.Show(activateWindow: false);
        else window.Activate();
    }

    public static void Start(AppModel model, MainWindow window)
    {
        var missing = AgentCommands.All.Select(c => c.Name).Where(n => !AgentHandlers.All.ContainsKey(n)).ToList();
        if (missing.Count > 0) AgentLog.Line($"AGENT no handler in AgentHandlers.cs for: {string.Join(", ", missing)}");
        var queue = window.DispatcherQueue;
        _ = Task.Run(async () =>
        {
            try
            {
                await AgentPipe.ListenAsync(request => OnUiThread(queue, () => Handle(request, model, window)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                AgentLog.Line($"AGENT not listening on {AgentPipe.Name}: another copy of the app owns it ({e.Message})");
            }
        });
    }

    private static Task<string> OnUiThread(DispatcherQueue queue, Func<string> work)
    {
        var done = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var queued = queue.TryEnqueue(() =>
        {
            try { done.SetResult(work()); }
            catch (Exception e) { done.SetResult(AgentError.Invalid(e.Message).ToJson().ToJsonString(AgentJson.Options)); }
        });
        if (!queued) done.SetResult(AgentError.Unavailable("the app is closing").ToJson().ToJsonString(AgentJson.Options));
        return done.Task;
    }

    /// A request is {"command":"items add","args":{"title":"Milk"}}.
    private static string Handle(string request, AppModel model, MainWindow window)
    {
        JsonObject reply;
        try
        {
            var message = JsonNode.Parse(request) as JsonObject ?? throw AgentError.Usage("bad request");
            var name = (string?)message["command"] ?? throw AgentError.Usage("bad request: no command");
            JsonNode result;
            if (name == "_app")
            {
                // Built in, for `__CLI_NAME__ version` and check-agent-cli.ps1.
                result = new JsonObject { ["pid"] = Environment.ProcessId, ["version"] = Version, ["pipe"] = AgentPipe.Name };
            }
            else
            {
                if (!AgentCommands.All.Any(c => c.Name == name))
                    throw AgentError.Usage($"unknown command \"{name}\". Run `{AgentPipe.CliName} help`");
                if (!AgentHandlers.All.TryGetValue(name, out var run))
                    throw AgentError.Invalid($"the app has no handler for \"{name}\". Add one to Agent/AgentHandlers.cs");
                result = run(new AgentArgs(AgentJson.Strings(message["args"] as JsonObject)), model, window);
            }
            reply = new JsonObject { ["ok"] = true, ["result"] = result };
        }
        catch (AgentError e)
        {
            reply = e.ToJson();
        }
        catch (Exception e)
        {
            reply = AgentError.Invalid(e.Message).ToJson();
        }
        return reply.ToJsonString(AgentJson.Options);
    }

    public static string Version =>
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev";
}
