using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>UI Toolkit viewfinder overlay for handheld camera mode.</summary>
[DisallowMultipleComponent]
public sealed class CameraViewfinderUI : MonoBehaviour
{
    [Header("Fallback")]
    [Tooltip("Optional. If the sibling UIDocument's visualTreeAsset reference is " +
             "null at runtime (e.g. after a failed asset import), this UXML is " +
             "assigned to the UIDocument so the UI still renders.")]
    [SerializeField]
    private VisualTreeAsset _viewfinderUxmlFallback;

    /// <summary>Sibling UIDocument on this GameObject.</summary>
    private UIDocument _document;

    /// <summary>Date/time stamp in the viewfinder corner.</summary>
    private Label _stampLabel;

    /// <summary>Full-screen white flash on capture.</summary>
    private VisualElement _shutterFlash;

    /// <summary>Saved path toast container.</summary>
    private VisualElement _savedToast;

    /// <summary>Absolute path text inside the saved toast.</summary>
    private Label _savedToastPath;

    /// <summary>Capture control hint label.</summary>
    private Label _hintCapture;

    /// <summary>Stow camera hint label.</summary>
    private Label _hintExit;

    /// <summary>Zoom bar fill width driven by normalized zoom.</summary>
    private VisualElement _zoomFill;

    /// <summary>Scheduled task that updates the date stamp periodically.</summary>
    private IVisualElementScheduledItem _tickSchedule;

    /// <summary>Unscaled time when the saved toast should hide.</summary>
    private float _toastHideTime;

    /// <summary>Running shutter flash fade coroutine, if any.</summary>
    private Coroutine _shutterFlashRoutine;

    /// <summary>Caches the UIDocument reference.</summary>
    private void Awake()
    {
        _document = GetComponent<UIDocument>();
    }

    /// <summary>Binds UXML, applies fallback asset, and starts stamp scheduling.</summary>
    private void OnEnable()
    {
        // Recover when the assigned UXML reference was lost at import time.
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

        root.style.position = Position.Absolute;
        root.style.left = 0;
        root.style.top = 0;
        root.style.right = 0;
        root.style.bottom = 0;

        BindElements(root);

        _tickSchedule = root.schedule.Execute(TickStamp).Every(250);

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

    /// <summary>Queries named visual elements from the viewfinder UXML root.</summary>
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

    /// <summary>Logs UXML binding failure details when viewfinderFrame is missing.</summary>
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

    /// <summary>Comma-separated child names for diagnostic logging.</summary>
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

    /// <summary>Pauses stamp schedule and stops shutter flash coroutine.</summary>
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

    /// <summary>Hides the saved toast after its visibility timeout.</summary>
    private void Update()
    {
        if (_savedToast != null &&
            Time.unscaledTime >= _toastHideTime &&
            _savedToast.ClassListContains("savedToastVisible"))
        {
            _savedToast.RemoveFromClassList("savedToastVisible");
        }
    }

    /// <summary>Updates the corner date/time stamp label.</summary>
    private void TickStamp()
    {
        if (_stampLabel != null)
            _stampLabel.text = DateTime.Now.ToString("yyyy-MM-dd  HH:mm:ss");
    }

    /// <summary>Updates capture and stow hint labels.</summary>
    public void SetControlHints(string captureText, string stowKeyDisplay)
    {
        if (_hintCapture != null)
            _hintCapture.text = captureText;
        if (_hintExit != null)
            _hintExit.text = string.IsNullOrEmpty(stowKeyDisplay) ? "Stow camera" : $"{stowKeyDisplay}  Stow";
    }

    /// <summary>Sets zoom bar fill (0 = wide, 1 = tele).</summary>
    public void SetZoomLevel(float normalized)
    {
        if (_zoomFill == null)
            return;
        float t = Mathf.Clamp01(normalized);
        _zoomFill.style.width = Length.Percent(t * 100f);
    }

    /// <summary>Plays the shutter flash overlay.</summary>
    public void PlayShutterEffect()
    {
        if (_shutterFlash == null)
            return;

        if (_shutterFlashRoutine != null)
            StopCoroutine(_shutterFlashRoutine);
        _shutterFlashRoutine = StartCoroutine(ShutterFlashRoutine());
    }

    /// <summary>Brief full-opacity flash then fade on the shutter overlay.</summary>
    private IEnumerator ShutterFlashRoutine()
    {
        // Inline opacity avoids USS transition issues on Unity 6.
        _shutterFlash.style.opacity = 0.92f;
        yield return new WaitForSecondsRealtime(0.05f);
        _shutterFlash.style.opacity = 0f;
        _shutterFlashRoutine = null;
    }

    /// <summary>Shows the saved-path toast for <paramref name="visibleSeconds"/>.</summary>
    public void ShowSavedToast(string absolutePath, float visibleSeconds = 2.2f)
    {
        if (_savedToast == null || _savedToastPath == null)
            return;

        _savedToastPath.text = absolutePath ?? string.Empty;
        _savedToast.AddToClassList("savedToastVisible");
        _toastHideTime = Time.unscaledTime + Mathf.Max(0.5f, visibleSeconds);
    }
}
