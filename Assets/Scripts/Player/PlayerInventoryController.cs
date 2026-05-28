using System.Collections.Generic;
using FishNet.Object;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Hotbar inventory, item selection, drops, and networked pickup sync.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerInventoryController : NetworkBehaviour
{
    private const int MaxHotbarSlots = 5;
    /// <summary>HUD key labels for hotbar slots 0 through 4.</summary>
    private static readonly string[] SlotKeyLabels = { "C", "1", "2", "3", "4" };
    /// <summary>Monotonic id generator for server-tracked world drops.</summary>
    private static int s_nextWorldItemId = 1;
    /// <summary>Server map of replicated world item id to item type for pickup validation.</summary>
    private static readonly Dictionary<int, InventoryItemType> s_serverWorldItems = new();

    [Header("Backpack")]
    [SerializeField]
    private bool _hasBackpack;

    [Header("Slots")]
    [Tooltip("Total slots including camera slot (slot 0).")]
    [SerializeField]
    private int _slotsWithoutBackpack = 3;

    [Tooltip("Total slots including camera slot (slot 0).")]
    [SerializeField]
    private int _slotsWithBackpack = 5;

    [SerializeField]
    private bool _startWithFlashlight = true;

    [Header("Selection")]
    [SerializeField]
    [Range(-1, 4)]
    private int _selectedSlotIndex = -1;

    [Header("Drop")]
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

    /// <summary>HUD bridge for hotbar and item key hints.</summary>
    private PlayerHUDController _hudController;
    /// <summary>Handheld camera mode toggled when slot 0 is selected.</summary>
    private PlayerCameraMode _cameraMode;
    /// <summary>Held flashlight visual and light toggled when flashlight slot is selected.</summary>
    private PlayerFlashlightMode _flashlightMode;
    /// <summary>Movement used to sync backpack sprint multiplier.</summary>
    private PlayerMovement _movement;
    /// <summary>Gameplay camera used as drop spawn origin.</summary>
    private Camera _gameplayCamera;
    /// <summary>Item type per hotbar slot index.</summary>
    private InventoryItemType[] _slotItems = new InventoryItemType[MaxHotbarSlots];
    /// <summary>Display names per slot, refreshed before HUD publish.</summary>
    private readonly string[] _slotItemLabels = new string[MaxHotbarSlots];
    /// <summary>True after default slot contents have been assigned once.</summary>
    private bool _inventoryInitialized;

    /// <summary>Whether the player has a backpack (affects slot count and sprint).</summary>
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

    /// <summary>Number of hotbar slots available with the current backpack state.</summary>
    public int AvailableSlots => Mathf.Clamp(_hasBackpack ? _slotsWithBackpack : _slotsWithoutBackpack, 1, MaxHotbarSlots);

    /// <summary>Selected slot index, or -1 when nothing is equipped.</summary>
    public int SelectedSlotIndex => _selectedSlotIndex;

    /// <summary>Caches components, ensures interactor/flashlight, and initializes default slots.</summary>
    private void Awake()
    {
        _hudController = GetComponent<PlayerHUDController>();
        _cameraMode = GetComponent<PlayerCameraMode>();
        _movement = GetComponent<PlayerMovement>();
        _gameplayCamera = GetComponentInChildren<Camera>(true);

        if (!TryGetComponent(out _flashlightMode))
            _flashlightMode = gameObject.AddComponent<PlayerFlashlightMode>();

        if (!TryGetComponent<PlayerInteractor>(out _))
            gameObject.AddComponent<PlayerInteractor>();

        EnsureInventoryInitialized();
    }

    /// <summary>True when this instance should accept local input (offline or owner).</summary>
    public bool IsLocalControllingPlayer()
    {
        if (!IsSpawned)
            return true;
        return IsOwner;
    }

    /// <summary>True when this client should update HUD elements for this player.</summary>
    public bool ShouldPublishHud()
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
    }

    /// <summary>Clamps selection to available slots and applies backpack and equip state.</summary>
    private void Start()
    {
        if (_selectedSlotIndex >= AvailableSlots)
            _selectedSlotIndex = -1;
        ApplyBackpackState();
        ApplySelection(publishHud: true);
    }

    /// <summary>Re-applies inventory and HUD when the component is enabled.</summary>
    private void OnEnable()
    {
        EnsureInventoryInitialized();
        ApplyBackpackState();
        ApplySelection(publishHud: true);
    }

    /// <summary>Local owner handles drop and hotbar selection key input.</summary>
    private void Update()
    {
        if (!IsLocalControllingPlayer())
            return;

        if (KeybindManager.WasPressedThisFrame(KeybindAction.Drop))
            DropSelectedItem();

        Keyboard kb = Keyboard.current;
        if (kb == null)
            return;

        HandleSelectionInput(kb);
    }

    /// <summary>Refreshes equip state and HUD after external changes.</summary>
    public void RefreshHudState()
    {
        ApplySelection(publishHud: true);
    }

    /// <summary>Assigns default slot contents (camera in slot 0, optional starter flashlight).</summary>
    private void EnsureInventoryInitialized()
    {
        if (_inventoryInitialized)
            return;

        if (_slotItems == null || _slotItems.Length != MaxHotbarSlots)
            _slotItems = new InventoryItemType[MaxHotbarSlots];

        // Clear slots then pin camera to slot 0.
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

    /// <summary>Maps C and digit keys to hotbar slot toggle requests.</summary>
    private void HandleSelectionInput(Keyboard kb)
    {
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

    /// <summary>Toggles selection locally and syncs to server or via RPC when networked.</summary>
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

    /// <summary>Selects slot index or deselects if already selected.</summary>
    private void ToggleSlot(int index)
    {
        if (index < 0)
            return;

        if (index >= AvailableSlots)
            return;

        _selectedSlotIndex = (_selectedSlotIndex == index) ? -1 : index;
        ApplySelection(publishHud: true);
    }

    /// <summary>Updates equipped camera/flashlight and optionally publishes HUD state.</summary>
    private void ApplySelection(bool publishHud)
    {
        InventoryItemType selectedItem = GetSelectedItem();
        bool localControl = IsLocalControllingPlayer();

        // Equip modes only for local controlling player (camera) or any owner (flashlight visual).
        if (_cameraMode != null)
            _cameraMode.SetEquipped(localControl && selectedItem == InventoryItemType.Camera);

        if (_flashlightMode != null)
            _flashlightMode.SetEquipped(selectedItem == InventoryItemType.Flashlight);

        if (publishHud && ShouldPublishHud())
            PublishHotbarState();
    }

    /// <summary>Clamps selection, syncs backpack to movement, and refreshes HUD.</summary>
    private void ApplyBackpackState()
    {
        if (_selectedSlotIndex >= AvailableSlots)
            _selectedSlotIndex = -1;

        if (_movement != null)
            _movement.SetHasBackpack(_hasBackpack);

        PublishHotbarState();
        ApplySelection(publishHud: false);
    }

    /// <summary>Builds slot display names and pushes hotbar state to the HUD.</summary>
    private void PublishHotbarState()
    {
        if (_hudController == null || !ShouldPublishHud())
            return;

        for (int i = 0; i < _slotItemLabels.Length; i++)
            _slotItemLabels[i] = InventoryItemCatalog.GetDisplayName(_slotItems[i]);

        _hudController.SetHotbarState(AvailableSlots, _selectedSlotIndex, _slotItemLabels, SlotKeyLabels);
        PublishItemKeyHints();
    }

    /// <summary>Shows primary-use and drop key hints for the currently selected item.</summary>
    private void PublishItemKeyHints()
    {
        if (_hudController == null || !ShouldPublishHud())
            return;

        InventoryItemType selectedItem = GetSelectedItem();
        if (_selectedSlotIndex < 0 || selectedItem == InventoryItemType.None)
        {
            _hudController.SetItemKeyHints(false, string.Empty);
            return;
        }

        string primaryLine = null;
        if (InventoryItemCatalog.SupportsPrimaryUse(selectedItem))
        {
            primaryLine = KeybindManager.FormatHint(
                KeybindAction.ItemPrimaryUse,
                InventoryItemCatalog.GetPrimaryUseDescription(selectedItem));
        }

        string dropLine = null;
        if (InventoryItemCatalog.CanDrop(selectedItem))
        {
            dropLine = KeybindManager.FormatHint(
                KeybindAction.Drop,
                $"Drop {InventoryItemCatalog.GetDisplayName(selectedItem)}");
        }

        bool visible = !string.IsNullOrEmpty(primaryLine) || !string.IsNullOrEmpty(dropLine);
        _hudController.SetItemKeyHints(visible, primaryLine, dropLine);
    }

    /// <summary>Returns the item in the selected slot, or None when deselected.</summary>
    private InventoryItemType GetSelectedItem()
    {
        if (_selectedSlotIndex < 0 || _selectedSlotIndex >= _slotItems.Length)
            return InventoryItemType.None;

        return _slotItems[_selectedSlotIndex];
    }

    /// <summary>True if any hotbar slot already holds the given item type.</summary>
    private bool ContainsItem(InventoryItemType itemType)
    {
        for (int i = 0; i < _slotItems.Length; i++)
        {
            if (_slotItems[i] == itemType)
                return true;
        }

        return false;
    }

    /// <summary>First empty slot index from 1 upward, or -1 if full.</summary>
    private int GetFirstAvailableInventorySlot()
    {
        for (int i = 1; i < AvailableSlots; i++)
        {
            if (_slotItems[i] == InventoryItemType.None)
                return i;
        }

        return -1;
    }

    /// <summary>Resolves which slot receives a pickup (selected empty slot or first free).</summary>
    private bool TryGetPickupSlot(InventoryItemType itemType, out int slotIndex)
    {
        slotIndex = -1;

        if (itemType == InventoryItemType.None || itemType == InventoryItemType.Camera)
            return false;

        // Prefer filling the currently selected empty slot.
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

    /// <summary>Drops the selected item in front of the camera; offline or via server RPC.</summary>
    private void DropSelectedItem()
    {
        if (_selectedSlotIndex <= 0 || _selectedSlotIndex >= _slotItems.Length)
            return;

        InventoryItemType selectedItem = _slotItems[_selectedSlotIndex];
        if (!InventoryItemCatalog.CanDrop(selectedItem))
            return;

        if (_gameplayCamera == null)
            _gameplayCamera = GetComponentInChildren<Camera>(true);

        // Spawn pose and impulse from camera forward.
        Transform dropOrigin = _gameplayCamera != null ? _gameplayCamera.transform : transform;
        Vector3 forward = dropOrigin.forward;
        Vector3 spawnPosition = dropOrigin.position +
                                forward * _dropForwardDistance +
                                Vector3.up * _dropUpOffset;
        Quaternion spawnRotation = Quaternion.LookRotation(forward, Vector3.up);
        Vector3 spawnVelocity = forward * _dropForwardImpulse + Vector3.up * _dropUpImpulse;

        // Offline: spawn world item immediately and clear slot.
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
        // Networked: clear slot locally, then server or client RPC spawns replicated world item.
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

    /// <summary>Whether the item type can fit in the hotbar (for interact hints).</summary>
    public bool CanPickup(InventoryItemType itemType)
    {
        if (!IsLocalControllingPlayer())
            return false;
        return TryGetPickupSlot(itemType, out _);
    }

    /// <summary>Picks up a world item when interact is pressed on a focused pickup.</summary>
    public void RequestPickup(WorldInventoryItem worldItem)
    {
        if (worldItem == null || !IsLocalControllingPlayer())
            return;

        if (!TryGetPickupSlot(worldItem.ItemType, out int slotIndex))
            return;

        if (!IsSpawned)
        {
            _slotItems[slotIndex] = worldItem.ItemType;
            Destroy(worldItem.gameObject);
            ApplySelection(publishHud: true);
            return;
        }

        if (IsServerStarted)
            ServerTryPickupWorldItem(worldItem.NetworkItemId);
        else
            RpcRequestPickupWorldItem(worldItem.NetworkItemId);
    }

    /// <summary>Applies replicated slot contents and selection from an ObserversRpc.</summary>
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
    }

    /// <summary>Pushes full hotbar state to all observers from the server.</summary>
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

    /// <summary>Client requests server to toggle a hotbar slot.</summary>
    [ServerRpc(RequireOwnership = true)]
    private void RpcRequestToggleSlot(int index)
    {
        ToggleSlot(index);
        BroadcastInventoryState();
    }

    /// <summary>Client requests server to validate and finalize a drop.</summary>
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

    /// <summary>Client requests server to pick up a replicated world item by id.</summary>
    [ServerRpc(RequireOwnership = true)]
    private void RpcRequestPickupWorldItem(int networkItemId)
    {
        ServerTryPickupWorldItem(networkItemId);
    }

    /// <summary>Replicates hotbar slot contents and selection to all clients.</summary>
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

    /// <summary>Spawns a replicated world pickup on all clients.</summary>
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

    /// <summary>Destroys a replicated world pickup on all clients.</summary>
    [ObserversRpc]
    private void ObserversDestroyWorldItem(int networkItemId)
    {
        WorldInventoryItem.DestroyReplicated(networkItemId);
    }

    /// <summary>Registers dropped item on server and notifies clients to spawn world pickup.</summary>
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

    /// <summary>Validates pickup, assigns slot, removes world item, and broadcasts inventory.</summary>
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
