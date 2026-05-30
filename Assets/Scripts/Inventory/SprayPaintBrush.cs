using UnityEngine;

/// <summary>
/// Shared brush texture, stamp shader, and canvas resources for runtime spray paint.
/// </summary>
public static class SprayPaintBrush
{
    private const int BrushTextureSize = 64;

    private static Texture2D s_brushTexture;
    private static Material s_stampMaterial;
    private static Material s_canvasMaterialTemplate;
    private static Mesh s_unitQuadMesh;
    private static bool s_initialized;
    private static bool s_loggedInitFailure;

    /// <summary>Whether shared spray paint GPU resources initialized successfully.</summary>
    public static bool IsReady => s_initialized;

    /// <summary>Soft circular alpha texture for spray stamps.</summary>
    public static Texture2D BrushTexture
    {
        get
        {
            EnsureInitialized();
            return s_brushTexture;
        }
    }

    /// <summary>Unit quad mesh used for paint canvas overlays.</summary>
    public static Mesh UnitQuadMesh
    {
        get
        {
            EnsureInitialized();
            return s_unitQuadMesh;
        }
    }

    /// <summary>
    /// Creates a display material that shows an accumulated paint render texture.
    /// </summary>
    public static Material CreateCanvasMaterial(RenderTexture paintTexture)
    {
        if (!EnsureInitialized() || s_canvasMaterialTemplate == null)
            return null;

        Material instance = new Material(s_canvasMaterialTemplate);
        instance.mainTexture = paintTexture;
        return instance;
    }

    /// <summary>
    /// Stamps the brush onto a paint canvas render texture using standard alpha blending.
    /// </summary>
    public static bool BlitStamp(
        RenderTexture source,
        RenderTexture destination,
        Vector2 centerUv,
        Vector2 halfExtentUv,
        Color tint)
    {
        if (source == null || destination == null)
            return false;

        if (!EnsureInitialized() || s_stampMaterial == null)
            return false;

        s_stampMaterial.SetVector("_StampData", new Vector4(centerUv.x, centerUv.y, halfExtentUv.x, halfExtentUv.y));
        s_stampMaterial.SetColor("_Color", tint);
        Graphics.Blit(source, destination, s_stampMaterial);
        return true;
    }

    /// <summary>
    /// Builds shared brush, stamp, and canvas resources once at runtime.
    /// </summary>
    private static bool EnsureInitialized()
    {
        if (s_initialized)
            return true;

        s_brushTexture = CreateSoftCircleTexture(BrushTextureSize);
        s_unitQuadMesh = CreateUnitQuadMesh();

        Shader stampShader = LoadShader("Urbex/SprayPaintStamp");
        Shader canvasShader = LoadShader("Urbex/SprayPaintCanvas");

        if (stampShader == null || canvasShader == null)
        {
            if (!s_loggedInitFailure)
            {
                Debug.LogError("[SprayPaintBrush] Failed to load spray paint shaders from Resources/Urbex.");
                s_loggedInitFailure = true;
            }

            return false;
        }

        s_stampMaterial = new Material(stampShader)
        {
            hideFlags = HideFlags.HideAndDontSave,
        };
        s_stampMaterial.SetTexture("_BrushTex", s_brushTexture);

        s_canvasMaterialTemplate = new Material(canvasShader)
        {
            hideFlags = HideFlags.HideAndDontSave,
        };

        s_initialized = true;
        return true;
    }

    /// <summary>
    /// Loads a shader from Resources with a Shader.Find fallback.
    /// </summary>
    private static Shader LoadShader(string shaderName)
    {
        Shader shader = Shader.Find(shaderName);
        if (shader != null)
            return shader;

        return Resources.Load<Shader>(shaderName);
    }

    /// <summary>
    /// Generates a radial falloff texture for soft spray edges.
    /// </summary>
    private static Texture2D CreateSoftCircleTexture(int size)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            name = "SprayPaintBrush",
        };

        float center = (size - 1) * 0.5f;
        float radius = size * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                float alpha = Mathf.Clamp01(1f - distance / radius);
                alpha *= alpha;
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        texture.Apply();
        return texture;
    }

    /// <summary>
    /// Creates a front-facing unit quad centered at the origin.
    /// </summary>
    private static Mesh CreateUnitQuadMesh()
    {
        Mesh mesh = new Mesh
        {
            name = "SprayPaintUnitQuad",
        };

        mesh.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3(0.5f, -0.5f, 0f),
            new Vector3(0.5f, 0.5f, 0f),
            new Vector3(-0.5f, 0.5f, 0f),
        };

        mesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, 1f),
        };

        mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
