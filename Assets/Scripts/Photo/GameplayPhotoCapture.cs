using System.IO;
using UnityEngine;

public static class GameplayPhotoCapture
{
    // Renders the camera to PNG bytes
    // World pass only
    // UI Toolkit is not included in this render
    public static byte[] CaptureToPng(Camera camera, int width, int height)
    {
        if (camera == null)
            return null;

        width = Mathf.Clamp(width, 64, 8192);
        height = Mathf.Clamp(height, 64, 8192);

        RenderTextureDescriptor desc = new(width, height, RenderTextureFormat.ARGB32, 24)
        {
            msaaSamples = 1,
            sRGB = true,
        };

        RenderTexture rt = RenderTexture.GetTemporary(desc);
        RenderTexture previousTarget = camera.targetTexture;
        CameraClearFlags previousClear = camera.clearFlags;

        try
        {
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.targetTexture = rt;
            camera.Render();

            RenderTexture.active = rt;
            Texture2D tex = new(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            byte[] png = tex.EncodeToPNG();
            UnityEngine.Object.Destroy(tex);
            return png;
        }
        finally
        {
            camera.targetTexture = previousTarget;
            camera.clearFlags = previousClear;
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);
        }
    }

    public static bool SavePngBytes(byte[] png, string absolutePath)
    {
        if (png == null || png.Length == 0 || string.IsNullOrEmpty(absolutePath))
            return false;
        string dir = Path.GetDirectoryName(absolutePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllBytes(absolutePath, png);
        return true;
    }
}
