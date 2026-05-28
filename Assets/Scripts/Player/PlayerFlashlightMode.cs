using UnityEngine;

/// <summary>
/// Held flashlight mesh and spot light parented to the gameplay camera.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerFlashlightMode : MonoBehaviour
{
    [Header("Placement")]
    [SerializeField]
    private Vector3 _localPosition = new(0f, -0.1f, 0.4f);

    [SerializeField]
    private Vector3 _localEulerAngles = new(2f, 0f, 0f);

    [Header("Light")]
    [SerializeField]
    private Color _lightColor = new(1f, 0.96f, 0.88f, 1f);

    [SerializeField]
    [Min(0.1f)]
    private float _lightRange = 18f;

    [SerializeField]
    [Min(0f)]
    private float _lightIntensity = 12f;

    [SerializeField]
    [Range(5f, 140f)]
    private float _spotAngle = 78f;

    [SerializeField]
    [Range(1f, 140f)]
    private float _innerSpotAngle = 56f;

    /// <summary>Child gameplay camera used as parent for the held flashlight anchor.</summary>
    private Camera _gameplayCamera;
    /// <summary>Child object parented to the camera holding mesh and spot light.</summary>
    private GameObject _anchor;
    /// <summary>Spot light on the anchor, toggled when flashlight is on.</summary>
    private Light _spotLight;
    /// <summary>Instantiated held mesh root (catalog prefab or placeholder).</summary>
    private Transform _heldVisualRoot;
    /// <summary>Whether the flashlight hotbar slot is currently selected.</summary>
    private bool _equipped;
    /// <summary>Whether the spotlight is enabled while equipped.</summary>
    private bool _isOn;

    /// <summary>Whether a flashlight is selected in the hotbar.</summary>
    public bool IsEquipped => _equipped;

    /// <summary>Whether the spotlight is enabled.</summary>
    public bool IsOn => _isOn;

    /// <summary>Resolves gameplay camera and builds the flashlight anchor hierarchy.</summary>
    private void Awake()
    {
        _gameplayCamera = GetComponentInChildren<Camera>(true);
        _isOn = false;
        EnsureAnchor();
        RefreshPresentation();
    }

    /// <summary>Hides the anchor when this component is disabled.</summary>
    private void OnDisable()
    {
        if (_anchor != null)
            _anchor.SetActive(false);
    }

    /// <summary>Handles primary-use key to toggle the spotlight while equipped.</summary>
    private void Update()
    {
        if (!_equipped || !isActiveAndEnabled)
            return;

        if (!KeybindManager.WasPressedThisFrame(KeybindAction.ItemPrimaryUse))
            return;

        Toggle();
    }

    /// <summary>Equips or stows the held flashlight visual and light anchor.</summary>
    public void SetEquipped(bool equipped)
    {
        _equipped = equipped;
        EnsureAnchor();
        RefreshPresentation();
    }

    /// <summary>Toggles the spotlight on or off.</summary>
    public void Toggle()
    {
        if (!_equipped)
            return;

        _isOn = !_isOn;
        RefreshPresentation();
    }

    /// <summary>Sets spotlight on or off explicitly.</summary>
    public void SetOn(bool on)
    {
        _isOn = on;
        RefreshPresentation();
    }

    /// <summary>Creates or updates the camera-parented anchor, mesh, and spot light settings.</summary>
    private void EnsureAnchor()
    {
        if (_gameplayCamera == null)
            _gameplayCamera = GetComponentInChildren<Camera>(true);

        if (_gameplayCamera == null)
            return;

        // Lazy-create anchor and spot light on first use.
        if (_anchor == null)
        {
            _anchor = new GameObject("__HeldFlashlight");
            _anchor.transform.SetParent(_gameplayCamera.transform, false);
            _spotLight = _anchor.AddComponent<Light>();
            _spotLight.type = LightType.Spot;
            _spotLight.shadows = LightShadows.None;
        }

        EnsureHeldVisual();

        Transform anchorTransform = _anchor.transform;
        anchorTransform.localPosition = _localPosition;
        anchorTransform.localRotation = Quaternion.Euler(_localEulerAngles);
        anchorTransform.localScale = Vector3.one;

        if (_spotLight != null)
        {
            _spotLight.color = _lightColor;
            _spotLight.range = _lightRange;
            _spotLight.intensity = _lightIntensity;
            _spotLight.spotAngle = _spotAngle;
            _spotLight.innerSpotAngle = Mathf.Min(_innerSpotAngle, _spotAngle);
        }
    }

    /// <summary>Instantiates catalog prefab mesh or a placeholder under the anchor.</summary>
    // Reuses the world-drop prefab so held and dropped flashlights match.
    private void EnsureHeldVisual()
    {
        if (_anchor == null || _heldVisualRoot != null)
            return;

        Transform legacy = _anchor.transform.Find("__FlashlightVisual");
        if (legacy != null)
            Destroy(legacy.gameObject);

        // Prefer world-drop prefab so held and dropped flashlights match.
        ItemPrefabCatalog catalog = ItemPrefabCatalog.Load();
        if (catalog != null &&
            catalog.TryGetPrefab(InventoryItemType.Flashlight, out WorldInventoryItem prefab) &&
            prefab != null)
        {
            GameObject instance = Instantiate(prefab.gameObject, _anchor.transform);
            instance.name = "HeldFlashlightMesh";
            StripWorldItemComponents(instance);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            _heldVisualRoot = instance.transform;
            return;
        }

        Transform placeholder = FlashlightVisualFactory.EnsurePlaceholderVisual(_anchor.transform);
        if (placeholder != null)
        {
            placeholder.localScale = new Vector3(0.9f, 0.9f, 0.9f);
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

    /// <summary>Shows or hides the anchor and enables the spot light based on equip and on state.</summary>
    private void RefreshPresentation()
    {
        if (_anchor == null)
            return;

        bool showHeld = _equipped && isActiveAndEnabled;
        _anchor.SetActive(showHeld);

        if (_spotLight != null)
            _spotLight.enabled = showHeld && _isOn;
    }
}
