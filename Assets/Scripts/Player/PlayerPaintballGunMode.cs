using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Held paintball gun that fires physics paintballs on primary use.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerPaintballGunMode : MonoBehaviour
{
    [Header("Paint")]
    [Tooltip("Hex color for paintball marks (for example #00AAFF).")]
    [SerializeField]
    private string _hexColor = "#00AAFF";

    [Tooltip("World diameter of the paint splat left on impact.")]
    [SerializeField]
    [Min(0.02f)]
    private float _markDiameter = 0.14f;

    [Tooltip("Peak opacity of each paintball splat.")]
    [SerializeField]
    [Range(0.02f, 1f)]
    private float _markOpacity = 0.72f;

    [Header("Firing")]
    [Tooltip("Initial speed of fired paintballs in meters per second.")]
    [SerializeField]
    [Min(1f)]
    private float _projectileSpeed = 24f;

    [Tooltip("Minimum seconds between shots.")]
    [SerializeField]
    [Min(0.05f)]
    private float _fireCooldownSeconds = 0.35f;

    [Tooltip("Forward offset from the camera where paintballs spawn.")]
    [SerializeField]
    [Min(0.1f)]
    private float _muzzleForwardOffset = 0.72f;

    [Tooltip("Vertical offset from the camera where paintballs spawn.")]
    [SerializeField]
    private float _muzzleUpOffset = -0.04f;

    [Header("Placement")]
    [SerializeField]
    private Vector3 _localPosition = new(0.24f, -0.14f, 0.46f);

    [SerializeField]
    private Vector3 _localEulerAngles = new(0f, -8f, 0f);

    /// <summary>Gameplay camera used for aim and muzzle placement.</summary>
    private Camera _gameplayCamera;
    /// <summary>Parent object for the held gun visual.</summary>
    private GameObject _anchor;
    /// <summary>Instantiated held mesh root.</summary>
    private Transform _heldVisualRoot;
    /// <summary>Inventory controller used to gate input to the local player.</summary>
    private PlayerInventoryController _inventory;
    /// <summary>Whether the paintball gun hotbar slot is selected.</summary>
    private bool _equipped;
    /// <summary>Next time a shot is allowed.</summary>
    private float _nextFireTime;

    /// <summary>Whether the paintball gun is selected in the hotbar.</summary>
    public bool IsEquipped => _equipped;

    /// <summary>Validates paint color in the editor.</summary>
    private void OnValidate()
    {
        if (!HexColorUtility.TryParse(_hexColor, out _))
            _hexColor = "#00AAFF";
    }

    /// <summary>Resolves dependencies and builds the held gun anchor.</summary>
    private void Awake()
    {
        _gameplayCamera = GetComponentInChildren<Camera>(true);
        TryGetComponent(out _inventory);
        EnsureAnchor();
        RefreshPresentation();
    }

    /// <summary>Hides the anchor when this component is disabled.</summary>
    private void OnDisable()
    {
        if (_anchor != null)
            _anchor.SetActive(false);
    }

    /// <summary>Fires paintballs while primary use is pressed and cooldown allows.</summary>
    private void Update()
    {
        if (!_equipped || !isActiveAndEnabled)
            return;

        if (_inventory != null && !_inventory.IsLocalControllingPlayer())
            return;

        if (!KeybindManager.WasPressedThisFrame(KeybindAction.ItemPrimaryUse))
            return;

        TryFire();
    }

    /// <summary>Equips or stows the held paintball gun visual.</summary>
    public void SetEquipped(bool equipped)
    {
        _equipped = equipped;
        EnsureAnchor();
        RefreshPresentation();
    }

    /// <summary>Spawns a paintball from the camera-forward muzzle when off cooldown.</summary>
    private void TryFire()
    {
        if (Time.time < _nextFireTime)
            return;

        if (_gameplayCamera == null)
            _gameplayCamera = GetComponentInChildren<Camera>(true);

        if (_gameplayCamera == null)
            return;

        Transform aimTransform = _gameplayCamera.transform;
        Vector3 forward = aimTransform.forward;
        Vector3 spawnPosition = aimTransform.position + forward * _muzzleForwardOffset + Vector3.up * _muzzleUpOffset;
        Vector3 velocity = forward * _projectileSpeed;
        Color paintColor = HexColorUtility.ParseOrDefault(_hexColor, Color.cyan);

        PaintballProjectile.Spawn(
            spawnPosition,
            velocity,
            paintColor,
            _markDiameter,
            _markOpacity,
            transform);

        _nextFireTime = Time.time + _fireCooldownSeconds;
    }

    /// <summary>Creates or updates the camera-parented anchor and held mesh.</summary>
    private void EnsureAnchor()
    {
        if (_gameplayCamera == null)
            _gameplayCamera = GetComponentInChildren<Camera>(true);

        if (_gameplayCamera == null)
            return;

        if (_anchor == null)
        {
            _anchor = new GameObject("__HeldPaintballGun");
            _anchor.transform.SetParent(_gameplayCamera.transform, false);
        }

        EnsureHeldVisual();

        Transform anchorTransform = _anchor.transform;
        anchorTransform.localPosition = _localPosition;
        anchorTransform.localRotation = Quaternion.Euler(_localEulerAngles);
        anchorTransform.localScale = Vector3.one;
    }

    /// <summary>Instantiates catalog prefab mesh or a placeholder under the anchor.</summary>
    private void EnsureHeldVisual()
    {
        if (_anchor == null || _heldVisualRoot != null)
            return;

        ItemPrefabCatalog catalog = ItemPrefabCatalog.Load();
        if (catalog != null &&
            catalog.TryGetPrefab(InventoryItemType.PaintballGun, out WorldInventoryItem prefab) &&
            prefab != null)
        {
            GameObject instance = Instantiate(prefab.gameObject, _anchor.transform);
            instance.name = "HeldPaintballGunMesh";
            StripWorldItemComponents(instance);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            _heldVisualRoot = instance.transform;
            return;
        }

        Transform placeholder = PaintballGunVisualFactory.EnsurePlaceholderVisual(_anchor.transform);
        if (placeholder != null)
        {
            placeholder.localScale = Vector3.one * 0.9f;
            _heldVisualRoot = placeholder;
        }
    }

    /// <summary>Removes pickup, collider, and rigidbody components from a held visual instance.</summary>
    private static void StripWorldItemComponents(GameObject root)
    {
        WorldInventoryItem worldItem = root.GetComponent<WorldInventoryItem>();
        if (worldItem != null)
            Destroy(worldItem);

        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                Destroy(colliders[i]);
        }

        Rigidbody[] bodies = root.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < bodies.Length; i++)
        {
            if (bodies[i] != null)
                Destroy(bodies[i]);
        }
    }

    /// <summary>Shows or hides the held gun based on equip state.</summary>
    private void RefreshPresentation()
    {
        if (_anchor == null)
            return;

        _anchor.SetActive(_equipped && isActiveAndEnabled);
    }
}
