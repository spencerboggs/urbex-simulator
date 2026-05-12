using UnityEngine;

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

    private Camera _gameplayCamera;
    private GameObject _anchor;
    private Light _spotLight;
    private bool _equipped;

    private void Awake()
    {
        _gameplayCamera = GetComponentInChildren<Camera>(true);
        EnsureAnchor();
        RefreshVisibility();
    }

    private void OnDisable()
    {
        if (_anchor != null)
            _anchor.SetActive(false);
    }

    public void SetEquipped(bool equipped)
    {
        _equipped = equipped;
        EnsureAnchor();
        RefreshVisibility();
    }

    private void EnsureAnchor()
    {
        if (_gameplayCamera == null)
            _gameplayCamera = GetComponentInChildren<Camera>(true);

        if (_gameplayCamera == null)
            return;

        if (_anchor == null)
        {
            _anchor = new GameObject("__HeldFlashlight");
            _anchor.transform.SetParent(_gameplayCamera.transform, false);
            _spotLight = _anchor.AddComponent<Light>();
            _spotLight.type = LightType.Spot;
            _spotLight.shadows = LightShadows.None;

            Transform visual = FlashlightVisualFactory.EnsurePlaceholderVisual(_anchor.transform);
            if (visual != null)
                visual.localScale = new Vector3(0.9f, 0.9f, 0.9f);
        }

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

    private void RefreshVisibility()
    {
        if (_anchor == null)
            return;

        _anchor.SetActive(_equipped && isActiveAndEnabled);
    }
}
