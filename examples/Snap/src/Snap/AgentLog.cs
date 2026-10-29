namespace Snap;

/// Output for agents: self-test results, snapshot paths, refusals.
/// A WinUI app has no dependable stdout: a redirected handle often stays empty, and a pipe
/// can keep the reader waiting after the app quits. So every line also goes to the file
/// in APP_LOG, flushed at once, and the tools in tools/ print that file.
public static class AgentLog
{
    private static readonly string? Path = Environment.GetEnvironmentVariable("APP_LOG");

    public static void Line(string text)
    {
        Console.WriteLine(text);
        if (string.IsNullOrEmpty(Path)) return;
        try { File.AppendAllText(Path, text + Environment.NewLine); }
        catch (IOException) { }
    }

    /// A debug line for the running app: only written when APP_LOG is set, so normal runs stay quiet.
    public static void Trace(string text)
    {
        if (!string.IsNullOrEmpty(Path)) Line(text);
    }

    /// Quits with an exit code, after the last line is on disk.
    public static void Exit(int code)
    {
        Console.Out.Flush();
        Environment.Exit(code);
    }
}
