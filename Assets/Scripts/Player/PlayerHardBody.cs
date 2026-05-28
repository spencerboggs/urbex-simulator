using UnityEngine;

/// <summary>
/// Kinematic capsule collider that mirrors the CharacterController for external physics blocking.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
public sealed class PlayerHardBody : MonoBehaviour
{
    [Tooltip("Optional: if set, the hard body child is placed on this layer (0-31). -1 leaves it unchanged.")]
    [SerializeField]
    private int _hardBodyLayer = -1;

    /// <summary>Source CharacterController whose dimensions drive the hard body.</summary>
    private CharacterController _cc;
    /// <summary>Kinematic rigidbody on the hard body child for physics queries.</summary>
    private Rigidbody _rb;
    /// <summary>Non-trigger capsule on the hard body child, ignored against the controller.</summary>
    private CapsuleCollider _capsule;

    /// <summary>Creates hard body child objects and performs an initial sync.</summary>
    private void Awake()
    {
        _cc = GetComponent<CharacterController>();
        EnsureHardBodyObjects();
        SyncFromCharacterController();
    }

    /// <summary>Ensures hard body exists when re-enabled after disable.</summary>
    private void OnEnable()
    {
        EnsureHardBodyObjects();
        SyncFromCharacterController();
    }

    /// <summary>Keeps hard body capsule aligned with CharacterController each frame.</summary>
    private void LateUpdate()
    {
        SyncFromCharacterController();
    }

    /// <summary>Creates or finds __HardBody child with capsule, rigidbody, and layer setup.</summary>
    private void EnsureHardBodyObjects()
    {
        if (_capsule != null && _rb != null)
            return;

        // Find or create the hard body child under the player root.
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

        _capsule.direction = 1;
        _capsule.isTrigger = false;

        _rb = go.GetComponent<Rigidbody>();
        if (_rb == null)
            _rb = go.AddComponent<Rigidbody>();

        _rb.isKinematic = true;
        _rb.useGravity = false;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        _rb.constraints = RigidbodyConstraints.FreezeRotation;

        if (_cc != null && _capsule != null)
            Physics.IgnoreCollision(_cc, _capsule, true);
    }

    /// <summary>Copies center, radius, and height from CharacterController to the hard body capsule.</summary>
    private void SyncFromCharacterController()
    {
        if (_cc == null || _capsule == null)
            return;

        _capsule.center = _cc.center;
        _capsule.radius = Mathf.Max(0.05f, _cc.radius);
        _capsule.height = Mathf.Max(_capsule.radius * 2f + 0.01f, _cc.height);
    }
}
