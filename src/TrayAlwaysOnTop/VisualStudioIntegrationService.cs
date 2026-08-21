using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;

namespace TrayAlwaysOnTop;

internal sealed class VisualStudioIntegrationService : IDisposable
{
    private const string PipeName = "TrayAlwaysOnTop.VisualStudio";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly CancellationTokenSource _shutdown = new();
    private readonly string _pipeName;
    private readonly object _sync = new();
    private readonly Dictionary<int, ReceivedMessage> _messages = [];
    private ReceivedMessage? _lastActive;
    private bool _disposed;

    public VisualStudioIntegrationService(string pipeName = PipeName)
    {
        _pipeName = pipeName;
        _ = Task.Run(() => RunServerAsync(_shutdown.Token));
    }

    public bool IsConnected
    {
        get
        {
            lock (_sync)
            {
                RemoveExpiredMessages();
                return _messages.Count > 0;
            }
        }
    }

    public string StatusText
    {
        get
        {
            lock (_sync)
            {
                RemoveExpiredMessages();
                if (_messages.Count == 0)
                {
                    return "Visual Studio 연동 확장의 연결을 기다리는 중";
                }

                var versions = _messages.Values
                    .Select(message => FormatVersion(message.Message.VisualStudioVersion))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(version => version)
                    .ToArray();
                var latest = _messages.Values.OrderByDescending(message => message.ReceivedAtUtc).First();
                var context = string.IsNullOrWhiteSpace(latest.Message.Context)
                    ? string.Empty
                    : $" · {latest.Message.Context}";
                return $"Visual Studio {string.Join(", ", versions)} 연동됨 · PID {latest.Message.ProcessId}{context} · 마지막 수신 {latest.ReceivedAtUtc.ToLocalTime():HH:mm:ss}";
            }
        }
    }

    internal int ReceivedShortcutCount
    {
        get
        {
            lock (_sync)
            {
                return _messages.Values.Sum(message => message.Message.Shortcuts.Count);
            }
        }
    }

    public IReadOnlyList<ContextualShortcut> GetForegroundShortcuts()
    {
        var foregroundProcessId = GetForegroundVisualStudioProcessId();
        if (foregroundProcessId == 0)
        {
            return [];
        }

        lock (_sync)
        {
            RemoveExpiredMessages();
            return _messages.TryGetValue(foregroundProcessId, out var message)
                ? ConvertShortcuts(message.Message)
                : [];
        }
    }

    public IReadOnlyList<ContextualShortcut> GetLastActiveShortcuts()
    {
        lock (_sync)
        {
            if (_lastActive is null || DateTime.UtcNow - _lastActive.ReceivedAtUtc >= TimeSpan.FromMinutes(2))
            {
                return [];
            }

            return ConvertShortcuts(_lastActive.Message);
        }
    }

    public IReadOnlyList<string> GetConnectedVersions()
    {
        lock (_sync)
        {
            RemoveExpiredMessages();
            return _messages.Values
                .Select(message => FormatVersion(message.Message.VisualStudioVersion))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(version => version)
                .ToArray();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _shutdown.Cancel();
        _shutdown.Dispose();
    }

    private async Task RunServerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var pipe = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.In,
                    8,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellationToken);
                _ = HandleClientAsync(pipe, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (IOException)
            {
                await DelayBeforeRetryAsync(cancellationToken);
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        await using (pipe)
        using (var reader = new StreamReader(pipe))
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested && pipe.IsConnected)
                {
                    var line = await reader.ReadLineAsync(cancellationToken);
                    if (line is null)
                    {
                        break;
                    }

                    var message = JsonSerializer.Deserialize<VisualStudioShortcutMessage>(line, JsonOptions);
                    if (message is null
                        || message.ProtocolVersion != 1
                        || !string.Equals(message.App, "visualstudio", StringComparison.OrdinalIgnoreCase)
                        || message.ProcessId <= 0)
                    {
                        continue;
                    }

                    var received = new ReceivedMessage(message, DateTime.UtcNow);
                    lock (_sync)
                    {
                        _messages[message.ProcessId] = received;
                        if (message.WindowActive)
                        {
                            _lastActive = received;
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (IOException) { }
            catch (JsonException) { }
        }
    }

    private static IReadOnlyList<ContextualShortcut> ConvertShortcuts(VisualStudioShortcutMessage snapshot)
    {
        var source = $"Visual Studio {FormatVersion(snapshot.VisualStudioVersion)}";
        return snapshot.Shortcuts
            .Select(item => Convert(item, source))
            .Where(shortcut => shortcut is not null)
            .Cast<ContextualShortcut>()
            .DistinctBy(shortcut => (shortcut.Modifiers, shortcut.Key, shortcut.RemainingChord, shortcut.Description))
            .ToArray();
    }

    private static ContextualShortcut? Convert(VisualStudioShortcutItem item, string source)
    {
        if (!VsCodeKeyGestureParser.TryParse(item.Key, out var modifiers, out var key, out _))
        {
            return null;
        }

        var remaining = string.IsNullOrWhiteSpace(item.RemainingChord) ? null : item.RemainingChord;
        var scope = string.IsNullOrWhiteSpace(item.Scope)
            || string.Equals(item.Scope, "Global", StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : $" · {item.Scope}";
        var description = remaining is null
            ? item.Title + scope
            : $"{item.Title}{scope} · 다음 키: {FormatRemainingChord(remaining)}";
        return new ContextualShortcut(
            modifiers,
            key,
            HotKeyFormatter.Format(modifiers, key),
            description,
            ShortcutVisualKind.VisualStudio,
            source,
            remaining);
    }

    private static string FormatRemainingChord(string chord) =>
        VsCodeKeyGestureParser.TryParse(chord, out var modifiers, out var key, out _)
            ? HotKeyFormatter.Format(modifiers, key)
            : chord;

    private void RemoveExpiredMessages()
    {
        var cutoff = DateTime.UtcNow - TimeSpan.FromSeconds(12);
        foreach (var processId in _messages
                     .Where(pair => pair.Value.ReceivedAtUtc < cutoff)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            _messages.Remove(processId);
        }
    }

    private static int GetForegroundVisualStudioProcessId()
    {
        var window = NativeMethods.GetForegroundWindow();
        if (window == nint.Zero)
        {
            return 0;
        }

        NativeMethods.GetWindowThreadProcessId(window, out var processId);
        try
        {
            return string.Equals(Process.GetProcessById((int)processId).ProcessName, "devenv", StringComparison.OrdinalIgnoreCase)
                ? (int)processId
                : 0;
        }
        catch (ArgumentException) { return 0; }
        catch (InvalidOperationException) { return 0; }
    }

    private static string FormatVersion(string version)
    {
        if (Version.TryParse(version, out var parsed))
        {
            return parsed.Major switch
            {
                17 => "2022",
                18 => "2026",
                _ => parsed.Major.ToString()
            };
        }

        return version;
    }

    private static async Task DelayBeforeRetryAsync(CancellationToken cancellationToken)
    {
        try { await Task.Delay(300, cancellationToken); }
        catch (OperationCanceledException) { }
    }

    private sealed record ReceivedMessage(VisualStudioShortcutMessage Message, DateTime ReceivedAtUtc);
}
