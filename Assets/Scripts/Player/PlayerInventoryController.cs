using System.Collections.Generic;
using FishNet.Object;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public sealed class PlayerInventoryController : NetworkBehaviour
{
    private const int MaxHotbarSlots = 5;
    private static readonly string[] SlotKeyLabels = { "C", "1", "2", "3", "4" };
    private static int s_nextWorldItemId = 1;
    private static readonly Dictionary<int, InventoryItemType> s_serverWorldItems = new();

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

    [SerializeField]
    private bool _startWithFlashlight = true;

    [Header("Selection")]
    [SerializeField]
    [Range(-1, 4)]
    private int _selectedSlotIndex = -1;

    [Header("Pickup")]
    [SerializeField]
    [Min(0.5f)]
    private float _pickupRange = 3f;

    [SerializeField]
    [Min(0.1f)]
    private float _dropForwardDistance = 0.95f;

    [SerializeField]
    private float _dropUpOffset = -0.08f;

    [SerializeField]
    [Min(0f)]
    private float _dropForwardImpulse = 2.25f;

    [SerializeField]
    [Min(0f)]
    private float _dropUpImpulse = 0.8f;

    private PlayerHUDController _hudController;
    private PlayerCameraMode _cameraMode;
    private PlayerFlashlightMode _flashlightMode;
    private PlayerMovement _movement;
    private Camera _gameplayCamera;
    private WorldInventoryItem _focusedWorldItem;
    private InventoryItemType[] _slotItems = new InventoryItemType[MaxHotbarSlots];
    private readonly string[] _slotItemLabels = new string[MaxHotbarSlots];
    private bool _inventoryInitialized;

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

    public int AvailableSlots => Mathf.Clamp(_hasBackpack ? _slotsWithBackpack : _slotsWithoutBackpack, 1, MaxHotbarSlots);

    public int SelectedSlotIndex => _selectedSlotIndex;

    private void Awake()
    {
        _hudController = GetComponent<PlayerHUDController>();
        _cameraMode = GetComponent<PlayerCameraMode>();
        _movement = GetComponent<PlayerMovement>();
        _gameplayCamera = GetComponentInChildren<Camera>(true);

        if (!TryGetComponent(out _flashlightMode))
            _flashlightMode = gameObject.AddComponent<PlayerFlashlightMode>();

        EnsureInventoryInitialized();
    }

    private bool IsLocalControllingPlayer()
    {
        if (!IsSpawned)
            return true;
        return IsOwner;
    }

    private bool ShouldPublishHud()
    {
        return !IsSpawned || IsOwner;
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        EnsureInventoryInitialized();
        BroadcastInventoryState();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        EnsureInventoryInitialized();
        ApplySelection(publishHud: true);
        UpdateContextHint();
    }

    private void Start()
    {
        if (_selectedSlotIndex >= AvailableSlots)
            _selectedSlotIndex = -1;
        ApplyBackpackState();
        ApplySelection(publishHud: true);
        UpdateContextHint();
    }

    private void OnEnable()
    {
        // When re-enabled, refresh HUD + equip state
        EnsureInventoryInitialized();
        ApplyBackpackState();
        ApplySelection(publishHud: true);
        UpdateContextHint();
    }

    private void Update()
    {
        if (!IsLocalControllingPlayer())
            return;

        Keyboard kb = Keyboard.current;
        if (kb == null)
            return;

        HandleSelectionInput(kb);

        if (kb.qKey.wasPressedThisFrame)
            DropSelectedItem();

        UpdateFocusedWorldItem();

        if (kb.eKey.wasPressedThisFrame)
            TryPickupFocusedItem();

        UpdateContextHint();
    }

    public void RefreshHudState()
    {
        ApplySelection(publishHud: true);
        UpdateContextHint();
    }

    private void EnsureInventoryInitialized()
    {
        if (_inventoryInitialized)
            return;

        if (_slotItems == null || _slotItems.Length != MaxHotbarSlots)
            _slotItems = new InventoryItemType[MaxHotbarSlots];

        for (int i = 0; i < _slotItems.Length; i++)
            _slotItems[i] = InventoryItemType.None;

        _slotItems[0] = InventoryItemType.Camera;

        if (_startWithFlashlight && !ContainsItem(InventoryItemType.Flashlight))
        {
            int defaultSlot = GetFirstAvailableInventorySlot();
            if (defaultSlot > 0)
                _slotItems[defaultSlot] = InventoryItemType.Flashlight;
        }

        _inventoryInitialized = true;
    }

    private void HandleSelectionInput(Keyboard kb)
    {
        // Input mapping: [C, 1, 2, 3, 4]
        // Slot 0 is camera (C), slots 1-4 are inventory items (1-4)
        if (kb.cKey.wasPressedThisFrame)
            RequestToggleSlot(0);

        int requested = -1;
        if (kb.digit1Key.wasPressedThisFrame) requested = 1;
        else if (kb.digit2Key.wasPressedThisFrame) requested = 2;
        else if (kb.digit3Key.wasPressedThisFrame) requested = 3;
        else if (kb.digit4Key.wasPressedThisFrame) requested = 4;

        if (requested >= 0)
            RequestToggleSlot(requested);
    }

    private void RequestToggleSlot(int index)
    {
        ToggleSlot(index);

        if (IsSpawned)
        {
            if (IsServerStarted)
                BroadcastInventoryState();
            else
                RpcRequestToggleSlot(index);
        }
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
        InventoryItemType selectedItem = GetSelectedItem();
        bool localControl = IsLocalControllingPlayer();

        if (_cameraMode != null)
            _cameraMode.SetEquipped(localControl && selectedItem == InventoryItemType.Camera);

        if (_flashlightMode != null)
            _flashlightMode.SetEquipped(selectedItem == InventoryItemType.Flashlight);

        if (publishHud && ShouldPublishHud())
            PublishHotbarState();
    }

    private void ApplyBackpackState()
    {
        // Clamp selection into new range
        if (_selectedSlotIndex >= AvailableSlots)
            _selectedSlotIndex = -1;

        if (_movement != null)
            _movement.SetHasBackpack(_hasBackpack);

        PublishHotbarState();
        ApplySelection(publishHud: false);
    }

    private void PublishHotbarState()
    {
        if (_hudController == null || !ShouldPublishHud())
            return;

        for (int i = 0; i < _slotItemLabels.Length; i++)
            _slotItemLabels[i] = InventoryItemCatalog.GetDisplayName(_slotItems[i]);

        _hudController.SetHotbarState(AvailableSlots, _selectedSlotIndex, _slotItemLabels, SlotKeyLabels);
    }

    private InventoryItemType GetSelectedItem()
    {
        if (_selectedSlotIndex < 0 || _selectedSlotIndex >= _slotItems.Length)
            return InventoryItemType.None;

        return _slotItems[_selectedSlotIndex];
    }

    private bool ContainsItem(InventoryItemType itemType)
    {
        for (int i = 0; i < _slotItems.Length; i++)
        {
            if (_slotItems[i] == itemType)
                return true;
        }

        return false;
    }

    private int GetFirstAvailableInventorySlot()
    {
        for (int i = 1; i < AvailableSlots; i++)
        {
            if (_slotItems[i] == InventoryItemType.None)
                return i;
        }

        return -1;
    }

    private bool TryGetPickupSlot(InventoryItemType itemType, out int slotIndex)
    {
        slotIndex = -1;

        if (itemType == InventoryItemType.None || itemType == InventoryItemType.Camera)
            return false;

        if (_selectedSlotIndex > 0 &&
            _selectedSlotIndex < AvailableSlots &&
            _slotItems[_selectedSlotIndex] == InventoryItemType.None)
        {
            slotIndex = _selectedSlotIndex;
            return true;
        }

        slotIndex = GetFirstAvailableInventorySlot();
        return slotIndex > 0;
    }

    private void DropSelectedItem()
    {
        if (_selectedSlotIndex <= 0 || _selectedSlotIndex >= _slotItems.Length)
            return;

        InventoryItemType selectedItem = _slotItems[_selectedSlotIndex];
        if (!InventoryItemCatalog.CanDrop(selectedItem))
            return;

        if (_gameplayCamera == null)
            _gameplayCamera = GetComponentInChildren<Camera>(true);

        Transform dropOrigin = _gameplayCamera != null ? _gameplayCamera.transform : transform;
        Vector3 forward = dropOrigin.forward;
        Vector3 spawnPosition = dropOrigin.position +
                                forward * _dropForwardDistance +
                                Vector3.up * _dropUpOffset;
        Quaternion spawnRotation = Quaternion.LookRotation(forward, Vector3.up);
        Vector3 spawnVelocity = forward * _dropForwardImpulse + Vector3.up * _dropUpImpulse;

        if (!IsSpawned)
        {
            WorldInventoryItem droppedItem = WorldInventoryItem.SpawnDropped(
                selectedItem,
                spawnPosition,
                spawnRotation,
                transform);

            if (droppedItem != null && droppedItem.TryGetComponent(out Rigidbody rb))
                rb.AddForce(spawnVelocity, ForceMode.VelocityChange);
        }
        else
        {
            int droppedSlot = _selectedSlotIndex;
            _slotItems[droppedSlot] = InventoryItemType.None;
            _selectedSlotIndex = -1;
            ApplySelection(publishHud: true);

            if (IsServerStarted)
                ServerFinalizeDrop(selectedItem, spawnPosition, spawnRotation, spawnVelocity);
            else
                RpcRequestDropSelectedItem(selectedItem, spawnPosition, spawnRotation, spawnVelocity);

            return;
        }

        _slotItems[_selectedSlotIndex] = InventoryItemType.None;
        _selectedSlotIndex = -1;
        ApplySelection(publishHud: true);
    }

    private void TryPickupFocusedItem()
    {
        UpdateFocusedWorldItem();
        if (_focusedWorldItem == null)
            return;

        if (!TryGetPickupSlot(_focusedWorldItem.ItemType, out int slotIndex))
            return;

        if (!IsSpawned)
        {
            _slotItems[slotIndex] = _focusedWorldItem.ItemType;
            Destroy(_focusedWorldItem.gameObject);
            _focusedWorldItem = null;
            ApplySelection(publishHud: true);
            return;
        }

        if (IsServerStarted)
            ServerTryPickupWorldItem(_focusedWorldItem.NetworkItemId);
        else
            RpcRequestPickupWorldItem(_focusedWorldItem.NetworkItemId);
    }

    private void UpdateFocusedWorldItem()
    {
        _focusedWorldItem = FindFocusedWorldItem();
    }

    private WorldInventoryItem FindFocusedWorldItem()
    {
        if (_gameplayCamera == null)
            _gameplayCamera = GetComponentInChildren<Camera>(true);

        Transform viewpoint = _gameplayCamera != null ? _gameplayCamera.transform : transform;
        RaycastHit[] hits = Physics.RaycastAll(
            viewpoint.position,
            viewpoint.forward,
            _pickupRange,
            ~0,
            QueryTriggerInteraction.Ignore);

        WorldInventoryItem closest = null;
        float closestDistance = float.MaxValue;

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null || hit.collider.transform.IsChildOf(transform))
                continue;

            WorldInventoryItem item = hit.collider.GetComponentInParent<WorldInventoryItem>();
            if (item == null)
                continue;

            if (hit.distance < closestDistance)
            {
                closest = item;
                closestDistance = hit.distance;
            }
        }

        return closest;
    }

    private void UpdateContextHint()
    {
        if (_hudController == null || !ShouldPublishHud())
            return;

        if (_focusedWorldItem != null)
        {
            if (TryGetPickupSlot(_focusedWorldItem.ItemType, out _))
            {
                string pickupKey = InputBindingDisplay.GetPrimaryKeyboardDisplay("E");
                _hudController.SetContextHint(true, $"Press {pickupKey} to pick up {_focusedWorldItem.DisplayName}");
            }
            else
            {
                _hudController.SetContextHint(true, $"No empty slot for {_focusedWorldItem.DisplayName}");
            }

            return;
        }

        InventoryItemType selectedItem = GetSelectedItem();
        if (_selectedSlotIndex > 0 && InventoryItemCatalog.CanDrop(selectedItem))
        {
            string dropKey = InputBindingDisplay.GetPrimaryKeyboardDisplay("Q");
            _hudController.SetContextHint(true, $"{dropKey} drop {InventoryItemCatalog.GetDisplayName(selectedItem)}");
            return;
        }

        _hudController.SetContextHint(false, string.Empty);
    }

    private void ApplyInventoryState(
        InventoryItemType slot0,
        InventoryItemType slot1,
        InventoryItemType slot2,
        InventoryItemType slot3,
        InventoryItemType slot4,
        int selectedSlot)
    {
        _slotItems[0] = slot0;
        _slotItems[1] = slot1;
        _slotItems[2] = slot2;
        _slotItems[3] = slot3;
        _slotItems[4] = slot4;
        _selectedSlotIndex = selectedSlot;
        _inventoryInitialized = true;

        ApplySelection(publishHud: true);
        UpdateContextHint();
    }

    private void BroadcastInventoryState()
    {
        if (!IsServerStarted)
            return;

        ObserversApplyInventoryState(
            _slotItems[0],
            _slotItems[1],
            _slotItems[2],
            _slotItems[3],
            _slotItems[4],
            _selectedSlotIndex);
    }

    [ServerRpc(RequireOwnership = true)]
    private void RpcRequestToggleSlot(int index)
    {
        ToggleSlot(index);
        BroadcastInventoryState();
    }

    [ServerRpc(RequireOwnership = true)]
    private void RpcRequestDropSelectedItem(
        InventoryItemType itemType,
        Vector3 position,
        Quaternion rotation,
        Vector3 velocity)
    {
        InventoryItemType selectedItem = GetSelectedItem();
        if (selectedItem != itemType || !InventoryItemCatalog.CanDrop(selectedItem))
        {
            BroadcastInventoryState();
            return;
        }

        int droppedSlot = _selectedSlotIndex;
        if (droppedSlot <= 0 || droppedSlot >= _slotItems.Length)
        {
            BroadcastInventoryState();
            return;
        }

        _slotItems[droppedSlot] = InventoryItemType.None;
        _selectedSlotIndex = -1;

        ServerFinalizeDrop(itemType, position, rotation, velocity);
    }

    [ServerRpc(RequireOwnership = true)]
    private void RpcRequestPickupWorldItem(int networkItemId)
    {
        ServerTryPickupWorldItem(networkItemId);
    }

    [ObserversRpc]
    private void ObserversApplyInventoryState(
        InventoryItemType slot0,
        InventoryItemType slot1,
        InventoryItemType slot2,
        InventoryItemType slot3,
        InventoryItemType slot4,
        int selectedSlot)
    {
        ApplyInventoryState(slot0, slot1, slot2, slot3, slot4, selectedSlot);
    }

    [ObserversRpc]
    private void ObserversSpawnWorldItem(
        int networkItemId,
        InventoryItemType itemType,
        Vector3 position,
        Quaternion rotation,
        Vector3 velocity)
    {
        WorldInventoryItem.SpawnReplicated(networkItemId, itemType, position, rotation, velocity);
    }

    [ObserversRpc]
    private void ObserversDestroyWorldItem(int networkItemId)
    {
        WorldInventoryItem.DestroyReplicated(networkItemId);
    }

    private void ServerFinalizeDrop(
        InventoryItemType itemType,
        Vector3 position,
        Quaternion rotation,
        Vector3 velocity)
    {
        int itemId = s_nextWorldItemId++;
        s_serverWorldItems[itemId] = itemType;

        ObserversSpawnWorldItem(itemId, itemType, position, rotation, velocity);
        BroadcastInventoryState();
    }

    private void ServerTryPickupWorldItem(int networkItemId)
    {
        if (!s_serverWorldItems.TryGetValue(networkItemId, out InventoryItemType itemType))
        {
            BroadcastInventoryState();
            return;
        }

        if (!TryGetPickupSlot(itemType, out int slotIndex))
        {
            BroadcastInventoryState();
            return;
        }

        _slotItems[slotIndex] = itemType;
        s_serverWorldItems.Remove(networkItemId);

        ObserversDestroyWorldItem(networkItemId);
        BroadcastInventoryState();
    }
}

