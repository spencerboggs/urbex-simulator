using System;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

// Runtime entry point for keybind queries. Looks up the configured Key for an
// action, polls Keyboard.current / Mouse.current, and exposes human-readable
// display strings for HUD prompts
public static class KeybindManager
{
    public const string KeybindsFileName = "keybinds.json";

    private static KeybindConfig _config;
    private static bool _initialized;

    public static KeybindConfig Current
    {
        get
        {
            EnsureInitialized();
            return _config;
        }
    }

    public static string KeybindsFilePath =>
        Path.Combine(Application.persistentDataPath, KeybindsFileName);

    public static bool IsAvailable =>
        Keyboard.current != null || Mouse.current != null;

    public static void ApplyConfig(KeybindConfig newConfig)
    {
        if (newConfig == null)
            return;

        _config = newConfig;
        _initialized = true;
        Save();
    }

    public static void ResetToDefaults()
    {
        _config = new KeybindConfig();
        _initialized = true;
        Save();
    }

    public static bool WasPressedThisFrame(KeybindAction action)
    {
        if (TryGetMouseButtonControl(action, out ButtonControl mouseButton))
            return mouseButton.wasPressedThisFrame;

        KeyControl key = GetKeyControl(action);
        return key != null && key.wasPressedThisFrame;
    }

    public static bool WasReleasedThisFrame(KeybindAction action)
    {
        if (TryGetMouseButtonControl(action, out ButtonControl mouseButton))
            return mouseButton.wasReleasedThisFrame;

        KeyControl key = GetKeyControl(action);
        return key != null && key.wasReleasedThisFrame;
    }

    public static bool IsPressed(KeybindAction action)
    {
        if (TryGetMouseButtonControl(action, out ButtonControl mouseButton))
            return mouseButton.isPressed;

        KeyControl key = GetKeyControl(action);
        return key != null && key.isPressed;
    }

    // HUD format: "E - Pick up Flashlight"
    public static string FormatHint(KeybindAction action, string description)
    {
        if (string.IsNullOrEmpty(description))
            return GetDisplayName(action);
        return $"{GetDisplayName(action)} - {description}";
    }

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

    private static bool IsMouseButtonName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return false;
        string lower = raw.Trim().ToLowerInvariant();
        return lower is "leftbutton" or "rightbutton" or "middlebutton";
    }

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

    private static bool TryParseKey(string raw, out Key key)
    {
        key = Key.None;

        if (string.IsNullOrWhiteSpace(raw))
            return false;

        if (raw.Length == 1 && raw[0] >= '0' && raw[0] <= '9')
            return Enum.TryParse("Digit" + raw, ignoreCase: true, out key);

        return Enum.TryParse(raw, ignoreCase: true, out key) && key != Key.None;
    }

    private static string FormatKeyboardFallback(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return "?";

        if (raw.StartsWith("Digit", StringComparison.OrdinalIgnoreCase) && raw.Length == 6)
            return raw.Substring(5);

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
