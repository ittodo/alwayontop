namespace TrayAlwaysOnTop;

internal enum WinKeyReleaseAction
{
    PassThrough,
    Suppress,
    InjectTap
}

internal sealed class WinKeyGestureTracker
{
    private readonly Dictionary<uint, WinKeyPressState> _pressedKeys = [];

    public void Press(uint virtualKeyCode, long timestamp, bool combinedWithOtherModifier)
    {
        if (!_pressedKeys.ContainsKey(virtualKeyCode))
        {
            _pressedKeys.Add(
                virtualKeyCode,
                new WinKeyPressState(timestamp, combinedWithOtherModifier));
        }
    }

    public void MarkOtherModifierActivity()
    {
        foreach (var state in _pressedKeys.Values)
        {
            state.CombinedWithOtherKey = true;
        }
    }

    public void MarkLongPress()
    {
        foreach (var state in _pressedKeys.Values.Where(state => !state.DeliveredToWindows))
        {
            state.LongPress = true;
        }
    }

    public IReadOnlyList<uint> DeliverForShortcut()
    {
        var keysToDeliver = new List<uint>();
        foreach (var pair in _pressedKeys.Where(pair => !pair.Value.DeliveredToWindows))
        {
            pair.Value.DeliveredToWindows = true;
            pair.Value.CombinedWithOtherKey = true;
            keysToDeliver.Add(pair.Key);
        }

        return keysToDeliver;
    }

    public WinKeyReleaseAction Release(uint virtualKeyCode, long timestamp, int longPressMilliseconds)
    {
        if (!_pressedKeys.Remove(virtualKeyCode, out var state))
        {
            return WinKeyReleaseAction.PassThrough;
        }

        if (state.DeliveredToWindows)
        {
            return WinKeyReleaseAction.PassThrough;
        }

        var elapsed = Math.Max(0, timestamp - state.PressedAt);
        return !state.LongPress
            && !state.CombinedWithOtherKey
            && elapsed < longPressMilliseconds
                ? WinKeyReleaseAction.InjectTap
                : WinKeyReleaseAction.Suppress;
    }

    public IReadOnlyList<uint> ResetAndGetDeliveredKeys()
    {
        var deliveredKeys = _pressedKeys
            .Where(pair => pair.Value.DeliveredToWindows)
            .Select(pair => pair.Key)
            .ToArray();
        _pressedKeys.Clear();
        return deliveredKeys;
    }

    private sealed class WinKeyPressState(long pressedAt, bool combinedWithOtherKey)
    {
        public long PressedAt { get; } = pressedAt;

        public bool CombinedWithOtherKey { get; set; } = combinedWithOtherKey;

        public bool DeliveredToWindows { get; set; }

        public bool LongPress { get; set; }
    }
}
