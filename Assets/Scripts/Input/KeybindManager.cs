using System;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>
/// Runtime keybind queries: load/save JSON config, poll input, and format HUD hint strings.
/// </summary>
public static class KeybindManager
{
    /// <summary>Filename under Application.persistentDataPath.</summary>
    public const string KeybindsFileName = "keybinds.json";

    /// <summary>Active keybind configuration loaded from or written to disk.</summary>
    private static KeybindConfig _config;

    /// <summary>True after <see cref="EnsureInitialized"/> has run at least once.</summary>
    private static bool _initialized;

    /// <summary>Current configuration (loads from disk on first access).</summary>
    public static KeybindConfig Current
    {
        get
        {
            EnsureInitialized();
            return _config;
        }
    }

    /// <summary>Absolute path to the keybinds JSON file.</summary>
    public static string KeybindsFilePath =>
        Path.Combine(Application.persistentDataPath, KeybindsFileName);

    /// <summary>True when keyboard or mouse devices are available.</summary>
    public static bool IsAvailable =>
        Keyboard.current != null || Mouse.current != null;

    /// <summary>Replaces the active config and saves to disk.</summary>
    public static void ApplyConfig(KeybindConfig newConfig)
    {
        if (newConfig == null)
            return;

        _config = newConfig;
        _initialized = true;
        Save();
    }

    /// <summary>Resets to defaults and saves.</summary>
    public static void ResetToDefaults()
    {
        _config = new KeybindConfig();
        _initialized = true;
        Save();
    }

    /// <summary>True if the binding was pressed this frame.</summary>
    public static bool WasPressedThisFrame(KeybindAction action)
    {
        if (TryGetMouseButtonControl(action, out ButtonControl mouseButton))
            return mouseButton.wasPressedThisFrame;

        KeyControl key = GetKeyControl(action);
        return key != null && key.wasPressedThisFrame;
    }

    /// <summary>True if the binding was released this frame.</summary>
    public static bool WasReleasedThisFrame(KeybindAction action)
    {
        if (TryGetMouseButtonControl(action, out ButtonControl mouseButton))
            return mouseButton.wasReleasedThisFrame;

        KeyControl key = GetKeyControl(action);
        return key != null && key.wasReleasedThisFrame;
    }

    /// <summary>True if the binding is held.</summary>
    public static bool IsPressed(KeybindAction action)
    {
        if (TryGetMouseButtonControl(action, out ButtonControl mouseButton))
            return mouseButton.isPressed;

        KeyControl key = GetKeyControl(action);
        return key != null && key.isPressed;
    }

    /// <summary>Formats a HUD hint such as "E - Pick up Flashlight".</summary>
    public static string FormatHint(KeybindAction action, string description)
    {
        if (string.IsNullOrEmpty(description))
            return GetDisplayName(action);
        return $"{GetDisplayName(action)} - {description}";
    }

    /// <summary>Human-readable label for the binding (LMB, key display name, etc.).</summary>
    public static string GetDisplayName(KeybindAction action)
    {
        EnsureInitialized();

        string raw = GetRawKeyName(action);
        if (TryGetMouseDisplayName(raw, out string mouseLabel))
            return mouseLabel;

        if (Keyboard.current != null && TryParseKey(raw, out Key key))
        {
            KeyControl control = Keyboard.current[key];
            if (control != null && !string.IsNullOrEmpty(control.displayName))
                return control.displayName;
        }

        return FormatKeyboardFallback(raw);
    }

    /// <summary>Configured key name from JSON (e.g. E, LeftButton, Digit1).</summary>
    public static string GetRawKeyName(KeybindAction action)
    {
        EnsureInitialized();

        return action switch
        {
            KeybindAction.Interact => _config.Interact,
            KeybindAction.Drop => _config.Drop,
            KeybindAction.ItemPrimaryUse => _config.ItemPrimaryUse,
            KeybindAction.Jump => _config.Jump,
            KeybindAction.Sprint => _config.Sprint,
            KeybindAction.Crouch => _config.Crouch,
            KeybindAction.Camera => _config.Camera,
            KeybindAction.Slot1 => _config.Slot1,
            KeybindAction.Slot2 => _config.Slot2,
            KeybindAction.Slot3 => _config.Slot3,
            KeybindAction.Slot4 => _config.Slot4,
            KeybindAction.MoveForward => _config.MoveForward,
            KeybindAction.MoveBackward => _config.MoveBackward,
            KeybindAction.MoveLeft => _config.MoveLeft,
            KeybindAction.MoveRight => _config.MoveRight,
            _ => string.Empty,
        };
    }

    /// <summary>Resolves a mouse button control when the binding names LeftButton, RightButton, or MiddleButton.</summary>
    private static bool TryGetMouseButtonControl(KeybindAction action, out ButtonControl button)
    {
        button = null;
        string raw = GetRawKeyName(action);
        if (!IsMouseButtonName(raw))
            return false;

        Mouse mouse = Mouse.current;
        if (mouse == null)
            return false;

        button = raw.ToLowerInvariant() switch
        {
            "leftbutton" => mouse.leftButton,
            "rightbutton" => mouse.rightButton,
            "middlebutton" => mouse.middleButton,
            _ => null,
        };
        return button != null;
    }

    /// <summary>Returns true when <paramref name="raw"/> is a supported mouse button binding name.</summary>
    private static bool IsMouseButtonName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return false;
        string lower = raw.Trim().ToLowerInvariant();
        return lower is "leftbutton" or "rightbutton" or "middlebutton";
    }

    /// <summary>Maps mouse button binding names to short HUD labels (LMB, RMB, MMB).</summary>
    private static bool TryGetMouseDisplayName(string raw, out string label)
    {
        label = null;
        if (!IsMouseButtonName(raw))
            return false;

        label = raw.Trim().ToLowerInvariant() switch
        {
            "leftbutton" => "LMB",
            "rightbutton" => "RMB",
            "middlebutton" => "MMB",
            _ => raw,
        };
        return true;
    }

    /// <summary>Returns the keyboard control for the action, or null for mouse bindings or missing devices.</summary>
    private static KeyControl GetKeyControl(KeybindAction action)
    {
        EnsureInitialized();

        string raw = GetRawKeyName(action);
        if (IsMouseButtonName(raw))
            return null;

        Keyboard kb = Keyboard.current;
        if (kb == null)
            return null;

        if (!TryParseKey(raw, out Key key))
            return null;

        return kb[key];
    }

    /// <summary>Parses a configured key name into an Input System <see cref="Key"/> value.</summary>
    private static bool TryParseKey(string raw, out Key key)
    {
        key = Key.None;

        if (string.IsNullOrWhiteSpace(raw))
            return false;

        // Single digit keys are stored as "0"-"9" in JSON but Input System uses Digit0-Digit9.
        if (raw.Length == 1 && raw[0] >= '0' && raw[0] <= '9')
            return Enum.TryParse("Digit" + raw, ignoreCase: true, out key);

        return Enum.TryParse(raw, ignoreCase: true, out key) && key != Key.None;
    }

    /// <summary>Formats a key name for HUD display when no live Input System control is available.</summary>
    private static string FormatKeyboardFallback(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return "?";

        if (raw.StartsWith("Digit", StringComparison.OrdinalIgnoreCase) && raw.Length == 6)
            return raw.Substring(5);

        // Insert spaces before interior capitals (e.g. LeftShift -> Left Shift).
        if (raw.Length > 1)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder(raw.Length + 4);
            sb.Append(raw[0]);
            for (int i = 1; i < raw.Length; i++)
            {
                char c = raw[i];
                if (char.IsUpper(c) && !char.IsUpper(raw[i - 1]))
                    sb.Append(' ');
                sb.Append(c);
            }
            return sb.ToString();
        }

        return raw;
    }

    /// <summary>Loads keybinds from disk on first access, applying defaults and version migrations when needed.</summary>
    private static void EnsureInitialized()
    {
        if (_initialized)
            return;

        _config = new KeybindConfig();
        _initialized = true;

        try
        {
            string path = KeybindsFilePath;
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                KeybindConfig loaded = JsonUtility.FromJson<KeybindConfig>(json);
                if (loaded != null)
                {
                    _config = loaded;
                    // Merge new default fields when the on-disk schema version is older.
                    if (loaded.Version < new KeybindConfig().Version)
                    {
                        KeybindConfig defaults = new KeybindConfig();
                        if (string.IsNullOrEmpty(loaded.ItemPrimaryUse))
                            loaded.ItemPrimaryUse = defaults.ItemPrimaryUse;
                        loaded.Version = defaults.Version;
                        _config = loaded;
                        Save();
                    }
                }
                else
                {
                    Debug.LogWarning(
                        $"[KeybindManager] {path} parsed as null. Falling back to defaults.");
                    Save();
                }
            }
            else
            {
                Save();
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning(
                $"[KeybindManager] Failed to load keybinds. Using defaults. {e.GetType().Name}: {e.Message}");
        }
    }

    /// <summary>Writes the active configuration to <see cref="KeybindsFilePath"/>.</summary>
    private static void Save()
    {
        try
        {
            string path = KeybindsFilePath;
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            string json = JsonUtility.ToJson(_config, prettyPrint: true);
            File.WriteAllText(path, json);
        }
        catch (Exception e)
        {
            Debug.LogWarning(
                $"[KeybindManager] Failed to save keybinds: {e.GetType().Name}: {e.Message}");
        }
    }
}
