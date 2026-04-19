using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// Builds a minimal default Canvas + controls when scenes have no authored UI. Disable by wiring your own UI in the inspector
public static class MultiplayerUiRuntimeBuilder
{
    private const float ColumnWidth = 460f;
    private const float ButtonHeight = 56f;
    private const float InputHeight = 52f;
    private const int ButtonFontSize = 22;
    private const int InputFontSize = 20;
    private const int LobbyTitleFontSize = 36;
    private const int LobbySubtitleFontSize = 22;
    private const int LobbyBodyFontSize = 20;

    private static Sprite _uiSprite;

    // 1×1 white sprite so Image draws reliably
    private static Sprite UiSprite
    {
        get
        {
            if (_uiSprite != null)
                return _uiSprite;

            var tex = Texture2D.whiteTexture;
            _uiSprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            return _uiSprite;
        }
    }

    private static Font DefaultUiFont()
    {
        // Built-in Resources font paths vary by Unity version and often return null on Unity 6+.
        // OS dynamic fonts are the most reliable for runtime-generated uGUI Text.
        string[] osNames = { "Arial", "Segoe UI", "Helvetica", "Liberation Sans", "Noto Sans", "DejaVu Sans" };
        foreach (string name in osNames)
        {
            var f = Font.CreateDynamicFontFromOSFont(name, 18);
            if (f != null)
                return f;
        }

        Font builtin = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (builtin != null)
            return builtin;

        builtin = Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (builtin != null)
            return builtin;

        string[] installed = Font.GetOSInstalledFontNames();
        if (installed != null && installed.Length > 0)
        {
            var fallback = Font.CreateDynamicFontFromOSFont(installed[0], 18);
            if (fallback != null)
                return fallback;
        }

        Debug.LogWarning("MultiplayerUiRuntimeBuilder: could not resolve a UI font; menu text may be invisible.");
        return null;
    }

    private static void StyleImage(Image img)
    {
        img.sprite = UiSprite;
        img.type = Image.Type.Simple;
        img.preserveAspect = false;
    }

    private static void AddLayoutElement(GameObject go, float height, float fixedColumnWidth = -1f)
    {
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = height;
        le.preferredHeight = height;
        if (fixedColumnWidth > 0f)
        {
            le.minWidth = fixedColumnWidth;
            le.preferredWidth = fixedColumnWidth;
            le.flexibleWidth = 0f;
        }
        else
        {
            le.flexibleWidth = 1f;
        }
    }

    public static void EnsureEventSystem()
    {
        if (Object.FindAnyObjectByType<EventSystem>() != null)
            return;

        var es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        // Project uses "Input System Package" only
        // StandaloneInputModule uses legacy UnityEngine.Input
        es.AddComponent<InputSystemUIInputModule>();
    }

    // Menu/lobby scenes copied from the level often have no Camera
    // The Game view then shows "No cameras rendering" and UI may not appear correctly.
    public static void EnsureSceneCamera()
    {
        if (Object.FindAnyObjectByType<Camera>() != null)
            return;

        var camGo = new GameObject("MenuCamera");
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.06f, 0.06f, 0.08f, 1f);
        cam.orthographic = true;
        cam.orthographicSize = 5f;
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = 100f;
        cam.transform.position = new Vector3(0f, 0f, -10f);
        camGo.tag = "MainCamera";
    }

    private static void EnsureCanvasFillsScreen(GameObject canvasRoot)
    {
        var rect = canvasRoot.GetComponent<RectTransform>();
        if (rect == null)
            return;

        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.anchoredPosition = Vector2.zero;
        rect.localScale = Vector3.one;
    }

    public static void BuildMainMenu(MainMenuUI menu)
    {
        if (menu == null)
            return;

        EnsureSceneCamera();
        EnsureEventSystem();

        var canvasGo = new GameObject("MainMenuCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();
        EnsureCanvasFillsScreen(canvasGo);

        var main = CreatePanel(canvasGo.transform, "MainPanel");
        var play = CreatePanel(canvasGo.transform, "PlayPanel");

        var playBtn = CreateButton(main.transform, "PlayButton", "Play");
        var quitBtn = CreateButton(main.transform, "QuitButton", "Quit");

        var hostBtn = CreateButton(play.transform, "HostButton", "Host");
        var joinBtn = CreateButton(play.transform, "JoinButton", "Join");

        var inputGo = new GameObject("JoinAddress", typeof(RectTransform));
        inputGo.transform.SetParent(play.transform, false);
        var inputRect = inputGo.GetComponent<RectTransform>();
        inputRect.anchorMin = new Vector2(0.5f, 1f);
        inputRect.anchorMax = new Vector2(0.5f, 1f);
        inputRect.pivot = new Vector2(0.5f, 1f);
        inputRect.sizeDelta = new Vector2(ColumnWidth, InputHeight);
        AddLayoutElement(inputGo, InputHeight, ColumnWidth);
        var inputBg = inputGo.AddComponent<Image>();
        StyleImage(inputBg);
        inputBg.color = new Color(0.15f, 0.15f, 0.15f, 1f);

        var input = inputGo.AddComponent<InputField>();
        var backBtn = CreateButton(play.transform, "BackButton", "Back");
        var inputTextGo = new GameObject("Text");
        inputTextGo.transform.SetParent(inputGo.transform, false);
        var inputText = inputTextGo.AddComponent<Text>();
        inputText.font = DefaultUiFont();
        inputText.fontSize = InputFontSize;
        inputText.color = Color.white;
        inputText.text = "localhost";
        inputText.horizontalOverflow = HorizontalWrapMode.Overflow;
        inputText.verticalOverflow = VerticalWrapMode.Truncate;
        var itr = inputText.GetComponent<RectTransform>();
        itr.anchorMin = Vector2.zero;
        itr.anchorMax = Vector2.one;
        itr.offsetMin = new Vector2(8f, 4f);
        itr.offsetMax = new Vector2(-8f, -4f);
        input.textComponent = inputText;
        input.targetGraphic = inputBg;
        input.lineType = InputField.LineType.SingleLine;

        play.SetActive(false);

        menu.ApplyRuntimeReferences(main, play, input, playBtn, quitBtn, hostBtn, joinBtn, backBtn);
    }

    public static void BuildLobby(LobbyUI lobby)
    {
        if (lobby == null)
            return;

        EnsureSceneCamera();
        EnsureEventSystem();

        var canvasGo = new GameObject("LobbyCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();
        EnsureCanvasFillsScreen(canvasGo);

        var root = CreatePanel(canvasGo.transform, "LobbyPanel");

        var titleGo = new GameObject("Title");
        titleGo.transform.SetParent(root.transform, false);
        var title = titleGo.AddComponent<Text>();
        title.font = DefaultUiFont();
        title.fontSize = LobbyTitleFontSize;
        title.fontStyle = FontStyle.Bold;
        title.color = new Color(0.96f, 0.97f, 1f, 1f);
        title.text = "Lobby";
        title.alignment = TextAnchor.MiddleCenter;
        var titleRect = titleGo.GetComponent<RectTransform>();
        titleRect.sizeDelta = new Vector2(ColumnWidth, 56f);
        AddLayoutElement(titleGo, 58f, ColumnWidth);

        var countGo = new GameObject("PlayerCount");
        countGo.transform.SetParent(root.transform, false);
        var countText = countGo.AddComponent<Text>();
        countText.font = DefaultUiFont();
        countText.fontSize = LobbySubtitleFontSize;
        countText.color = new Color(0.85f, 0.88f, 0.95f, 1f);
        countText.text = "Players: --";
        countText.alignment = TextAnchor.MiddleCenter;
        var countRect = countGo.GetComponent<RectTransform>();
        countRect.sizeDelta = new Vector2(ColumnWidth, 40f);
        AddLayoutElement(countGo, 42f, ColumnWidth);

        var mapGo = new GameObject("MapLabel");
        mapGo.transform.SetParent(root.transform, false);
        var mapLabel = mapGo.AddComponent<Text>();
        mapLabel.font = DefaultUiFont();
        mapLabel.fontSize = LobbyBodyFontSize;
        mapLabel.color = new Color(0.75f, 0.78f, 0.88f, 1f);
        mapLabel.text = "Map: Level (default)";
        mapLabel.alignment = TextAnchor.MiddleCenter;
        mapGo.GetComponent<RectTransform>().sizeDelta = new Vector2(ColumnWidth, 34f);
        AddLayoutElement(mapGo, 38f, ColumnWidth);

        var mapNext = CreateButton(root.transform, "MapNext", "Next map");
        var startBtn = CreateButton(root.transform, "StartMatch", "Start match (host)");
        var leaveBtn = CreateButton(root.transform, "Leave", "Leave lobby");

        lobby.ApplyRuntimeReferences(countText, mapLabel, mapNext, startBtn, leaveBtn);
    }

    private static GameObject CreatePanel(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rect = go.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        var img = go.AddComponent<Image>();
        StyleImage(img);
        img.color = new Color(0.07f, 0.08f, 0.12f, 0.94f);
        var layout = go.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.spacing = 18f;
        layout.padding = new RectOffset(56, 56, 56, 56);
        // With childControlHeight true, Text with a bad/missing font reports 0 preferred height (controls collapse to invisible).
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        // Let buttons/inputs use their preferred width so they stay a centered column, not edge-to-edge.
        layout.childForceExpandWidth = false;
        return go;
    }

    private static Button CreateButton(Transform parent, string name, string label)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(ColumnWidth, ButtonHeight);
        AddLayoutElement(go, ButtonHeight, ColumnWidth);

        var img = go.AddComponent<Image>();
        StyleImage(img);
        img.color = new Color(0.22f, 0.42f, 0.72f, 1f);
        var btn = go.AddComponent<Button>();
        var colors = btn.colors;
        colors.normalColor = new Color(0.22f, 0.42f, 0.72f, 1f);
        colors.highlightedColor = new Color(0.32f, 0.52f, 0.88f, 1f);
        colors.pressedColor = new Color(0.16f, 0.30f, 0.55f, 1f);
        colors.disabledColor = new Color(0.25f, 0.25f, 0.28f, 0.65f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        btn.colors = colors;
        btn.navigation = new Navigation { mode = Navigation.Mode.None };

        var textGo = new GameObject("Text");
        textGo.transform.SetParent(go.transform, false);
        var text = textGo.AddComponent<Text>();
        text.font = DefaultUiFont();
        text.fontSize = ButtonFontSize;
        text.fontStyle = FontStyle.Bold;
        text.color = new Color(0.98f, 0.99f, 1f, 1f);
        text.text = label;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.raycastTarget = false;
        var tr = text.GetComponent<RectTransform>();
        tr.anchorMin = Vector2.zero;
        tr.anchorMax = Vector2.one;
        tr.offsetMin = new Vector2(16f, 0f);
        tr.offsetMax = new Vector2(-16f, 0f);
        return btn;
    }
}
