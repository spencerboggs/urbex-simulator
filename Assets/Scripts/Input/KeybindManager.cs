using System;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

// Runtime entry point for keybind queries. Looks up the configured Key for an
// action, polls Keyboard.current, and exposes human-readable display strings for
// HUD prompts
//
// Lifecycle:
//   - Lazy: the first call to any public method loads keybinds.json (or writes the
//     defaults if the file is missing). All subsequent calls use the in-memory copy
//   - Defaults live in code (KeybindConfig field initializers), so a deleted /
//     corrupt JSON file never bricks input - we silently fall back to defaults and
//     log a warning
//
// File location:
//   Application.persistentDataPath/keybinds.json
//   Windows: %LocalAppData%Low\<company>\<product>\keybinds.json
//   macOS:   ~/Library/Application Support/<company>/<product>/keybinds.json
//   Linux:   ~/.config/unity3d/<company>/<product>/keybinds.json
//
// The file is plain JSON - users can edit it with any text editor or via a future
// in-game settings UI (which should call ApplyConfig + Save)
public static class KeybindManager
{
    public const string KeybindsFileName = "keybinds.json";

    private static KeybindConfig _config;
    private static bool _initialized;

    // Read-only snapshot of the currently active config. Don't mutate the returned
    // instance directly - call ApplyConfig instead so the JSON stays in sync
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

    // True if the underlying input subsystem can actually be queried this frame
    // Editor scripts and tests may call query methods before Keyboard.current exists
    public static bool IsAvailable => Keyboard.current != null;

    // Replace the active config and write it to disk. Use this from a future
    // settings UI to persist user changes
    public static void ApplyConfig(KeybindConfig newConfig)
    {
        if (newConfig == null)
            return;

        _config = newConfig;
        _initialized = true;
        Save();
    }

    // Reset everything to compiled defaults and overwrite the JSON file
    public static void ResetToDefaults()
    {
        _config = new KeybindConfig();
        _initialized = true;
        Save();
    }

    // True if the configured key for `action` transitioned to pressed this frame
    // Returns false (without throwing) when no keyboard device is connected
    public static bool WasPressedThisFrame(KeybindAction action)
    {
        KeyControl key = GetKeyControl(action);
        return key != null && key.wasPressedThisFrame;
    }

    // True if the configured key for `action` transitioned to released this frame
    public static bool WasReleasedThisFrame(KeybindAction action)
    {
        KeyControl key = GetKeyControl(action);
        return key != null && key.wasReleasedThisFrame;
    }

    // True while the configured key for `action` is held down
    public static bool IsPressed(KeybindAction action)
    {
        KeyControl key = GetKeyControl(action);
        return key != null && key.isPressed;
    }

    // Short human-readable label for the configured key ("E", "Shift", "1", ...)
    // Uses the Input System's runtime displayName when available so it adapts to
    // keyboard layouts (e.g. "Z" -> "W" on AZERTY for the W bind).
    public static string GetDisplayName(KeybindAction action)
    {
        EnsureInitialized();

        string raw = GetRawKeyName(action);
        if (Keyboard.current != null && TryParseKey(raw, out Key key))
        {
            KeyControl control = Keyboard.current[key];
            if (control != null && !string.IsNullOrEmpty(control.displayName))
                return control.displayName;
        }

        return FormatFallback(raw);
    }

    // Underlying key string from the active config (e.g. "E", "Digit1"). Useful for
    // settings UIs that want to show the raw bound key
    public static string GetRawKeyName(KeybindAction action)
    {
        EnsureInitialized();

        return action switch
        {
            KeybindAction.Interact => _config.Interact,
            KeybindAction.Drop => _config.Drop,
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

    private static KeyControl GetKeyControl(KeybindAction action)
    {
        EnsureInitialized();

        Keyboard kb = Keyboard.current;
        if (kb == null)
            return null;

        string raw = GetRawKeyName(action);
        if (!TryParseKey(raw, out Key key))
            return null;

        return kb[key];
    }

    private static bool TryParseKey(string raw, out Key key)
    {
        key = Key.None;

        if (string.IsNullOrWhiteSpace(raw))
            return false;

        // Accept bare digits ("1") as a friendlier shorthand for "Digit1"
        if (raw.Length == 1 && raw[0] >= '0' && raw[0] <= '9')
        {
            return Enum.TryParse("Digit" + raw, ignoreCase: true, out key);
        }

        return Enum.TryParse(raw, ignoreCase: true, out key) && key != Key.None;
    }

    // Cheap user-facing fallback when the Input System has no displayName for a key
    // (e.g. running in batch mode). "Digit1" -> "1", "LeftShift" -> "Left Shift"
    private static string FormatFallback(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return "?";

        if (raw.StartsWith("Digit", StringComparison.OrdinalIgnoreCase) && raw.Length == 6)
            return raw.Substring(5);

        // Insert spaces before capitals after the first character: "LeftShift" -> "Left Shift"
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

        // Set defaults first so a load failure still leaves the manager usable.
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
                    // If the on-disk version is older, rewrite with the defaults
                    // merged in for any new fields. JsonUtility already populated
                    // missing fields with the compiled defaults
                    if (loaded.Version != new KeybindConfig().Version)
                        Save();
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
                // First run - write defaults so the user can find and edit the file
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
