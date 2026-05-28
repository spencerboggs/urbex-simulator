using UnityEngine;

/// <summary>
/// Base door with normalized open progress (0 = closed, 1 = open). Subclasses implement
/// <see cref="ApplyProgress"/> for the visual pose. Progress is clamped each frame.
/// </summary>
[DisallowMultipleComponent]
public abstract class DoorBase : MonoBehaviour, IInteractable
{
    /// <summary>Door motion state.</summary>
    public enum DoorState
    {
        Closed,
        Opening,
        Open,
        Closing,
    }

    [Header("Door")]
    [Tooltip("State the door starts the scene in.")]
    [SerializeField]
    protected DoorState _initialState = DoorState.Closed;

    [Tooltip("How fast the door moves between fully closed and fully open, in units per second of normalized progress. 1.0 = open/close takes one second; 2.0 = half a second.")]
    [SerializeField]
    [Min(0.05f)]
    protected float _animationSpeed = 1.5f;

    [Header("Prompts")]
    [Tooltip("Text shown when the door can be opened.")]
    [SerializeField]
    protected string _openPrompt = "Open door";

    [Tooltip("Text shown when the door can be closed.")]
    [SerializeField]
    protected string _closePrompt = "Close door";

    [Tooltip("When false, the door can't be interacted with (e.g. locked door, scripted sequence). Prompt is suppressed.")]
    [SerializeField]
    protected bool _interactionEnabled = true;

    /// <summary>Normalized open amount in [0, 1], driven by <see cref="Update"/> while animating.</summary>
    private float _progress;

    /// <summary>Current door state machine value.</summary>
    private DoorState _state;

    /// <summary>Current state machine state.</summary>
    public DoorState State => _state;

    /// <summary>Normalized open amount in [0, 1].</summary>
    public float Progress => _progress;

    protected virtual void Awake()
    {
        _state = _initialState;
        _progress = (_state == DoorState.Open || _state == DoorState.Opening) ? 1f : 0f;
        ApplyProgress(_progress);
    }

    protected virtual void Update()
    {
        if (_state == DoorState.Opening || _state == DoorState.Closing)
        {
            float direction = _state == DoorState.Opening ? 1f : -1f;
            float next = _progress + direction * _animationSpeed * Time.deltaTime;

            // Snap to endpoints and transition state when progress crosses fully open or closed.
            if (next >= 1f)
            {
                _progress = 1f;
                _state = DoorState.Open;
            }
            else if (next <= 0f)
            {
                _progress = 0f;
                _state = DoorState.Closed;
            }
            else
            {
                _progress = next;
            }

            ApplyProgress(_progress);
        }
    }

    /// <summary>Applies visual pose for normalized progress (0 = closed, 1 = open).</summary>
    protected abstract void ApplyProgress(float progress01);

    /// <summary>Toggles open/closed; mid-animation input reverses direction.</summary>
    public virtual void Toggle()
    {
        switch (_state)
        {
            case DoorState.Closed:
            case DoorState.Closing:
                _state = DoorState.Opening;
                break;
            case DoorState.Open:
            case DoorState.Opening:
                _state = DoorState.Closing;
                break;
        }
    }

    /// <summary>Starts opening if not already open or opening.</summary>
    public virtual void Open()
    {
        if (_state == DoorState.Open || _state == DoorState.Opening)
            return;
        _state = DoorState.Opening;
    }

    /// <summary>Starts closing if not already closed or closing.</summary>
    public virtual void Close()
    {
        if (_state == DoorState.Closed || _state == DoorState.Closing)
            return;
        _state = DoorState.Closing;
    }

    /// <inheritdoc />
    public virtual bool CanInteract(Transform interactor) => _interactionEnabled;

    /// <inheritdoc />
    public virtual string GetInteractionPrompt()
    {
        if (!_interactionEnabled)
            return string.Empty;

        // Open or mid-open means the next interact will start closing.
        bool willCloseNext = _state == DoorState.Open || _state == DoorState.Opening;
        return willCloseNext ? _closePrompt : _openPrompt;
    }

    /// <inheritdoc />
    public virtual void Interact(Transform interactor)
    {
        if (!_interactionEnabled)
            return;
        Toggle();
    }
}
