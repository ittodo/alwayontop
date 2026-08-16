namespace TrayAlwaysOnTop;

internal static class HotKeyFormatter
{
    public static string Format(HotKeyModifiers modifiers, Keys key)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(HotKeyModifiers.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(HotKeyModifiers.Win)) parts.Add("Win");
        if (modifiers.HasFlag(HotKeyModifiers.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(HotKeyModifiers.Shift)) parts.Add("Shift");
        parts.Add(FormatKey(key));
        return string.Join(" + ", parts);
    }

    private static string FormatKey(Keys key)
    {
        if (key >= Keys.D0 && key <= Keys.D9)
        {
            return ((int)key - (int)Keys.D0).ToString();
        }

        return key switch
        {
            Keys.Return => "Enter",
            Keys.Next => "PageDown",
            Keys.Prior => "PageUp",
            Keys.Snapshot => "PrintScreen",
            _ => key.ToString()
        };
    }
}
