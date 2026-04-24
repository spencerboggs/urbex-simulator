using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

[DisallowMultipleComponent]
public sealed class CameraViewfinderUI : MonoBehaviour
{
    private UIDocument _document;
    private Label _stampLabel;
    private VisualElement _shutterFlash;
    private VisualElement _savedToast;
    private Label _savedToastPath;
    private Label _hintCapture;
    private Label _hintExit;
    private VisualElement _recDot;
    private IVisualElementScheduledItem _tickSchedule;
    private float _toastHideTime;
    private Coroutine _shutterFlashRoutine;

    private void Awake()
    {
        _document = GetComponent<UIDocument>();
    }

    private void OnEnable()
    {
        VisualElement root = _document != null ? _document.rootVisualElement : null;
        if (root == null)
            return;

        _stampLabel = root.Q<Label>("stampLabel");
        _shutterFlash = root.Q<VisualElement>("shutterFlash");
        _savedToast = root.Q<VisualElement>("savedToast");
        _savedToastPath = root.Q<Label>("savedToastPath");
        _hintCapture = root.Q<Label>("hintCapture");
        _hintExit = root.Q<Label>("hintExit");
        _recDot = root.Q<VisualElement>("recDot");

        _tickSchedule = root.schedule.Execute(TickUi).Every(250);
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
        if (_savedToast != null && Time.unscaledTime >= _toastHideTime && _savedToast.ClassListContains("visible"))
        {
            _savedToast.RemoveFromClassList("visible");
        }
    }

    private void TickUi()
    {
        if (_stampLabel != null)
            _stampLabel.text = DateTime.Now.ToString("yyyy-MM-dd  HH:mm:ss");

        if (_recDot != null)
            _recDot.ToggleInClassList("recBlinkOn");
    }

    public void SetControlHints(string captureText, string stowKeyDisplay)
    {
        if (_hintCapture != null)
            _hintCapture.text = captureText;
        if (_hintExit != null)
            _hintExit.text = string.IsNullOrEmpty(stowKeyDisplay) ? "Stow camera" : $"{stowKeyDisplay}  Stow";
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
        _shutterFlash.RemoveFromClassList("flashFade");
        _shutterFlash.AddToClassList("flashActive");
        yield return new WaitForSecondsRealtime(0.05f);
        _shutterFlash.RemoveFromClassList("flashActive");
        _shutterFlash.AddToClassList("flashFade");
        _shutterFlashRoutine = null;
    }

    public void ShowSavedToast(string absolutePath, float visibleSeconds = 2.2f)
    {
        if (_savedToast == null || _savedToastPath == null)
            return;

        _savedToastPath.text = absolutePath ?? string.Empty;
        _savedToast.AddToClassList("visible");
        _toastHideTime = Time.unscaledTime + Mathf.Max(0.5f, visibleSeconds);
    }
}
