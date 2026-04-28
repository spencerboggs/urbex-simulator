using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
public sealed class PlayerHardBody : MonoBehaviour
{
    [Tooltip("Optional: if set, the hard body child is placed on this layer (0-31). -1 leaves it unchanged.")]
    [SerializeField]
    private int _hardBodyLayer = -1;

    private CharacterController _cc;
    private Rigidbody _rb;
    private CapsuleCollider _capsule;

    private void Awake()
    {
        _cc = GetComponent<CharacterController>();
        EnsureHardBodyObjects();
        SyncFromCharacterController();
    }

    private void OnEnable()
    {
        EnsureHardBodyObjects();
        SyncFromCharacterController();
    }

    private void LateUpdate()
    {
        // Keep the blocking capsule in sync with crouch height/center changes
        SyncFromCharacterController();
    }

    private void EnsureHardBodyObjects()
    {
        if (_capsule != null && _rb != null)
            return;

        Transform existing = transform.Find("__HardBody");
        GameObject go = existing != null ? existing.gameObject : new GameObject("__HardBody");
        if (existing == null)
            go.transform.SetParent(transform, false);

        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        if (_hardBodyLayer >= 0 && _hardBodyLayer <= 31)
            go.layer = _hardBodyLayer;

        _capsule = go.GetComponent<CapsuleCollider>();
        if (_capsule == null)
            _capsule = go.AddComponent<CapsuleCollider>();

        _capsule.direction = 1; // Y axis
        _capsule.isTrigger = false;

        _rb = go.GetComponent<Rigidbody>();
        if (_rb == null)
            _rb = go.AddComponent<Rigidbody>();

        _rb.isKinematic = true;
        _rb.useGravity = false;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        _rb.constraints = RigidbodyConstraints.FreezeRotation;

        // Ensure our own CharacterController isn't blocked by its own hard body
        if (_cc != null && _capsule != null)
            Physics.IgnoreCollision(_cc, _capsule, true);
    }

    private void SyncFromCharacterController()
    {
        if (_cc == null || _capsule == null)
            return;

        _capsule.center = _cc.center;
        _capsule.radius = Mathf.Max(0.05f, _cc.radius);
        _capsule.height = Mathf.Max(_capsule.radius * 2f + 0.01f, _cc.height);
    }
}

