namespace __APP_NAME__.Agent;

/// Everything an agent can do in __APP_NAME__. Add a command here and the CLI, `help`, and the
/// MCP tools all get it. Then add what it does to AgentHandlers.cs, under the same name.
/// Shared: the app and the __APP_NAME__.Cli project both compile this file, so it holds
/// only names, options, and help text. No app types.
public static class AgentCommands
{
    public static readonly AgentCommand[] All =
    [
        new("status", "Show whether the app runs, the open page, the pages, and item counts.",
            "__CLI_NAME__ status --json") { ReadOnly = true },

        new("items list", "List items, newest first.", "__CLI_NAME__ items list --starred --limit 10 --json")
        {
            ReadOnly = true,
            Params =
            [
                new("starred", "only starred items", AgentParamKind.Boolean),
                new("search", "only items whose title or notes contain this text"),
                new("limit", "at most this many (default 50)", AgentParamKind.Integer),
                new("offset", "skip this many, for the next page", AgentParamKind.Integer),
            ],
        },

        new("items show", "Show one item.", "__CLI_NAME__ items show 3f2a9c1e --json")
        {
            ReadOnly = true,
            Params = [new("id", "the item's id (the first 8 characters are enough)", Required: true, Positional: true)],
        },

        new("items add", "Add an item and select it in the window.", "__CLI_NAME__ items add --title \"Buy milk\" --starred --json")
        {
            Params =
            [
                new("title", "the title", Required: true),
                new("notes", "notes under the title"),
                new("starred", "star it", AgentParamKind.Boolean),
            ],
        },

        new("items update", "Change an item. Only the options you give change.", "__CLI_NAME__ items update 3f2a9c1e --starred false --json")
        {
            Params =
            [
                new("id", "the item's id", Required: true, Positional: true),
                new("title", "new title"),
                new("notes", "new notes"),
                new("starred", "true or false", AgentParamKind.Boolean),
            ],
        },

        new("items delete", "Delete an item.", "__CLI_NAME__ items delete 3f2a9c1e --yes --json")
        {
            Destructive = true,
            Params = [new("id", "the item's id", Required: true, Positional: true)],
        },

        new("open", "Show a page in the window.", "__CLI_NAME__ open starred")
        {
            Params = [new("page", "a page id, like all or settings. `__CLI_NAME__ status` lists them", Required: true, Positional: true)],
        },
    ];
}
