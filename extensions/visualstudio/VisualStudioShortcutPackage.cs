using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;

namespace TrayAlwaysOnTop.VisualStudio;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[InstalledProductRegistration("Tray Always On Top", "현재 컨텍스트 단축키 연동", "1.2.0")]
[ProvideAutoLoad(VSConstants.UICONTEXT.ShellInitialized_string, PackageAutoLoadFlags.BackgroundLoad)]
[Guid(PackageGuidString)]
public sealed class VisualStudioShortcutPackage : AsyncPackage
{
    public const string PackageGuidString = "A7B2B10C-60DF-4E27-9898-7824C84C88F8";
    private const string PipeName = "TrayAlwaysOnTop.VisualStudio";
    private readonly CancellationTokenSource _shutdown = new();
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private Timer? _timer;
    private DTE2? _dte;
    private IReadOnlyList<BoundCommand> _commands = Array.Empty<BoundCommand>();
    private DateTime _commandsCachedAtUtc;
    private string? _lastPayload;
    private DateTime _lastSentAtUtc;

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        _dte = await GetServiceAsync(typeof(DTE)) as DTE2;
        if (_dte is null)
        {
            return;
        }

        _timer = new Timer(_ => QueueRefresh(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(1500));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _shutdown.Cancel();
            _timer?.Dispose();
            _refreshLock.Dispose();
            _shutdown.Dispose();
        }

        base.Dispose(disposing);
    }

    private void QueueRefresh()
    {
        if (_shutdown.IsCancellationRequested || _dte is null)
        {
            return;
        }

        JoinableTaskFactory.RunAsync(RefreshAsync).FileAndForget("TrayAlwaysOnTop/VisualStudioShortcutRefresh");
    }

    private async Task RefreshAsync()
    {
        if (!await _refreshLock.WaitAsync(0).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(_shutdown.Token);
            if (_dte is null)
            {
                return;
            }

            if (_commands.Count == 0 || DateTime.UtcNow - _commandsCachedAtUtc > TimeSpan.FromSeconds(30))
            {
                _commands = ReadBoundCommands(_dte);
                _commandsCachedAtUtc = DateTime.UtcNow;
            }

            var shortcuts = new List<ShortcutItem>();
            foreach (var entry in _commands)
            {
                try
                {
                    if (!entry.Command.IsAvailable)
                    {
                        continue;
                    }

                    shortcuts.AddRange(entry.Bindings.Select(binding => new ShortcutItem
                    {
                        Key = binding.Key,
                        RemainingChord = binding.RemainingChord,
                        Command = entry.CommandName,
                        Title = entry.Title,
                        Scope = binding.Scope
                    }));
                }
                catch (COMException)
                {
                    // Commands can disappear when packages unload. The next cache refresh repairs the list.
                }
            }

            var processId = System.Diagnostics.Process.GetCurrentProcess().Id;
            var message = new ShortcutMessage
            {
                ProtocolVersion = 1,
                App = "visualstudio",
                VisualStudioVersion = _dte.Version ?? string.Empty,
                ProcessId = processId,
                WindowActive = GetForegroundProcessId() == processId,
                Context = GetContext(_dte),
                Shortcuts = shortcuts
                    .GroupBy(item => (item.Key, item.RemainingChord, item.Command, item.Scope))
                    .Select(group => group.First())
                    .ToList()
            };
            var payload = Serialize(message);
            if (string.Equals(payload, _lastPayload, StringComparison.Ordinal)
                && DateTime.UtcNow - _lastSentAtUtc < TimeSpan.FromSeconds(5))
            {
                return;
            }

            if (await SendAsync(payload, _shutdown.Token).ConfigureAwait(false))
            {
                _lastPayload = payload;
                _lastSentAtUtc = DateTime.UtcNow;
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            // Visual Studio is shutting down.
        }
        catch (COMException)
        {
            _commands = Array.Empty<BoundCommand>();
        }
        catch (Exception exception)
        {
            ActivityLog.TryLogError(nameof(VisualStudioShortcutPackage), exception.ToString());
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private static IReadOnlyList<BoundCommand> ReadBoundCommands(DTE2 dte)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var result = new List<BoundCommand>();
        foreach (Command command in dte.Commands)
        {
            try
            {
                var bindings = ReadBindings(command.Bindings).ToArray();
                if (bindings.Length == 0)
                {
                    continue;
                }

                var name = command.Name ?? string.Empty;
                var title = string.IsNullOrWhiteSpace(command.LocalizedName) ? name : command.LocalizedName;
                result.Add(new BoundCommand(command, name, title, bindings));
            }
            catch (COMException)
            {
                // Some lazy-loaded commands do not expose metadata yet.
            }
        }

        return result;
    }

    private static IEnumerable<ParsedBinding> ReadBindings(object bindingsObject)
    {
        if (!(bindingsObject is IEnumerable bindings))
        {
            yield break;
        }

        foreach (var value in bindings)
        {
            if (value is string binding && TryParseBinding(binding, out var parsed))
            {
                yield return parsed;
            }
        }
    }

    internal static bool TryParseBinding(string binding, out ParsedBinding parsed)
    {
        parsed = new ParsedBinding(string.Empty, string.Empty, null);
        if (string.IsNullOrWhiteSpace(binding))
        {
            return false;
        }

        var separator = binding.IndexOf("::", StringComparison.Ordinal);
        var scope = separator >= 0 ? binding.Substring(0, separator).Trim() : "Global";
        var gesture = separator >= 0 ? binding.Substring(separator + 2).Trim() : binding.Trim();
        var chords = gesture.Split(new[] { ',' }, 2, StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeGesture)
            .Where(part => part.Length > 0)
            .ToArray();
        if (chords.Length == 0)
        {
            return false;
        }

        parsed = new ParsedBinding(scope, chords[0], chords.Length > 1 ? chords[1] : null);
        return true;
    }

    private static string NormalizeGesture(string gesture)
    {
        var trimmed = gesture.Trim();
        var trailingPlus = trimmed.EndsWith("+", StringComparison.Ordinal);
        if (trailingPlus)
        {
            trimmed = trimmed.Substring(0, trimmed.Length - 1);
        }

        var parts = trimmed.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => NormalizeKeyPart(part.Trim().ToLowerInvariant()))
            .ToList();
        if (trailingPlus)
        {
            parts.Add("plus");
        }

        return string.Join("+", parts);
    }

    private static string NormalizeKeyPart(string part)
    {
        switch (part)
        {
            case "control": return "ctrl";
            case "left arrow": return "left";
            case "right arrow": return "right";
            case "up arrow": return "up";
            case "down arrow": return "down";
            case "page up": return "pageup";
            case "page down": return "pagedown";
            case "break": return "pause";
            default: return part.Replace(" ", string.Empty);
        }
    }

    private static string GetContext(DTE2 dte)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        string? language = null;
        string? windowKind = null;
        string? debugMode = null;
        try { language = dte.ActiveDocument?.Language; } catch (COMException) { }
        try { windowKind = dte.ActiveWindow?.Kind; } catch (COMException) { }
        try { debugMode = dte.Debugger?.CurrentMode.ToString(); } catch (COMException) { }
        return string.Join(" · ", new[] { language, windowKind, debugMode }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string Serialize(ShortcutMessage message)
    {
        var serializer = new DataContractJsonSerializer(typeof(ShortcutMessage));
        using (var stream = new MemoryStream())
        {
            serializer.WriteObject(stream, message);
            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }

    private static async Task<bool> SendAsync(string payload, CancellationToken cancellationToken)
    {
        try
        {
            using (var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.Asynchronous))
            {
                await pipe.ConnectAsync(250, cancellationToken).ConfigureAwait(false);
                using (var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true))
                {
                    await writer.WriteLineAsync(payload).ConfigureAwait(false);
                    await writer.FlushAsync().ConfigureAwait(false);
                }
            }
            return true;
        }
        catch (TimeoutException) { return false; }
        catch (IOException) { return false; }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    private static int GetForegroundProcessId()
    {
        var window = GetForegroundWindow();
        GetWindowThreadProcessId(window, out var processId);
        return unchecked((int)processId);
    }

    private sealed class BoundCommand
    {
        public BoundCommand(Command command, string commandName, string title, IReadOnlyList<ParsedBinding> bindings)
        {
            Command = command;
            CommandName = commandName;
            Title = title;
            Bindings = bindings;
        }

        public Command Command { get; }
        public string CommandName { get; }
        public string Title { get; }
        public IReadOnlyList<ParsedBinding> Bindings { get; }
    }

    internal sealed class ParsedBinding
    {
        public ParsedBinding(string scope, string key, string? remainingChord)
        {
            Scope = scope;
            Key = key;
            RemainingChord = remainingChord;
        }

        public string Scope { get; }
        public string Key { get; }
        public string? RemainingChord { get; }
    }

    [DataContract]
    private sealed class ShortcutMessage
    {
        [DataMember(Name = "protocolVersion")] public int ProtocolVersion { get; set; }
        [DataMember(Name = "app")] public string App { get; set; } = string.Empty;
        [DataMember(Name = "visualStudioVersion")] public string VisualStudioVersion { get; set; } = string.Empty;
        [DataMember(Name = "processId")] public int ProcessId { get; set; }
        [DataMember(Name = "windowActive")] public bool WindowActive { get; set; }
        [DataMember(Name = "context")] public string Context { get; set; } = string.Empty;
        [DataMember(Name = "shortcuts")] public List<ShortcutItem> Shortcuts { get; set; } = new List<ShortcutItem>();
    }

    [DataContract]
    private sealed class ShortcutItem
    {
        [DataMember(Name = "key")] public string Key { get; set; } = string.Empty;
        [DataMember(Name = "remainingChord", EmitDefaultValue = false)] public string? RemainingChord { get; set; }
        [DataMember(Name = "command")] public string Command { get; set; } = string.Empty;
        [DataMember(Name = "title")] public string Title { get; set; } = string.Empty;
        [DataMember(Name = "scope")] public string Scope { get; set; } = string.Empty;
    }
}
