using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

public static class InputBindingDisplay
{
    // Short label for a keyboard letter such as C
    // Uses Input System displayName when available
    public static string GetPrimaryKeyboardDisplay(string letterFallback)
    {
        if (string.IsNullOrEmpty(letterFallback))
            letterFallback = "?";

        Keyboard kb = Keyboard.current;
        if (kb == null)
            return letterFallback;

        KeyControl key = letterFallback.ToUpperInvariant() switch
        {
            "C" => kb.cKey,
            "F" => kb.fKey,
            "E" => kb.eKey,
            "Q" => kb.qKey,
            _ => null,
        };

        if (key != null && !string.IsNullOrEmpty(key.displayName))
            return key.displayName;

        return letterFallback;
    }
}
