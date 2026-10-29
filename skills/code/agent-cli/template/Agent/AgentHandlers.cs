using System.Text.Json.Nodes;
using __APP_NAME__.Core;

namespace __APP_NAME__.Agent;

/// What each command in AgentCommands.cs does. Runs inside the app on the UI thread, with the
/// app's model, like a button in the window. Call the function the button calls; never copy
/// its logic. App only (the CLI project does not compile it).
public static class AgentHandlers
{
    public delegate JsonNode Handler(AgentArgs args, AppModel model, MainWindow window);

    public static readonly Dictionary<string, Handler> All = new()
    {
        ["status"] = (_, model, _) => new JsonObject
        {
            ["running"] = true,
            ["page"] = model.Page.Id(),
            ["pages"] = AgentJson.Array(Pages.Select(p => (JsonNode)p)),
            ["items"] = model.Items.Count,
            ["starred"] = model.Items.Count(i => i.IsStarred),
            ["selected"] = model.Selection?.ShortId(),
        },

        ["items list"] = (args, model, _) =>
        {
            IEnumerable<Item> items = model.Items;
            if (args.Bool("starred") == true) items = items.Where(i => i.IsStarred);
            if (args.Text("search") is { } text)
            {
                items = items.Where(i => i.Title.Contains(text, StringComparison.CurrentCultureIgnoreCase)
                    || i.Notes.Contains(text, StringComparison.CurrentCultureIgnoreCase));
            }
            var all = items.ToList();
            var limit = args.Int("limit", 50);
            var offset = args.Int("offset", 0);
            var page = all.Skip(offset).Take(limit).ToList();
            var result = new JsonObject
            {
                ["items"] = AgentJson.Array(page.Select(i => (JsonNode)i.ToAgentJson())),
                ["total"] = all.Count,
            };
            if (offset + page.Count < all.Count) result["nextOffset"] = offset + page.Count;
            return result;
        },

        ["items show"] = (args, model, _) => Find(args.Required("id"), model).ToAgentJson(),

        ["items add"] = (args, model, _) =>
        {
            var title = ItemTitle.Clean(args.Required("title"));
            if (title.Length == 0) throw AgentError.Invalid("--title needs some text. Example: __CLI_NAME__ items add --title \"Buy milk\"");
            var item = model.AddItem(title);          // the New item button: saves, shows, selects
            if (args.Text("notes") is { } notes) item.Notes = notes;
            if (args.Bool("starred") is { } starred && starred != item.IsStarred)
            {
                item.IsStarred = starred;
                model.Refresh();                       // the Starred page shows or hides it
            }
            return item.ToAgentJson();
        },

        ["items update"] = (args, model, _) =>
        {
            var item = Find(args.Required("id"), model);
            if (args.Text("title") is { } raw)
            {
                var title = ItemTitle.Clean(raw);
                if (title.Length == 0) throw AgentError.Invalid("--title cannot be empty");
                item.Title = title;
            }
            if (args.Text("notes") is { } notes) item.Notes = notes;
            if (args.Bool("starred") is { } starred && starred != item.IsStarred)
            {
                item.IsStarred = starred;
                model.Refresh();                       // the Starred page shows or hides it
            }
            return item.ToAgentJson();
        },

        ["items delete"] = (args, model, _) =>
        {
            var item = Find(args.Required("id"), model);
            var shown = item.ToAgentJson();
            if (!args.Confirmed) return new JsonObject { ["deleted"] = false, ["wouldDelete"] = shown, ["hint"] = "add --yes to delete it" };
            model.Delete(item);                        // the Delete button
            return new JsonObject { ["deleted"] = true, ["item"] = shown };
        },

        ["open"] = (args, _, window) =>
        {
            var id = args.Required("page");
            if (!Pages.Contains(id)) throw AgentError.NotFound($"no page \"{id}\". Pages: {string.Join(", ", Pages)}");
            window.Show(id);
            return new JsonObject { ["page"] = id };
        },
    };

    /// Every page `open` accepts: the navigation pages, then Settings.
    private static IEnumerable<string> Pages => Enum.GetValues<SidebarPage>().Select(p => p.Id()).Append("settings");

    /// An item by its id, or by any unique start of it.
    private static Item Find(string id, AppModel model)
    {
        var start = id.ToLowerInvariant();
        var matches = model.Items.Where(i => i.AgentId().StartsWith(start, StringComparison.Ordinal)).ToList();
        return matches.Count switch
        {
            0 => throw AgentError.NotFound($"no item with id {id}. Run `__CLI_NAME__ items list`"),
            1 => matches[0],
            _ => throw AgentError.Usage($"{matches.Count} items start with {id}. Give more characters"),
        };
    }

    /// A stable id for agents: the Guid in lowercase, without dashes.
    private static string AgentId(this Item item) => item.Id.ToString("N");

    private static string ShortId(this Item item) => item.AgentId()[..8];

    private static JsonObject ToAgentJson(this Item item) => new()
    {
        ["id"] = item.ShortId(),
        ["title"] = item.Title,
        ["notes"] = item.Notes,
        ["starred"] = item.IsStarred,
        ["createdAt"] = AgentJson.Date(item.CreatedAt),
    };
}
