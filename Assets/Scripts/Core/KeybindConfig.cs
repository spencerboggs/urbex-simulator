using System;

/// <summary>
/// User-editable keybind configuration serialized to
/// Application.persistentDataPath/keybinds.json by <see cref="KeybindManager"/>.
/// JSON field names match property names; bump <see cref="Version"/> on breaking layout changes.
/// Values must match <see cref="UnityEngine.InputSystem.Key"/> names (case-insensitive).
/// </summary>
[Serializable]
public class KeybindConfig
{
    /// <summary>Schema version for migrations.</summary>
    public int Version = 2;

    /// <summary>Interact (pickup, doors, context use).</summary>
    public string Interact = "E";

    /// <summary>Drop equipped item.</summary>
    public string Drop = "Q";

    /// <summary>Primary use for equipped item (mouse button names: LeftButton, RightButton, MiddleButton).</summary>
    public string ItemPrimaryUse = "LeftButton";

    /// <summary>Reserved for future PlayerMovement wiring.</summary>
    public string Jump = "Space";

    /// <summary>Reserved for future PlayerMovement wiring.</summary>
    public string Sprint = "LeftShift";

    /// <summary>Reserved for future PlayerMovement wiring.</summary>
    public string Crouch = "LeftCtrl";

    /// <summary>Reserved for future inventory wiring.</summary>
    public string Camera = "C";

    /// <summary>Reserved for future inventory wiring.</summary>
    public string Slot1 = "Digit1";

    /// <summary>Reserved for future inventory wiring.</summary>
    public string Slot2 = "Digit2";

    /// <summary>Reserved for future inventory wiring.</summary>
    public string Slot3 = "Digit3";

    /// <summary>Reserved for future inventory wiring.</summary>
    public string Slot4 = "Digit4";

    /// <summary>Reserved for future movement wiring.</summary>
    public string MoveForward = "W";

    /// <summary>Reserved for future movement wiring.</summary>
    public string MoveBackward = "S";

    /// <summary>Reserved for future movement wiring.</summary>
    public string MoveLeft = "A";

    /// <summary>Reserved for future movement wiring.</summary>
    public string MoveRight = "D";
}

/// <summary>Rebindable actions; names map 1:1 to <see cref="KeybindConfig"/> fields.</summary>
public enum KeybindAction
{
    Interact,
    Drop,
    ItemPrimaryUse,
    Jump,
    Sprint,
    Crouch,
    Camera,
    Slot1,
    Slot2,
    Slot3,
    Slot4,
    MoveForward,
    MoveBackward,
    MoveLeft,
    MoveRight,
}
