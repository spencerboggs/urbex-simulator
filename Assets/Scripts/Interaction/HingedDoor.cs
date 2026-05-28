using UnityEngine;

/// <summary>
/// Hinged door that rotates a pivot transform between closed and open local Euler angles.
/// </summary>
[AddComponentMenu("Interaction/Doors/Hinged Door")]
public sealed class HingedDoor : DoorBase
{
    [Header("Hinge")]
    [Tooltip("Transform used as the pivot. Its localRotation is animated. The door visual should be a child of this transform.")]
    [SerializeField]
    private Transform _hinge;

    [Tooltip("Local Euler angles of the hinge when the door is fully closed.")]
    [SerializeField]
    private Vector3 _closedLocalEuler = Vector3.zero;

    [Tooltip("Local Euler angles of the hinge when the door is fully open. This defines the swing direction - use a single non-zero axis (e.g. (0,90,0) for a Y-axis swing). Use a negative value to swing the other way.")]
    [SerializeField]
    private Vector3 _openLocalEuler = new Vector3(0f, 90f, 0f);

    protected override void Awake()
    {
        if (_hinge == null)
            _hinge = transform;
        base.Awake();
    }

    protected override void ApplyProgress(float progress01)
    {
        if (_hinge == null)
            return;

        // Slerp between authored closed and open local rotations for smooth swing motion.
        Quaternion closed = Quaternion.Euler(_closedLocalEuler);
        Quaternion open = Quaternion.Euler(_openLocalEuler);
        _hinge.localRotation = Quaternion.Slerp(closed, open, progress01);
    }

    /// <summary>Previews hinge rotation in the editor when closed/open angles or initial state change.</summary>
    private void OnValidate()
    {
        if (_hinge == null || !Application.isEditor || Application.isPlaying)
            return;

        Quaternion closed = Quaternion.Euler(_closedLocalEuler);
        Quaternion open = Quaternion.Euler(_openLocalEuler);
        bool startsOpen = _initialState == DoorState.Open || _initialState == DoorState.Opening;
        _hinge.localRotation = startsOpen ? open : closed;
    }
}
