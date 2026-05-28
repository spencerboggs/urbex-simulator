using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>Builds default Canvas UI when menu/lobby scenes have no authored layout.</summary>
public static class MultiplayerUiRuntimeBuilder
{
    private const float UiScale = 2f;

    private const float ColumnWidth = 460f * UiScale;
    private const float ButtonHeight = 56f * UiScale;
    private const float InputHeight = 52f * UiScale;
    private const int ButtonFontSize = (int)(22 * UiScale);
    private const int InputFontSize = (int)(20 * UiScale);
    private const int LobbyTitleFontSize = (int)(36 * UiScale);
    private const int LobbySubtitleFontSize = (int)(22 * UiScale);
    private const int LobbyBodyFontSize = (int)(20 * UiScale);

    /// <summary>Shared white sprite for runtime-generated Image backgrounds.</summary>
    private static Sprite _uiSprite;

    /// <summary>Lazy white sprite used by StyleImage for buttons and panels.</summary>
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

    /// <summary>Resolves a readable UI font from OS, builtin, or installed fallbacks.</summary>
    private static Font DefaultUiFont()
    {
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

    /// <summary>Applies the shared white sprite and simple Image settings.</summary>
    private static void StyleImage(Image img)
    {
        img.sprite = UiSprite;
        img.type = Image.Type.Simple;
        img.preserveAspect = false;
    }

    /// <summary>Adds LayoutElement height and optional fixed column width for vertical groups.</summary>
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

    /// <summary>Creates an EventSystem with Input System UI module if missing.</summary>
    public static void EnsureEventSystem()
    {
        if (Object.FindAnyObjectByType<EventSystem>() != null)
            return;

        var es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        es.AddComponent<InputSystemUIInputModule>();
    }

    /// <summary>Adds a menu camera when the scene has none (avoids empty Game view).</summary>
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

    /// <summary>Stretches the root RectTransform to full screen for overlay canvases.</summary>
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

    /// <summary>Builds the default main menu hierarchy on <paramref name="menu"/>.</summary>
    public static void BuildMainMenu(MainMenuUI menu)
    {
        if (menu == null)
            return;

        EnsureSceneCamera();
        EnsureEventSystem();

        // Screen-space overlay with scaled reference resolution.
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

        var hostBtn = CreateButton(play.transform, "HostButton", "Host Game");
        var dividerA = CreateHorizontalDivider(play.transform, "DividerA");

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
        inputBg.color = new Color(0.78f, 0.78f, 0.78f, 1f);

        var input = inputGo.AddComponent<InputField>();
        var joinBtn = CreateButton(play.transform, "JoinButton", "Join Game");
        var dividerB = CreateHorizontalDivider(play.transform, "DividerB");
        var backBtn = CreateButton(play.transform, "BackButton", "Back");

        var placeholderGo = new GameObject("Placeholder");
        placeholderGo.transform.SetParent(inputGo.transform, false);
        var placeholder = placeholderGo.AddComponent<Text>();
        placeholder.font = DefaultUiFont();
        placeholder.fontSize = InputFontSize;
        placeholder.fontStyle = FontStyle.Italic;
        placeholder.color = new Color(0.35f, 0.35f, 0.35f, 1f);
        placeholder.text = "Host SteamID";
        placeholder.alignment = TextAnchor.MiddleCenter;
        placeholder.horizontalOverflow = HorizontalWrapMode.Overflow;
        placeholder.verticalOverflow = VerticalWrapMode.Truncate;
        var phtr = placeholder.GetComponent<RectTransform>();
        phtr.anchorMin = Vector2.zero;
        phtr.anchorMax = Vector2.one;
        phtr.offsetMin = new Vector2(8f * UiScale, 4f * UiScale);
        phtr.offsetMax = new Vector2(-8f * UiScale, -4f * UiScale);

        var inputTextGo = new GameObject("Text");
        inputTextGo.transform.SetParent(inputGo.transform, false);
        var inputText = inputTextGo.AddComponent<Text>();
        inputText.font = DefaultUiFont();
        inputText.fontSize = InputFontSize;
        inputText.color = Color.black;
        inputText.text = string.Empty;
        inputText.alignment = TextAnchor.MiddleCenter;
        inputText.horizontalOverflow = HorizontalWrapMode.Overflow;
        inputText.verticalOverflow = VerticalWrapMode.Truncate;
        var itr = inputText.GetComponent<RectTransform>();
        itr.anchorMin = Vector2.zero;
        itr.anchorMax = Vector2.one;
        itr.offsetMin = new Vector2(8f * UiScale, 4f * UiScale);
        itr.offsetMax = new Vector2(-8f * UiScale, -4f * UiScale);

        input.placeholder = placeholder;
        input.textComponent = inputText;
        input.targetGraphic = inputBg;
        input.lineType = InputField.LineType.SingleLine;

        play.SetActive(false);

        menu.ApplyRuntimeReferences(main, play, input, playBtn, quitBtn, hostBtn, joinBtn, backBtn);
    }

    /// <summary>Builds the default lobby hierarchy on <paramref name="lobby"/>.</summary>
    public static void BuildLobby(LobbyUI lobby)
    {
        if (lobby == null)
            return;

        EnsureSceneCamera();
        EnsureEventSystem();

        // Lobby overlay: title, counts, map label, and host actions.
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
        titleRect.sizeDelta = new Vector2(ColumnWidth, 56f * UiScale);
        AddLayoutElement(titleGo, 58f * UiScale, ColumnWidth);

        var countGo = new GameObject("PlayerCount");
        countGo.transform.SetParent(root.transform, false);
        var countText = countGo.AddComponent<Text>();
        countText.font = DefaultUiFont();
        countText.fontSize = LobbySubtitleFontSize;
        countText.color = new Color(0.85f, 0.88f, 0.95f, 1f);
        countText.text = "Players: --";
        countText.alignment = TextAnchor.MiddleCenter;
        var countRect = countGo.GetComponent<RectTransform>();
        countRect.sizeDelta = new Vector2(ColumnWidth, 40f * UiScale);
        AddLayoutElement(countGo, 42f * UiScale, ColumnWidth);

        var mapGo = new GameObject("MapLabel");
        mapGo.transform.SetParent(root.transform, false);
        var mapLabel = mapGo.AddComponent<Text>();
        mapLabel.font = DefaultUiFont();
        mapLabel.fontSize = LobbyBodyFontSize;
        mapLabel.color = new Color(0.75f, 0.78f, 0.88f, 1f);
        mapLabel.text = "Map: Level (default)";
        mapLabel.alignment = TextAnchor.MiddleCenter;
        mapGo.GetComponent<RectTransform>().sizeDelta = new Vector2(ColumnWidth, 34f * UiScale);
        AddLayoutElement(mapGo, 38f * UiScale, ColumnWidth);

        var mapNext = CreateButton(root.transform, "MapNext", "Next Map");
        var startBtn = CreateButton(root.transform, "StartMatch", "Start Match (Host)");
        var leaveBtn = CreateButton(root.transform, "Leave", "Leave Lobby");

        lobby.ApplyRuntimeReferences(countText, mapLabel, mapNext, startBtn, leaveBtn);
    }

    /// <summary>Full-screen black panel with centered vertical layout.</summary>
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
        img.color = Color.black;
        var layout = go.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.spacing = 18f * UiScale;
        layout.padding = new RectOffset((int)(56 * UiScale), (int)(56 * UiScale), (int)(56 * UiScale), (int)(56 * UiScale));
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = false;
        return go;
    }

    /// <summary>Styled button with label text child for runtime menus.</summary>
    private static Button CreateButton(Transform parent, string name, string label)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(ColumnWidth, ButtonHeight);
        AddLayoutElement(go, ButtonHeight, ColumnWidth);

        var img = go.AddComponent<Image>();
        StyleImage(img);
        img.color = new Color(0.92f, 0.92f, 0.90f, 1f);
        var btn = go.AddComponent<Button>();
        var colors = btn.colors;
        colors.normalColor = new Color(0.92f, 0.92f, 0.90f, 1f);
        colors.highlightedColor = new Color(0.98f, 0.98f, 0.96f, 1f);
        colors.pressedColor = new Color(0.82f, 0.82f, 0.80f, 1f);
        colors.disabledColor = new Color(0.70f, 0.70f, 0.70f, 1f);
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
        text.color = Color.black;
        text.text = label;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.raycastTarget = false;
        var tr = text.GetComponent<RectTransform>();
        tr.anchorMin = Vector2.zero;
        tr.anchorMax = Vector2.one;
        tr.offsetMin = new Vector2(16f * UiScale, 0f);
        tr.offsetMax = new Vector2(-16f * UiScale, 0f);
        return btn;
    }

    /// <summary>Thin horizontal rule between play submenu sections.</summary>
    private static GameObject CreateHorizontalDivider(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);

        var rect = go.AddComponent<RectTransform>();
        float w = ColumnWidth * 0.92f;
        float h = 6f * UiScale;
        rect.sizeDelta = new Vector2(w, h);
        AddLayoutElement(go, 10f * UiScale, w);

        var img = go.AddComponent<Image>();
        StyleImage(img);
        img.color = new Color(0.35f, 0.35f, 0.35f, 1f);
        return go;
    }
}
