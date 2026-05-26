using UnityEngine;

// Single source of truth for the Interact key on the player. Replaces the per-frame
// E-key polling that used to live inside PlayerInventoryController. Both world
// pickups and IInteractable targets (doors, future switches/lockers/etc.) share the
// same bind through this component
//
// Each frame:
//   1. Cast a ray from the gameplay camera forward up to _interactionRange.
//   2. Walk hits in distance order until we find the first one that resolves to
//      either a WorldInventoryItem (pickup target) or an IInteractable (interact
//      target). Whichever is closer wins.
//   3. Drive the HUD context hint based on the focused target. If nothing is in
//      front but the inventory can offer a "drop" prompt, show that instead.
//   4. On Interact-keybind press, dispatch:
//        - WorldInventoryItem -> PlayerInventoryController.RequestPickup
//        - IInteractable      -> interactable.Interact(transform)
//
// PlayerInteractor expects to live on the same GameObject as PlayerInventoryController
// (so it can co-locate references) and a Camera should be a child of the player
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

    private PlayerInventoryController _inventory;
    private PlayerHUDController _hud;
    private Camera _gameplayCamera;

    private WorldInventoryItem _focusedItem;
    private IInteractable _focusedInteractable;

    // Cached buffer reused every frame to avoid GC churn.
    private readonly RaycastHit[] _hitBuffer = new RaycastHit[16];

    private void Awake()
    {
        _inventory = GetComponent<PlayerInventoryController>();
        _hud = GetComponent<PlayerHUDController>();
        _gameplayCamera = GetComponentInChildren<Camera>(true);
    }

    private void Update()
    {
        if (_inventory == null || !_inventory.IsLocalControllingPlayer())
            return;

        UpdateFocusedTarget();
        UpdateContextHint();

        if (!KeybindManager.WasPressedThisFrame(KeybindAction.Interact))
            return;

        // Items take priority over generic interactables when both resolve from the
        // same hit - the inventory pipeline is more specific and matches the
        // previous behaviour of the E key.
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

        // Sort just the slice we used. Bubble sort is fine - buffer is tiny.
        for (int i = 1; i < hitCount; i++)
        {
            for (int j = i; j > 0 && _hitBuffer[j].distance < _hitBuffer[j - 1].distance; j--)
            {
                (_hitBuffer[j], _hitBuffer[j - 1]) = (_hitBuffer[j - 1], _hitBuffer[j]);
            }
        }

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

    private void UpdateContextHint()
    {
        if (_hud == null || !_inventory.ShouldPublishHud())
            return;

        if (_focusedItem != null)
        {
            string key = KeybindManager.GetDisplayName(KeybindAction.Interact);

            if (_inventory.CanPickup(_focusedItem.ItemType))
                _hud.SetContextHint(true, $"Press {key} to pick up {_focusedItem.DisplayName}");
            else
                _hud.SetContextHint(true, $"No empty slot for {_focusedItem.DisplayName}");

            return;
        }

        if (_focusedInteractable != null)
        {
            string prompt = _focusedInteractable.GetInteractionPrompt();
            if (!string.IsNullOrEmpty(prompt))
            {
                string key = KeybindManager.GetDisplayName(KeybindAction.Interact);
                _hud.SetContextHint(true, $"Press {key} to {prompt.ToLowerInvariant()}");
                return;
            }
        }

        if (_inventory.TryGetDropPrompt(out string dropPrompt))
        {
            _hud.SetContextHint(true, dropPrompt);
            return;
        }

        _hud.SetContextHint(false, string.Empty);
    }
}
