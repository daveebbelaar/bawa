using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace __APP_NAME__.Agent;

/// A small MCP server over stdin and stdout (JSON-RPC, one message per line).
/// Every AgentCommand becomes a tool ("items add" -> "items_add"), and each call goes to the
/// running app, like the CLI. Register it once:
///   claude mcp add --scope user __CLI_NAME__ -- %USERPROFILE%\.local\bin\__CLI_NAME__.exe mcp
/// Optional: Claude Code drives the CLI fine without it. Use it for Claude Desktop and other
/// clients that have no shell.
public static class AgentMcp
{
    public static async Task ServeAsync()
    {
        using var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
        var output = AgentCli.Out;
        while (await input.ReadLineAsync() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            JsonObject? message;
            try { message = JsonNode.Parse(line) as JsonObject; }
            catch (JsonException) { continue; }
            // Notifications have no id and get no answer.
            if (message?["id"] is not { } id) continue;
            var method = (string?)message["method"] ?? "";
            var parameters = message["params"] as JsonObject ?? [];
            var reply = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id.DeepClone() };
            switch (method)
            {
                case "initialize":
                    reply["result"] = new JsonObject
                    {
                        ["protocolVersion"] = (string?)parameters["protocolVersion"] ?? "2025-06-18",
                        ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                        ["serverInfo"] = new JsonObject { ["name"] = AgentPipe.CliName, ["version"] = "1.0" },
                        ["instructions"] = $"Tools that drive the {AgentPipe.AppName} app on this PC. Destructive tools only report what they would change unless yes is true.",
                    };
                    break;
                case "ping":
                    reply["result"] = new JsonObject();
                    break;
                case "tools/list":
                    reply["result"] = new JsonObject { ["tools"] = AgentJson.Array(AgentCommands.All.Select(c => (JsonNode)Tool(c))) };
                    break;
                case "tools/call":
                    reply["result"] = await CallAsync(parameters);
                    break;
                default:
                    reply["error"] = new JsonObject { ["code"] = -32601, ["message"] = $"unknown method {method}" };
                    break;
            }
            output.WriteLine(AgentJson.Text(reply));
            output.Flush();
        }
    }

    private static JsonObject Tool(AgentCommand command)
    {
        var schema = AgentCli.Schema(command);
        schema["name"] = command.Name.Replace(' ', '_');
        schema.Remove("readOnly");
        schema.Remove("destructive");
        schema["annotations"] = new JsonObject { ["readOnlyHint"] = command.ReadOnly, ["destructiveHint"] = command.Destructive };
        return schema;
    }

    private static async Task<JsonObject> CallAsync(JsonObject parameters)
    {
        var name = ((string?)parameters["name"] ?? "").Replace('_', ' ');
        try
        {
            if (!AgentCommands.All.Any(c => c.Name == name)) throw AgentError.Usage($"unknown tool {name}");
            var args = new JsonObject();
            foreach (var (key, value) in AgentJson.Strings(parameters["arguments"] as JsonObject)) args[key] = value;
            var reply = await AgentPipe.SendAsync(new JsonObject { ["command"] = name, ["args"] = args }, launch: true);
            var ok = (bool?)reply["ok"] == true;
            return Text(AgentJson.Text(ok ? reply["result"] : reply["error"]), isError: !ok);
        }
        catch (AgentError e)
        {
            return Text(AgentJson.Text(e.ToJson()), isError: true);
        }
        catch (Exception e)
        {
            return Text(e.Message, isError: true);
        }
    }

    private static JsonObject Text(string text, bool isError) => new()
    {
        ["content"] = AgentJson.Array([new JsonObject { ["type"] = "text", ["text"] = text }]),
        ["isError"] = isError,
    };
}
