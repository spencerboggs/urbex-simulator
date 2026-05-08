using FishNet.Object;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public sealed class PlayerInventoryController : MonoBehaviour
{
    [Header("Backpack")]
    [SerializeField]
    private bool _hasBackpack;

    [Header("Slots")]
    [Tooltip("Total slots including camera slot (slot 0).")]
    [SerializeField]
    private int _slotsWithoutBackpack = 3; // camera + 2

    [Tooltip("Total slots including camera slot (slot 0).")]
    [SerializeField]
    private int _slotsWithBackpack = 5; // camera + 4

    [Header("Selection")]
    [SerializeField]
    [Range(-1, 4)]
    private int _selectedSlotIndex = -1;

    private PlayerHUDController _hudController;
    private PlayerCameraMode _cameraMode;
    private PlayerMovement _movement;
    private NetworkObject _networkObject;

    public bool HasBackpack
    {
        get => _hasBackpack;
        set
        {
            if (_hasBackpack == value)
                return;
            _hasBackpack = value;
            ApplyBackpackState();
        }
    }

    public int AvailableSlots => Mathf.Clamp(_hasBackpack ? _slotsWithBackpack : _slotsWithoutBackpack, 1, 5);

    public int SelectedSlotIndex => _selectedSlotIndex;

    private void Awake()
    {
        _hudController = GetComponent<PlayerHUDController>();
        _cameraMode = GetComponent<PlayerCameraMode>();
        _movement = GetComponent<PlayerMovement>();
        TryGetComponent(out _networkObject);
    }

    private bool IsLocalControllingPlayer()
    {
        if (_networkObject == null || !_networkObject.IsSpawned)
            return true;
        return _networkObject.IsOwner;
    }

    private void Start()
    {
        if (_selectedSlotIndex >= AvailableSlots)
            _selectedSlotIndex = -1;
        ApplyBackpackState();
        ApplySelection(publishHud: true);
    }

    private void OnEnable()
    {
        // When re-enabled, refresh HUD + equip state
        ApplyBackpackState();
        ApplySelection(publishHud: true);
    }

    private void Update()
    {
        if (!IsLocalControllingPlayer())
            return;

        Keyboard kb = Keyboard.current;
        if (kb == null)
            return;

        // Input mapping: [C, 1, 2, 3, 4]
        // Slot 0 is camera (C), slots 1-4 are inventory items (1-4)
        if (kb.cKey.wasPressedThisFrame)
        {
            ToggleSlot(0);
            return;
        }

        int requested = -1;
        if (kb.digit1Key.wasPressedThisFrame) requested = 1;
        else if (kb.digit2Key.wasPressedThisFrame) requested = 2;
        else if (kb.digit3Key.wasPressedThisFrame) requested = 3;
        else if (kb.digit4Key.wasPressedThisFrame) requested = 4;

        if (requested >= 0)
            ToggleSlot(requested);
    }

    private void ToggleSlot(int index)
    {
        if (index < 0)
            return;

        // If slot is not available (due to backpack limits), ignore
        if (index >= AvailableSlots)
            return;

        // Pressing the currently selected slot key stows it (no active item)
        _selectedSlotIndex = (_selectedSlotIndex == index) ? -1 : index;
        ApplySelection(publishHud: true);
    }

    private void ApplySelection(bool publishHud)
    {
        // Slot 0 is camera. Equipping camera toggles overlay but does not hide main HUD
        if (_cameraMode != null)
            _cameraMode.SetEquipped(_selectedSlotIndex == 0);

        if (publishHud && _hudController != null)
            _hudController.SetHotbarState(AvailableSlots, _selectedSlotIndex);
    }

    private void ApplyBackpackState()
    {
        // Clamp selection into new range
        if (_selectedSlotIndex >= AvailableSlots)
            _selectedSlotIndex = -1;

        if (_movement != null)
            _movement.SetHasBackpack(_hasBackpack);

        _hudController?.SetHotbarState(AvailableSlots, _selectedSlotIndex);

        // Ensure camera equip matches clamped selection
        if (_cameraMode != null)
            _cameraMode.SetEquipped(_selectedSlotIndex == 0);
    }
}

