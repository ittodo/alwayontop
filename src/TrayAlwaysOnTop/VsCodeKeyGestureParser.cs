namespace TrayAlwaysOnTop;

internal static class VsCodeKeyGestureParser
{
    public static bool TryParse(
        string gesture,
        out HotKeyModifiers modifiers,
        out Keys key,
        out string remainingChord)
    {
        modifiers = HotKeyModifiers.None;
        key = Keys.None;
        remainingChord = string.Empty;
        if (string.IsNullOrWhiteSpace(gesture))
        {
            return false;
        }

        var chords = gesture.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var parts = chords[0].Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var rawPart in parts)
        {
            var part = rawPart.ToLowerInvariant();
            switch (part)
            {
                case "ctrl":
                case "control":
                    modifiers |= HotKeyModifiers.Control;
                    break;
                case "shift":
                    modifiers |= HotKeyModifiers.Shift;
                    break;
                case "alt":
                    modifiers |= HotKeyModifiers.Alt;
                    break;
                case "win":
                case "meta":
                    modifiers |= HotKeyModifiers.Win;
                    break;
                default:
                    if (!TryParseKey(part, out key))
                    {
                        return false;
                    }
                    break;
            }
        }

        if (key == Keys.None)
        {
            return false;
        }

        remainingChord = chords.Length > 1
            ? string.Join(" ", chords.Skip(1))
            : string.Empty;
        return true;
    }

    private static bool TryParseKey(string value, out Keys key)
    {
        key = Keys.None;
        if (value.Length == 1)
        {
            var character = value[0];
            if (character is >= 'a' and <= 'z')
            {
                key = Keys.A + (character - 'a');
                return true;
            }

            if (character is >= '0' and <= '9')
            {
                key = Keys.D0 + (character - '0');
                return true;
            }

            key = character switch
            {
                '.' => Keys.OemPeriod,
                ',' => Keys.Oemcomma,
                '/' => Keys.OemQuestion,
                ';' => Keys.OemSemicolon,
                '\'' => Keys.OemQuotes,
                '[' => Keys.OemOpenBrackets,
                ']' => Keys.OemCloseBrackets,
                '\\' => Keys.OemPipe,
                '-' => Keys.OemMinus,
                '=' => Keys.Oemplus,
                '`' => Keys.Oemtilde,
                _ => Keys.None
            };
            return key != Keys.None;
        }

        if (value.Length > 1 && value[0] == 'f'
            && int.TryParse(value[1..], out var function)
            && function is >= 1 and <= 24)
        {
            key = Keys.F1 + function - 1;
            return true;
        }

        key = value switch
        {
            "plus" or "equal" or "equals" => Keys.Oemplus,
            "minus" => Keys.OemMinus,
            "escape" or "esc" => Keys.Escape,
            "tab" => Keys.Tab,
            "space" => Keys.Space,
            "enter" or "return" => Keys.Return,
            "backspace" => Keys.Back,
            "delete" => Keys.Delete,
            "insert" => Keys.Insert,
            "home" => Keys.Home,
            "end" => Keys.End,
            "pageup" => Keys.PageUp,
            "pagedown" => Keys.PageDown,
            "up" => Keys.Up,
            "down" => Keys.Down,
            "left" => Keys.Left,
            "right" => Keys.Right,
            _ => Keys.None
        };
        return key != Keys.None;
    }
}
