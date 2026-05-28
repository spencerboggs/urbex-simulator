using UnityEngine;

/// <summary>
/// Raycasts from the gameplay camera to focus world pickups and <see cref="IInteractable"/> targets,
/// drives context hints, and dispatches interact input to inventory or interactables.
/// </summary>
/// <remarks>
/// Expects the same GameObject as <see cref="PlayerInventoryController"/> and a child gameplay camera.
/// </remarks>
[RequireComponent(typeof(PlayerInventoryController))]
[DisallowMultipleComponent]
public sealed class PlayerInteractor : MonoBehaviour
{
    [Header("Targeting")]
    [Tooltip("How far the player can reach to pick up an item or interact with an object.")]
    [SerializeField]
    [Min(0.5f)]
    private float _interactionRange = 3f;

    [Tooltip("Which layers count as interaction targets. Leave at -1 (Everything) to match the existing pickup behaviour.")]
    [SerializeField]
    private LayerMask _interactionMask = ~0;

    /// <summary>Inventory on the same player; receives pickup requests.</summary>
    private PlayerInventoryController _inventory;
    /// <summary>HUD used for context hint lines.</summary>
    private PlayerHUDController _hud;
    /// <summary>Gameplay camera used as ray origin for targeting.</summary>
    private Camera _gameplayCamera;

    /// <summary>World pickup under the current crosshair, if any.</summary>
    private WorldInventoryItem _focusedItem;
    /// <summary>Interactable under the current crosshair, if no pickup took priority.</summary>
    private IInteractable _focusedInteractable;

    /// <summary>Reused buffer for Physics.RaycastNonAlloc to avoid per-frame allocations.</summary>
    private readonly RaycastHit[] _hitBuffer = new RaycastHit[16];

    /// <summary>Caches player components and gameplay camera.</summary>
    private void Awake()
    {
        _inventory = GetComponent<PlayerInventoryController>();
        _hud = GetComponent<PlayerHUDController>();
        _gameplayCamera = GetComponentInChildren<Camera>(true);
    }

    /// <summary>Updates focus and hints each frame; dispatches interact on key press.</summary>
    private void Update()
    {
        if (_inventory == null || !_inventory.IsLocalControllingPlayer())
            return;

        UpdateFocusedTarget();
        UpdateContextHint();

        if (!KeybindManager.WasPressedThisFrame(KeybindAction.Interact))
            return;

        // Pickups take priority over generic interactables on the same hit.
        if (_focusedItem != null)
        {
            _inventory.RequestPickup(_focusedItem);
            return;
        }

        if (_focusedInteractable != null && _focusedInteractable.CanInteract(transform))
        {
            _focusedInteractable.Interact(transform);
        }
    }

    /// <summary>Raycasts from the camera and sets focused pickup or interactable.</summary>
    private void UpdateFocusedTarget()
    {
        _focusedItem = null;
        _focusedInteractable = null;

        if (_gameplayCamera == null)
        {
            _gameplayCamera = GetComponentInChildren<Camera>(true);
            if (_gameplayCamera == null)
                return;
        }

        Transform viewpoint = _gameplayCamera.transform;
        int hitCount = Physics.RaycastNonAlloc(
            viewpoint.position,
            viewpoint.forward,
            _hitBuffer,
            _interactionRange,
            _interactionMask,
            QueryTriggerInteraction.Ignore);

        if (hitCount <= 0)
            return;

        // Sort hits by distance so nearest valid target wins.
        for (int i = 1; i < hitCount; i++)
        {
            for (int j = i; j > 0 && _hitBuffer[j].distance < _hitBuffer[j - 1].distance; j--)
            {
                (_hitBuffer[j], _hitBuffer[j - 1]) = (_hitBuffer[j - 1], _hitBuffer[j]);
            }
        }

        // Walk sorted hits: pickups first, then interactables.
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = _hitBuffer[i];
            Collider col = hit.collider;
            if (col == null || col.transform.IsChildOf(transform))
                continue;

            WorldInventoryItem item = col.GetComponentInParent<WorldInventoryItem>();
            if (item != null)
            {
                _focusedItem = item;
                return;
            }

            IInteractable interactable = col.GetComponentInParent<IInteractable>();
            if (interactable != null && interactable.CanInteract(transform))
            {
                _focusedInteractable = interactable;
                return;
            }
        }
    }

    /// <summary>Pushes pickup or interact prompt text to the HUD context hint line.</summary>
    private void UpdateContextHint()
    {
        if (_hud == null || !_inventory.ShouldPublishHud())
            return;

        // Pickup hint: pick up or no empty slot.
        if (_focusedItem != null)
        {
            string itemName = InventoryItemCatalog.GetDisplayName(_focusedItem.ItemType);
            if (string.IsNullOrEmpty(itemName))
                itemName = _focusedItem.DisplayName;

            if (_inventory.CanPickup(_focusedItem.ItemType))
            {
                _hud.SetContextHint(
                    true,
                    KeybindManager.FormatHint(KeybindAction.Interact, $"Pick up {itemName}"));
            }
            else
            {
                _hud.SetContextHint(true, $"No empty slot for {itemName}");
            }

            return;
        }

        // Interactable prompt from IInteractable when no pickup is focused.
        if (_focusedInteractable != null)
        {
            string prompt = _focusedInteractable.GetInteractionPrompt();
            if (!string.IsNullOrEmpty(prompt))
            {
                _hud.SetContextHint(
                    true,
                    KeybindManager.FormatHint(KeybindAction.Interact, prompt));
                return;
            }
        }

        _hud.SetContextHint(false, string.Empty);
    }
}
