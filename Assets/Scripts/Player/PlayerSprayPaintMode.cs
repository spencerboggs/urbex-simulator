using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Crosshair presentation modes for the gameplay HUD.
/// </summary>
public enum CrosshairMode
{
    /// <summary>Small centered dot.</summary>
    Dot = 0,

    /// <summary>Open circle when spray paint can hit the looked-at surface.</summary>
    SprayInRange = 1,

    /// <summary>X when spray paint is equipped but the target is out of range or blocked.</summary>
    SprayOutOfRange = 2,
}

/// <summary>
/// Held spray paint can that paints surfaces while primary use is held.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerSprayPaintMode : MonoBehaviour
{
    [Header("Spray")]
    [Tooltip("Hex color for spray marks (for example #FF0000).")]
    [SerializeField]
    private string _hexColor = "#FF0000";

    [Tooltip("Diameter of each spray mark in world units.")]
    [SerializeField]
    [Min(0.02f)]
    private float _spraySize = 0.18f;

    [Tooltip("Minimum spray diameter when scrolling size down.")]
    [SerializeField]
    [Min(0.02f)]
    private float _minSpraySize = 0.06f;

    [Tooltip("Maximum spray diameter when scrolling size up.")]
    [SerializeField]
    [Min(0.02f)]
    private float _maxSpraySize = 0.45f;

    [Tooltip("Spray diameter change per scroll wheel notch while equipped.")]
    [SerializeField]
    [Min(0.005f)]
    private float _scrollSizePerNotch = 0.055f;

    [Tooltip("Maximum raycast distance from the camera to paint a surface.")]
    [SerializeField]
    [Min(0.1f)]
    private float _maxDistance = 3.5f;

    [Tooltip("Stroke sample spacing as a fraction of current spray diameter.")]
    [SerializeField]
    [Range(0.05f, 0.35f)]
    private float _strokeSpacingSprayFraction = 0.11f;

    [Tooltip("Peak opacity contributed by each spray mark layer (lower = slower buildup to solid).")]
    [SerializeField]
    [Range(0.02f, 1f)]
    private float _markLayerOpacity = 0.14f;

    [Header("Crosshair")]
    [Tooltip("Smallest spray circle crosshair size in pixels.")]
    [SerializeField]
    [Min(4f)]
    private float _minCrosshairCirclePixels = 10f;

    [Tooltip("Largest spray circle crosshair size in pixels.")]
    [SerializeField]
    [Min(4f)]
    private float _maxCrosshairCirclePixels = 30f;

    [Header("Placement")]
    [SerializeField]
    private Vector3 _localPosition = new(0.28f, -0.12f, 0.42f);

    [SerializeField]
    private Vector3 _localEulerAngles = new(0f, -18f, 12f);

    /// <summary>Gameplay camera used for spray raycasts.</summary>
    private Camera _gameplayCamera;
    /// <summary>Parent object for the held can visual.</summary>
    private GameObject _anchor;
    /// <summary>Instantiated held mesh root.</summary>
    private Transform _heldVisualRoot;
    /// <summary>Whether the spray paint hotbar slot is selected.</summary>
    private bool _equipped;
    /// <summary>Last world point where a mark was successfully placed.</summary>
    private Vector3 _lastPlacedPoint;
    /// <summary>Collider receiving the active stroke.</summary>
    private Collider _lastPlacedCollider;
    /// <summary>Whether at least one mark exists in the current stroke.</summary>
    private bool _hasLastPlaced;

    /// <summary>Whether spray paint is selected in the hotbar.</summary>
    public bool IsEquipped => _equipped;

    /// <summary>Current spray color parsed from <see cref="_hexColor"/>.</summary>
    public Color SprayColor => HexColorUtility.ParseOrDefault(_hexColor, Color.red);

    /// <summary>World diameter of each spray mark.</summary>
    public float SpraySize => _spraySize;

    /// <summary>Minimum scroll-adjustable spray diameter.</summary>
    public float MinSpraySize => _minSpraySize;

    /// <summary>Maximum scroll-adjustable spray diameter.</summary>
    public float MaxSpraySize => _maxSpraySize;

    /// <summary>Minimum crosshair circle size in pixels.</summary>
    public float MinCrosshairCirclePixels => _minCrosshairCirclePixels;

    /// <summary>Maximum crosshair circle size in pixels.</summary>
    public float MaxCrosshairCirclePixels => _maxCrosshairCirclePixels;

    /// <summary>Maximum paint raycast distance.</summary>
    public float MaxDistance => _maxDistance;

    /// <summary>Validates hex color and spray size limits in the editor.</summary>
    private void OnValidate()
    {
        if (!HexColorUtility.TryParse(_hexColor, out _))
            _hexColor = "#FF0000";

        if (_maxSpraySize < _minSpraySize)
            _maxSpraySize = _minSpraySize;

        _spraySize = Mathf.Clamp(_spraySize, _minSpraySize, _maxSpraySize);

        if (_maxCrosshairCirclePixels < _minCrosshairCirclePixels)
            _maxCrosshairCirclePixels = _minCrosshairCirclePixels;
    }

    /// <summary>Resolves gameplay camera and builds the held can anchor.</summary>
    private void Awake()
    {
        _gameplayCamera = GetComponentInChildren<Camera>(true);
        _spraySize = Mathf.Clamp(_spraySize, _minSpraySize, _maxSpraySize);
        EnsureAnchor();
        RefreshPresentation();
    }

    /// <summary>Hides the anchor when this component is disabled.</summary>
    private void OnDisable()
    {
        if (_anchor != null)
            _anchor.SetActive(false);
    }

    /// <summary>Adjusts spray size from scroll wheel and paints while primary use is held.</summary>
    private void Update()
    {
        if (!_equipped || !isActiveAndEnabled)
            return;

        HandleScrollSizeAdjust();

        if (!KeybindManager.IsPressed(KeybindAction.ItemPrimaryUse))
        {
            _hasLastPlaced = false;
            return;
        }

        if (!TryGetPaintTarget(out RaycastHit hit))
        {
            _hasLastPlaced = false;
            return;
        }

        ContinueStroke(hit);
    }

    /// <summary>Returns the crosshair mode implied by equip state and the current paint raycast.</summary>
    public CrosshairMode GetCrosshairMode()
    {
        if (!_equipped || !isActiveAndEnabled)
            return CrosshairMode.Dot;

        return TryGetPaintTarget(out _)
            ? CrosshairMode.SprayInRange
            : CrosshairMode.SprayOutOfRange;
    }

    /// <summary>Equips or stows the held spray paint visual.</summary>
    public void SetEquipped(bool equipped)
    {
        _equipped = equipped;
        _hasLastPlaced = false;

        if (_equipped)
            _ = SprayPaintBrush.BrushTexture;

        EnsureAnchor();
        RefreshPresentation();
    }

    /// <summary>Changes spray diameter while equipped using the scroll wheel.</summary>
    private void HandleScrollSizeAdjust()
    {
        if (Mouse.current == null)
            return;

        float scrollY = Mouse.current.scroll.ReadValue().y;
        if (Mathf.Abs(scrollY) < 0.01f)
            return;

        float delta = Mathf.Sign(scrollY) * _scrollSizePerNotch;
        _spraySize = Mathf.Clamp(
            _spraySize + delta,
            _minSpraySize,
            _maxSpraySize);
    }

    /// <summary>Spacing between stroke samples based on the active spray diameter.</summary>
    private float GetStrokeStep()
    {
        return Mathf.Max(0.006f, _spraySize * _strokeSpacingSprayFraction);
    }

    /// <summary>Fills marks from the last placed point to the current target each frame.</summary>
    private void ContinueStroke(RaycastHit hit)
    {
        if (!_hasLastPlaced)
        {
            if (TryPlaceMark(hit, out Vector3 placedPoint))
            {
                _lastPlacedPoint = placedPoint;
                _lastPlacedCollider = hit.collider;
                _hasLastPlaced = true;
            }

            return;
        }

        if (_lastPlacedCollider != hit.collider)
        {
            _hasLastPlaced = false;
            ContinueStroke(hit);
            return;
        }

        float step = GetStrokeStep();
        float distance = Vector3.Distance(_lastPlacedPoint, hit.point);

        if (distance > _maxDistance * 0.65f)
        {
            _hasLastPlaced = false;
            ContinueStroke(hit);
            return;
        }

        if (distance <= step * 0.25f)
        {
            if (TryPlaceMark(hit, out Vector3 placedPoint))
                _lastPlacedPoint = placedPoint;
            return;
        }

        int steps = Mathf.Max(1, Mathf.CeilToInt(distance / step));
        for (int i = 1; i <= steps; i++)
        {
            float t = i / (float)steps;
            Vector3 samplePoint = Vector3.Lerp(_lastPlacedPoint, hit.point, t);
            if (TryPlaceMark(samplePoint, hit.normal, hit.collider, out Vector3 placedPoint))
                _lastPlacedPoint = placedPoint;
        }
    }

    /// <summary>Raycasts from the camera and returns the first valid paint target.</summary>
    private bool TryGetPaintTarget(out RaycastHit hit)
    {
        if (_gameplayCamera == null)
            _gameplayCamera = GetComponentInChildren<Camera>(true);

        if (!SprayPaintRaycast.TryGetTarget(_gameplayCamera, transform, _maxDistance, out hit))
            return false;

        if (!SprayPaintRaycast.IsBrushFootprintOnSurface(hit.point, hit.normal, hit.collider, _spraySize))
            return false;

        return true;
    }

    /// <summary>Places a single mark from a raycast hit when the footprint is valid.</summary>
    private bool TryPlaceMark(RaycastHit hit, out Vector3 placedPoint)
    {
        return TryPlaceMark(hit.point, hit.normal, hit.collider, out placedPoint);
    }

    /// <summary>Re-samples, validates, and places a mark at an approximate surface point.</summary>
    private bool TryPlaceMark(Vector3 approximatePoint, Vector3 normal, Collider collider, out Vector3 placedPoint)
    {
        placedPoint = approximatePoint;
        if (!SprayPaintRaycast.TryResolvePointOnCollider(approximatePoint, normal, collider, out RaycastHit resolved))
            return false;

        if (!SprayPaintRaycast.IsBrushFootprintOnSurface(resolved.point, resolved.normal, collider, _spraySize))
            return false;

        PaintableSurface surface = PaintableSurface.GetOrCreate(collider);
        if (surface == null)
            return false;

        if (!surface.AddMark(resolved.point, resolved.normal, SprayColor, _spraySize, _markLayerOpacity))
            return false;

        placedPoint = resolved.point;
        return true;
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
            _anchor = new GameObject("__HeldSprayPaint");
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
            catalog.TryGetPrefab(InventoryItemType.SprayPaint, out WorldInventoryItem prefab) &&
            prefab != null)
        {
            GameObject instance = Instantiate(prefab.gameObject, _anchor.transform);
            instance.name = "HeldSprayPaintMesh";
            StripWorldItemComponents(instance);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            _heldVisualRoot = instance.transform;
            return;
        }

        Transform placeholder = SprayPaintVisualFactory.EnsurePlaceholderVisual(_anchor.transform);
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

    /// <summary>Shows or hides the held can based on equip state.</summary>
    private void RefreshPresentation()
    {
        if (_anchor == null)
            return;

        _anchor.SetActive(_equipped && isActiveAndEnabled);
    }
}
