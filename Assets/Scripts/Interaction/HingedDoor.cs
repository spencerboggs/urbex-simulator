using UnityEngine;

// Standard hinged door. A thin "hinge" transform sits at the pivot edge and the
// visual door mesh is parented underneath it so rotating the hinge swings the door.
// One-way: the configured open angle defines the swing direction; the door only
// ever interpolates between closed and that angle, never past either end
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
        {
            // Allow the script to be placed directly on the hinge object too.
            _hinge = transform;
        }
        base.Awake();
    }

    protected override void ApplyProgress(float progress01)
    {
        if (_hinge == null)
            return;

        // Quaternion.Slerp gives a smooth, gimbal-safe interpolation between the
        // two configured Euler poses. Progress is already clamped by DoorBase, so
        // we can't overshoot the endpoints.
        Quaternion closed = Quaternion.Euler(_closedLocalEuler);
        Quaternion open = Quaternion.Euler(_openLocalEuler);
        _hinge.localRotation = Quaternion.Slerp(closed, open, progress01);
    }

    // Snap to the closed pose immediately in the editor when the inspector changes
    // so designers can preview the closed/open angles without entering play mode.
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
