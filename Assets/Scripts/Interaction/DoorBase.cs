using UnityEngine;

// Abstract base for all door types. Implements IInteractable + an open/close state
// machine over a normalized progress value (0 = closed, 1 = open). Subclasses only
// have to translate that progress into a visual (rotation, slide, etc.) by
// overriding ApplyProgress
//
// The progress value is hard-clamped to [0, 1] every frame so a slow framerate or
// huge deltaTime spike can never cause the door to over-shoot past closed/open
//
// State is one-way at any given moment: a door is either Closed, Opening, Open, or
// Closing. Interacting while Opening flips to Closing (and vice-versa) so a player
// mid-animation can immediately reverse course without breaking the clamp
[DisallowMultipleComponent]
public abstract class DoorBase : MonoBehaviour, IInteractable
{
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

    // Normalized progress: 0 = fully closed, 1 = fully open. Always clamped
    private float _progress;
    private DoorState _state;

    public DoorState State => _state;
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

            // Hard clamp so frame spikes can't push progress past the endpoints,
            // then snap into the matching terminal state
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

    // Hook for subclasses: 0 means render the closed pose, 1 means the open pose,
    // anything in between is the interpolated frame. Called every Update while the
    // door is animating, plus once during Awake to set the initial pose
    protected abstract void ApplyProgress(float progress01);

    // Flips the door between open and closed. Mid-animation toggles reverse the
    // direction without losing progress, so spamming the key never desyncs the
    // visual from the state
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

    public virtual void Open()
    {
        if (_state == DoorState.Open || _state == DoorState.Opening)
            return;
        _state = DoorState.Opening;
    }

    public virtual void Close()
    {
        if (_state == DoorState.Closed || _state == DoorState.Closing)
            return;
        _state = DoorState.Closing;
    }

    // ---- IInteractable ----

    public virtual bool CanInteract(Transform interactor) => _interactionEnabled;

    public virtual string GetInteractionPrompt()
    {
        if (!_interactionEnabled)
            return string.Empty;

        // While animating, anticipate the result so the prompt doesn't flicker
        // (a half-open door says "Close" if it's currently opening - pressing E
        // will reverse it to closing)
        bool willCloseNext = _state == DoorState.Open || _state == DoorState.Opening;
        return willCloseNext ? _closePrompt : _openPrompt;
    }

    public virtual void Interact(Transform interactor)
    {
        if (!_interactionEnabled)
            return;
        Toggle();
    }
}
