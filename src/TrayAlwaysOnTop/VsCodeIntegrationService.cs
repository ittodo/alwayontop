using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;

namespace TrayAlwaysOnTop;

internal sealed class VsCodeIntegrationService : IDisposable
{
    private const string DefaultPipeName = "TrayAlwaysOnTop.VSCode";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _sync = new();
    private readonly string _pipeName;
    private VsCodeShortcutMessage? _latest;
    private VsCodeShortcutMessage? _lastActive;
    private DateTime _receivedAtUtc;
    private DateTime _lastActiveReceivedAtUtc;
    private bool _disposed;

    public VsCodeIntegrationService(string pipeName = DefaultPipeName)
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
                return _latest is not null && DateTime.UtcNow - _receivedAtUtc < TimeSpan.FromSeconds(12);
            }
        }
    }

    public string StatusText => IsConnected
        ? "VS Code 연동됨"
        : "VS Code 연동 확장의 연결을 기다리는 중";

    internal int ReceivedShortcutCount
    {
        get
        {
            lock (_sync)
            {
                return _latest?.Shortcuts.Count ?? 0;
            }
        }
    }

    public IReadOnlyList<ContextualShortcut> GetForegroundShortcuts()
    {
        if (!IsVsCodeForeground())
        {
            return [];
        }

        VsCodeShortcutMessage? snapshot;
        lock (_sync)
        {
            snapshot = _lastActive;
            if (snapshot is null
                || DateTime.UtcNow - _lastActiveReceivedAtUtc >= TimeSpan.FromSeconds(12))
            {
                return [];
            }
        }

        return ConvertShortcuts(snapshot);
    }

    public IReadOnlyList<ContextualShortcut> GetLastActiveShortcuts()
    {
        VsCodeShortcutMessage? snapshot;
        lock (_sync)
        {
            snapshot = _lastActive;
            if (snapshot is null
                || DateTime.UtcNow - _lastActiveReceivedAtUtc >= TimeSpan.FromMinutes(2))
            {
                return [];
            }
        }

        return ConvertShortcuts(snapshot);
    }

    private static IReadOnlyList<ContextualShortcut> ConvertShortcuts(VsCodeShortcutMessage snapshot) =>
        snapshot.Shortcuts
            .Select(Convert)
            .Where(shortcut => shortcut is not null)
            .Cast<ContextualShortcut>()
            .DistinctBy(shortcut => (shortcut.Modifiers, shortcut.Key, shortcut.Description))
            .ToArray();

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
            catch (JsonException)
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

                    var message = JsonSerializer.Deserialize<VsCodeShortcutMessage>(line, JsonOptions);
                    if (message is null || message.ProtocolVersion != 1 || message.App != "vscode")
                    {
                        continue;
                    }

                    lock (_sync)
                    {
                        _latest = message;
                        _receivedAtUtc = DateTime.UtcNow;
                        if (message.WindowActive)
                        {
                            _lastActive = message;
                            _lastActiveReceivedAtUtc = _receivedAtUtc;
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Application shutdown.
            }
            catch (IOException)
            {
                // VS Code closed or reloaded its extension host.
            }
            catch (JsonException)
            {
                // Ignore malformed data and let the extension reconnect.
            }
        }
    }

    private static async Task DelayBeforeRetryAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(300, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested.
        }
    }

    private static ContextualShortcut? Convert(VsCodeShortcutItem item)
    {
        if (!VsCodeKeyGestureParser.TryParse(item.Key, out var modifiers, out var key, out var remaining))
        {
            return null;
        }

        var shortcut = HotKeyFormatter.Format(modifiers, key);
        var description = string.IsNullOrWhiteSpace(remaining)
            ? item.Title
            : $"{item.Title}  ·  다음 키: {FormatRemainingChord(remaining)}";
        return new ContextualShortcut(
            modifiers,
            key,
            shortcut,
            description,
            ShortcutVisualKind.VsCode,
            "VS Code",
            string.IsNullOrWhiteSpace(remaining) ? null : remaining);
    }

    private static string FormatRemainingChord(string chord)
    {
        return VsCodeKeyGestureParser.TryParse(chord, out var modifiers, out var key, out _)
            ? HotKeyFormatter.Format(modifiers, key)
            : chord;
    }

    private static bool IsVsCodeForeground()
    {
        var window = NativeMethods.GetForegroundWindow();
        if (window == nint.Zero)
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(window, out var processId);
        try
        {
            return string.Equals(
                Process.GetProcessById((int)processId).ProcessName,
                "Code",
                StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
