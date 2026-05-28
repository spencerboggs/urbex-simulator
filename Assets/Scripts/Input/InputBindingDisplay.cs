using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>Display labels for common keyboard keys via the Input System.</summary>
public static class InputBindingDisplay
{
    /// <summary>Returns the Input System display name for a letter key, or <paramref name="letterFallback"/>.</summary>
    public static string GetPrimaryKeyboardDisplay(string letterFallback)
    {
        if (string.IsNullOrEmpty(letterFallback))
            letterFallback = "?";

        Keyboard kb = Keyboard.current;
        if (kb == null)
            return letterFallback;

        // Map common HUD letters to Input System controls for localized key labels.
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
