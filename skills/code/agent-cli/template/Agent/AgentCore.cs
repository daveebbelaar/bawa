using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace __APP_NAME__.Agent;

// The agent interface: one list of commands (AgentCommands.cs) that the CLI, its help,
// and the MCP server all read. The app runs each one (AgentHandlers.cs) on the UI thread,
// through the same code as the buttons, so the window updates live.
// Shared: the app and the __APP_NAME__.Cli project both compile this file.

public enum AgentParamKind { String, Integer, Boolean }

/// One option of a command. Positional: given without its --name, right after the command (`items show 3f2a`).
public sealed record AgentParam(string Name, string Help, AgentParamKind Kind = AgentParamKind.String,
    bool Required = false, bool Positional = false)
{
    public string Type => Kind.ToString().ToLowerInvariant();
}

/// One thing an agent can do. Names are "<noun> <verb>": "items add".
public sealed record AgentCommand(string Name, string Summary, string Example)
{
    public AgentParam[] Params { get; init; } = [];

    /// Only reads. Agents may run it without asking.
    public bool ReadOnly { get; init; }

    /// Removes data or cannot be undone. Without --yes it only reports what it would do.
    public bool Destructive { get; init; }

    public string[] Words => Name.Split(' ');
}

/// The values a command was called with. Every value travels as text, like on a command line.
public sealed class AgentArgs(Dictionary<string, string> values)
{
    public string? Text(string name) => values.GetValueOrDefault(name);

    public string Required(string name) =>
        values.TryGetValue(name, out var value) && value.Length > 0 ? value : throw AgentError.Usage($"missing --{name}");

    public int Int(string name, int fallback)
    {
        if (!values.TryGetValue(name, out var raw)) return fallback;
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw AgentError.Usage($"--{name} must be a whole number, not \"{raw}\"");
    }

    public bool? Bool(string name)
    {
        if (!values.TryGetValue(name, out var raw)) return null;
        return raw.ToLowerInvariant() switch
        {
            "" or "true" or "yes" or "1" => true,
            "false" or "no" or "0" => false,
            _ => throw AgentError.Usage($"--{name} must be true or false, not \"{raw}\""),
        };
    }

    public bool Confirmed => values.TryGetValue("yes", out var raw) && raw.ToLowerInvariant() is "" or "true" or "yes" or "1";
}

/// An error with a stable code an agent can branch on, a message that says how to fix it,
/// and the exit code the CLI returns.
public sealed class AgentError(string code, string message, int exitCode) : Exception(message)
{
    public string Code { get; } = code;
    public int ExitCode { get; } = exitCode;

    public static AgentError Usage(string message) => new("usage", message, 2);
    public static AgentError NotFound(string message) => new("not_found", message, 3);
    public static AgentError Invalid(string message) => new("invalid", message, 1);
    public static AgentError Unavailable(string message) => new("app_unavailable", message, 4);

    /// The error part of a reply from the app: {"ok":false,"error":{"code":"...","message":"..."}}.
    public static AgentError FromReply(JsonObject reply)
    {
        var error = reply["error"] as JsonObject;
        var code = (string?)error?["code"] ?? "error";
        var exit = code switch { "usage" => 2, "not_found" => 3, "app_unavailable" => 4, _ => 1 };
        return new AgentError(code, (string?)error?["message"] ?? "unknown error", exit);
    }

    public JsonObject ToJson() => new()
    {
        ["ok"] = false,
        ["error"] = new JsonObject { ["code"] = Code, ["message"] = Message },
    };
}

public static class AgentJson
{
    /// Relaxed: + " ` and < stay readable instead of + and friends. The output is not for HTML.
    public static readonly JsonSerializerOptions Options = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// One line of JSON in plain ASCII: every non-ASCII character becomes \uXXXX, so the text
    /// reads right in every shell and code page (Windows PowerShell 5.1 included).
    public static string Text(JsonNode? node)
    {
        var json = node?.ToJsonString(Options) ?? "null";
        var ascii = new StringBuilder(json.Length);
        foreach (var c in json)
        {
            if (c < 128) ascii.Append(c);
            else ascii.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
        }
        return ascii.ToString();
    }

    /// ISO 8601 with the offset, like 2026-10-29T14:05:00+01:00.
    public static string Date(DateTimeOffset date) => date.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);

    /// A JSON value as command-line text: "milk" -> milk, true -> true, 10 -> 10.
    public static string? Value(JsonNode? node) => node switch
    {
        null => null,
        JsonValue value when value.GetValueKind() == JsonValueKind.String => value.GetValue<string>(),
        _ => node.ToJsonString(),
    };

    public static JsonArray Array(IEnumerable<JsonNode?> nodes)
    {
        var array = new JsonArray();
        foreach (var node in nodes) array.Add(node);
        return array;
    }

    /// {"title":"Milk","starred":true} -> title=Milk, starred=true.
    public static Dictionary<string, string> Strings(JsonObject? values)
    {
        var result = new Dictionary<string, string>();
        if (values is null) return result;
        foreach (var (key, value) in values)
        {
            if (Value(value) is { } text) result[key] = text;
        }
        return result;
    }
}
