namespace Samitivity.Core;

/// <summary>Allowlisted reasons for safe local diagnostics; never serialize exception messages or paths.</summary>
public static class ImportFailure
{
    private static readonly Dictionary<string, string> Reasons = new()
    {
        ["local-source-required"] = "Copy the recording to a local disk or USB drive before importing.",
        ["extension"] = "Choose an M4A, MP3 or WAV file.",
        ["file-size"] = "The recording must be nonempty and at most 256 MiB.",
        ["audio-format"] = "The decoded sample rate or channel layout is unsupported.",
        ["format-change"] = "The recording changes audio format mid-stream, which is unsupported.",
        ["duration"] = "The recording must contain audio and be no longer than two hours.",
        ["empty-decode"] = "The Windows decoder returned no audio. This does not prove the original file is damaged."
    };
    public static InvalidDataException Reject(string reason)
    {
        var exception = new InvalidDataException(Reasons[reason]);
        exception.Data["SamitivityReason"] = reason;
        return exception;
    }
    public static string Reason(Exception exception) => exception.Data["SamitivityReason"] is string key && Reasons.ContainsKey(key) ? key : "native-or-io";
    public static string Describe(Exception exception)
    {
        var reason = Reason(exception);
        var message = Reasons.TryGetValue(reason, out var known) ? known :
            exception is DllNotFoundException or EntryPointNotFoundException ? "Required Windows media components are unavailable on this system." :
            "Windows could not read or decode this recording. Check the local file and collect the diagnostic log; this may be a codec or environment issue.";
        return $"{message} [{reason}; {exception.GetType().Name}; 0x{exception.HResult:X8}]";
    }
}
