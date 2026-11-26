namespace InterviewMaster.Support;

/// Where `print` and `NSLog` lines go. Silent until the app or a tool installs a sink.
public static class Log
{
    public static Action<string>? Sink { get; set; }

    public static void Print(string message) => Sink?.Invoke(message);
}
