using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

[DisallowMultipleComponent]
public sealed class CameraViewfinderUI : MonoBehaviour
{
    [Header("Fallback")]
    [Tooltip("Optional. If the sibling UIDocument's visualTreeAsset reference is " +
             "null at runtime (e.g. after a failed asset import), this UXML is " +
             "assigned to the UIDocument so the UI still renders.")]
    [SerializeField]
    private VisualTreeAsset _viewfinderUxmlFallback;

    private UIDocument _document;
    private Label _stampLabel;
    private VisualElement _shutterFlash;
    private VisualElement _savedToast;
    private Label _savedToastPath;
    private Label _hintCapture;
    private Label _hintExit;
    private VisualElement _zoomFill;
    private IVisualElementScheduledItem _tickSchedule;
    private float _toastHideTime;
    private Coroutine _shutterFlashRoutine;

    private void Awake()
    {
        _document = GetComponent<UIDocument>();
    }

    private void OnEnable()
    {
        // Recover from broken/half-imported visualTreeAsset references on the
        // sibling UIDocument by re-assigning the inspector-provided fallback
        if (_document != null &&
            _document.visualTreeAsset == null &&
            _viewfinderUxmlFallback != null)
        {
            Debug.LogWarning(
                "[CameraViewfinderUI] UIDocument.visualTreeAsset was null; assigning the fallback UXML serialized on this component.");
            _document.visualTreeAsset = _viewfinderUxmlFallback;
        }

        VisualElement root = _document != null ? _document.rootVisualElement : null;
        if (root == null)
        {
            Debug.LogWarning("[CameraViewfinderUI] UIDocument.rootVisualElement is null; UXML did not bind. Check UIDocument.sourceAsset and PanelSettings.");
            return;
        }

        // Belt-and-suspenders: force the panel root to span its parent so children
        // anchored with insets always have a real-sized box to compute against
        root.style.position = Position.Absolute;
        root.style.left = 0;
        root.style.top = 0;
        root.style.right = 0;
        root.style.bottom = 0;

        BindElements(root);

        _tickSchedule = root.schedule.Execute(TickStamp).Every(250);

        // If the visual tree wasn't built yet at OnEnable time (UIDocument can
        // build lazily), retry once on the next frame before warning
        if (root.Q<VisualElement>("viewfinderFrame") == null)
        {
            root.schedule.Execute(() =>
            {
                if (_document == null)
                    return;
                VisualElement r = _document.rootVisualElement;
                if (r == null)
                    return;
                BindElements(r);
                if (r.Q<VisualElement>("viewfinderFrame") == null)
                    LogMissingFrameDiagnostics(r);
            }).StartingIn(0);
        }
    }

    private void BindElements(VisualElement root)
    {
        _stampLabel = root.Q<Label>("stampLabel");
        _shutterFlash = root.Q<VisualElement>("shutterFlash");
        _savedToast = root.Q<VisualElement>("savedToast");
        _savedToastPath = root.Q<Label>("savedToastPath");
        _hintCapture = root.Q<Label>("hintCapture");
        _hintExit = root.Q<Label>("hintExit");
        _zoomFill = root.Q<VisualElement>("zoomFill");
    }

    private void LogMissingFrameDiagnostics(VisualElement root)
    {
        string visibleAssetName = _document != null && _document.visualTreeAsset != null
            ? _document.visualTreeAsset.name
            : "<null>";
        string childDump = DescribeChildren(root);
        Debug.LogWarning(
            $"[CameraViewfinderUI] 'viewfinderFrame' still not found after a deferred retry.\n" +
            $"  UIDocument.visualTreeAsset = '{visibleAssetName}'\n" +
            $"  rootVisualElement childCount = {root.childCount}\n" +
            $"  children = {childDump}\n" +
            $"  If the dump shows old element names (e.g. viewfinderRoot), Unity has a stale " +
            $".uxml in its artifact cache. Close the editor, delete the Library/Artifacts and " +
            $"Library/ArtifactDB folders in the project root, then reopen Unity to fully rebuild the asset cache.");
    }

    private static string DescribeChildren(VisualElement root)
    {
        if (root == null || root.childCount == 0)
            return "<empty>";
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int i = 0; i < root.childCount; i++)
        {
            VisualElement child = root[i];
            if (i > 0) sb.Append(", ");
            sb.Append(string.IsNullOrEmpty(child.name) ? $"<{child.GetType().Name}>" : child.name);
        }
        return sb.ToString();
    }

    private void OnDisable()
    {
        if (_tickSchedule != null)
        {
            _tickSchedule.Pause();
            _tickSchedule = null;
        }

        if (_shutterFlashRoutine != null)
        {
            StopCoroutine(_shutterFlashRoutine);
            _shutterFlashRoutine = null;
        }
    }

    private void Update()
    {
        if (_savedToast != null &&
            Time.unscaledTime >= _toastHideTime &&
            _savedToast.ClassListContains("savedToastVisible"))
        {
            _savedToast.RemoveFromClassList("savedToastVisible");
        }
    }

    private void TickStamp()
    {
        if (_stampLabel != null)
            _stampLabel.text = DateTime.Now.ToString("yyyy-MM-dd  HH:mm:ss");
    }

    public void SetControlHints(string captureText, string stowKeyDisplay)
    {
        if (_hintCapture != null)
            _hintCapture.text = captureText;
        if (_hintExit != null)
            _hintExit.text = string.IsNullOrEmpty(stowKeyDisplay) ? "Stow camera" : $"{stowKeyDisplay}  Stow";
    }

    // Sets the zoom-bar fill width. 0 = wide (no fill), 1 = full tele
    public void SetZoomLevel(float normalized)
    {
        if (_zoomFill == null)
            return;
        float t = Mathf.Clamp01(normalized);
        _zoomFill.style.width = Length.Percent(t * 100f);
    }

    public void PlayShutterEffect()
    {
        if (_shutterFlash == null)
            return;

        if (_shutterFlashRoutine != null)
            StopCoroutine(_shutterFlashRoutine);
        _shutterFlashRoutine = StartCoroutine(ShutterFlashRoutine());
    }

    private IEnumerator ShutterFlashRoutine()
    {
        // Drive opacity directly via inline style instead of relying on USS
        // transitions, which can crash Unity 6's style applier
        _shutterFlash.style.opacity = 0.92f;
        yield return new WaitForSecondsRealtime(0.05f);
        _shutterFlash.style.opacity = 0f;
        _shutterFlashRoutine = null;
    }

    public void ShowSavedToast(string absolutePath, float visibleSeconds = 2.2f)
    {
        if (_savedToast == null || _savedToastPath == null)
            return;

        _savedToastPath.text = absolutePath ?? string.Empty;
        _savedToast.AddToClassList("savedToastVisible");
        _toastHideTime = Time.unscaledTime + Mathf.Max(0.5f, visibleSeconds);
    }
}
