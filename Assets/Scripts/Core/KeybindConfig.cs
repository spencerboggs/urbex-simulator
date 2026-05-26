using System;

// User-editable keybind configuration. Serialized to JSON at
// Application.persistentDataPath/keybinds.json by KeybindManager. Field names match
// the JSON keys verbatim, so anything you rename here is a breaking change for saved
// configs - bump `Version` if you do
//
// Every value must match a name in the UnityEngine.InputSystem.Key enum
// (case-insensitive). Examples: "E", "Q", "Space", "LeftShift", "LeftCtrl",
// "Digit1" (the "1" key on the number row), "W", "A", "S", "D"
//
// NOTE: not every action below is currently wired up in the game. The full list is
// kept in one place so the JSON file is forward-compatible - future migrations of
// PlayerMovement / ClimbingController etc. can adopt these without changing the file
// format. Actions that are wired today are flagged with [active]; the rest are
// reserved.
[Serializable]
public class KeybindConfig
{
    // Bump if the JSON layout changes in a breaking way
    public int Version = 1;

    // [active] Pick up world items, open doors, and other context-sensitive interactions
    public string Interact = "E";

    // [active] Drop the currently selected inventory item
    public string Drop = "Q";

    // Reserved - PlayerMovement still polls these directly
    public string Jump = "Space";
    public string Sprint = "LeftShift";
    public string Crouch = "LeftCtrl";

    // Reserved - PlayerInventoryController still polls these directly
    public string Camera = "C";
    public string Slot1 = "Digit1";
    public string Slot2 = "Digit2";
    public string Slot3 = "Digit3";
    public string Slot4 = "Digit4";

    // Reserved - PlayerMovement / ClimbingController still poll WASD directly
    public string MoveForward = "W";
    public string MoveBackward = "S";
    public string MoveLeft = "A";
    public string MoveRight = "D";
}

// Identifier for every rebindable action in the game. Names map 1:1 to fields on
// KeybindConfig. KeybindManager handles the lookup from action -> configured key
public enum KeybindAction
{
    Interact,
    Drop,
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
