using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;

namespace __APP_NAME__.Agent;

/// __CLI_NAME__.exe runs commands against the running __APP_NAME__ and exits.
///   __CLI_NAME__ help                         every command, with examples
///   __CLI_NAME__ items add --title "Milk"     run one command
///   __CLI_NAME__ items list --json            one JSON object on stdout
///   __CLI_NAME__ mcp                          an MCP server on stdin and stdout
///   __CLI_NAME__ version                      this CLI's build and the running app's
/// Exit codes: 0 ok, 1 error, 2 usage, 3 not found, 4 app not running.
public static class AgentCli
{
    private const string Cli = AgentPipe.CliName;

    /// Redirected (an agent, a script, an MCP client): UTF-8 bytes with \n line ends.
    /// A console window: Console.Out, which writes Unicode text to the console.
    public static readonly TextWriter Out = Console.IsOutputRedirected
        ? new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" }
        : Console.Out;

    public static async Task<int> RunAsync(string[] args)
    {
        var words = args.ToList();
        var json = words.RemoveAll(w => w == "--json") > 0;
        try
        {
            switch (words.FirstOrDefault())
            {
                case null or "help" or "--help" or "-h":
                    return Help([.. words.Skip(1)], json);
                case "version" or "--version":
                    return await VersionAsync(json);
                case "mcp":
                    await AgentMcp.ServeAsync();
                    return 0;
                default:
                    return await RunCommandAsync(words, json);
            }
        }
        catch (AgentError e)
        {
            Fail(e, json);
            return e.ExitCode;
        }
        catch (Exception e)
        {
            Fail(AgentError.Invalid(e.Message), json);
            return 1;
        }
    }

    private static async Task<int> RunCommandAsync(List<string> words, bool json)
    {
        // The longest command name that matches the first words: "items add".
        var command = AgentCommands.All
            .Where(c => words.Count >= c.Words.Length && c.Words.SequenceEqual(words.Take(c.Words.Length)))
            .MaxBy(c => c.Words.Length)
            ?? throw AgentError.Usage($"unknown command \"{string.Join(' ', words.Take(2))}\". Run `{Cli} help`");
        var rest = words.Skip(command.Words.Length).ToList();
        if (rest.Contains("--help") || rest.Contains("-h"))
        {
            Out.WriteLine(Usage(command));
            return 0;
        }
        var launch = rest.RemoveAll(w => w == "--no-launch") == 0;
        var values = Parse(rest, command);
        var args = new JsonObject();
        foreach (var (key, value) in values) args[key] = value;
        var reply = await AgentPipe.SendAsync(new JsonObject { ["command"] = command.Name, ["args"] = args }, launch);
        if ((bool?)reply["ok"] != true) throw AgentError.FromReply(reply);
        var result = reply["result"];
        Out.WriteLine(json ? AgentJson.Text(result) : Human(result));
        return 0;
    }

    /// --name value, --flag, and positional values, checked against the command's params.
    public static Dictionary<string, string> Parse(List<string> words, AgentCommand command)
    {
        var values = new Dictionary<string, string>();
        var positional = new Queue<AgentParam>(command.Params.Where(p => p.Positional));
        for (var i = 0; i < words.Count; i++)
        {
            var word = words[i];
            if (word.StartsWith("--", StringComparison.Ordinal))
            {
                var name = word[2..];
                if (name == "yes" && command.Destructive)
                {
                    values["yes"] = "true";
                    continue;
                }
                var param = command.Params.FirstOrDefault(p => p.Name == name)
                    ?? throw AgentError.Usage($"{command.Name} has no option --{name}. Run `{Cli} {command.Name} --help`");
                var next = i + 1 < words.Count ? words[i + 1] : null;
                if (param.Kind == AgentParamKind.Boolean && (next is null || next.ToLowerInvariant() is not ("true" or "false" or "yes" or "no")))
                {
                    values[name] = "true";
                    continue;
                }
                values[name] = next ?? throw AgentError.Usage($"--{name} needs a value. Example: {command.Example}");
                i++;
            }
            else
            {
                if (!positional.TryDequeue(out var param))
                    throw AgentError.Usage($"unexpected \"{word}\". Run `{Cli} {command.Name} --help`");
                values[param.Name] = word;
            }
        }
        foreach (var param in command.Params.Where(p => p.Required && !values.ContainsKey(p.Name)))
            throw AgentError.Usage($"missing {(param.Positional ? $"<{param.Name}>" : $"--{param.Name}")}. Example: {command.Example}");
        return values;
    }

    // Help

    private static int Help(string[] words, bool json)
    {
        if (json)
        {
            Out.WriteLine(AgentJson.Text(new JsonObject { ["commands"] = AgentJson.Array(AgentCommands.All.Select(c => (JsonNode)Schema(c))) }));
            return 0;
        }
        var name = string.Join(' ', words);
        if (AgentCommands.All.FirstOrDefault(c => c.Name == name) is { } command)
        {
            Out.WriteLine(Usage(command));
            return 0;
        }
        var width = AgentCommands.All.Max(c => c.Name.Length) + 2;
        var lines = new List<string> { $"{Cli}: drive {AgentPipe.AppName} from the shell. Every command takes --json.", "" };
        lines.AddRange(AgentCommands.All.Select(c => "  " + c.Name.PadRight(width) + c.Summary));
        lines.Add("");
        lines.Add($"More: {Cli} help <command>, {Cli} help --json (every command as a schema), {Cli} version, {Cli} mcp.");
        lines.Add("Exit codes: 0 ok, 1 error, 2 usage, 3 not found, 4 app not running.");
        Out.WriteLine(string.Join(Environment.NewLine, lines));
        return 0;
    }

    public static string Usage(AgentCommand command)
    {
        var shape = new StringBuilder($"  {Cli} {command.Name}");
        foreach (var param in command.Params)
        {
            var shown = param.Positional ? $"<{param.Name}>" : param.Kind == AgentParamKind.Boolean ? $"--{param.Name}" : $"--{param.Name} <{param.Type}>";
            shape.Append(' ').Append(param.Required ? shown : $"[{shown}]");
        }
        if (command.Destructive) shape.Append(" [--yes]");
        var lines = new List<string> { command.Summary, "", shape.ToString() };
        if (command.Params.Length > 0) lines.Add("");
        lines.AddRange(command.Params.Select(p => "  " + (p.Positional ? p.Name : "--" + p.Name).PadRight(14) + p.Help));
        if (command.Destructive) lines.Add("  --yes".PadRight(16) + "do it. Without --yes it only shows what it would change");
        lines.Add("");
        lines.Add($"Example: {command.Example}");
        return string.Join(Environment.NewLine, lines);
    }

    public static JsonObject Schema(AgentCommand command)
    {
        var properties = new JsonObject();
        foreach (var param in command.Params)
            properties[param.Name] = new JsonObject { ["type"] = param.Type, ["description"] = param.Help };
        if (command.Destructive)
            properties["yes"] = new JsonObject { ["type"] = "boolean", ["description"] = "Do it. Without yes it only shows what it would change." };
        return new JsonObject
        {
            ["name"] = command.Name,
            ["description"] = $"{command.Summary} Example: {command.Example}",
            ["readOnly"] = command.ReadOnly,
            ["destructive"] = command.Destructive,
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = AgentJson.Array(command.Params.Where(p => p.Required).Select(p => (JsonNode)p.Name)),
            },
        };
    }

    // Version

    /// The CLI's build, and the running app's when it runs. They differ after a build that
    /// could not close the app: restart it.
    private static async Task<int> VersionAsync(bool json)
    {
        var version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev";
        var reply = await AgentPipe.TryExchangeAsync(new JsonObject { ["command"] = "_app" }, connectMs: 300);
        var app = reply?["result"] as JsonObject;
        if (json)
        {
            var appJson = app is null ? new JsonObject { ["running"] = false } : new JsonObject
            {
                ["running"] = true,
                ["version"] = app["version"]?.DeepClone(),
                ["pid"] = app["pid"]?.DeepClone(),
            };
            Out.WriteLine(AgentJson.Text(new JsonObject { ["version"] = version, ["app"] = appJson }));
        }
        else
        {
            Out.WriteLine(app is null ? $"{Cli} {version} ({AgentPipe.AppName} is not running)" : $"{Cli} {version} ({AgentPipe.AppName} {app["version"]} is running)");
        }
        if (app is not null && (string?)app["version"] != version)
            Console.Error.WriteLine($"{Cli}: the running app is another build. Close it and run the command again");
        return 0;
    }

    // Output

    /// Short plain lines for people. Agents use --json.
    private static string Human(JsonNode? result)
    {
        if (result is JsonObject obj && obj.Select(p => p.Value).OfType<JsonArray>().FirstOrDefault(a => a.All(n => n is JsonObject)) is { } list)
            return list.Count == 0 ? "(none)" : string.Join(Environment.NewLine, list.Select(n => Line((JsonObject)n!)));
        return result is JsonObject one ? Line(one) : AgentJson.Value(result) ?? "";
    }

    private static string Line(JsonObject obj)
    {
        string[] first = ["id", "title", "name"];
        return string.Join("  ", obj
            .OrderBy(p => Array.IndexOf(first, p.Key) is var i && i >= 0 ? i : 9)
            .ThenBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => p.Value is JsonValue v && v.TryGetValue<bool>(out var flag)
                ? $"{p.Key}: {(flag ? "yes" : "no")}"
                : $"{p.Key}: {AgentJson.Value(p.Value)}"));
    }

    private static void Fail(AgentError error, bool json)
    {
        if (json) Out.WriteLine(AgentJson.Text(error.ToJson()));
        else Console.Error.WriteLine($"{Cli}: {error.Message}");
    }
}
