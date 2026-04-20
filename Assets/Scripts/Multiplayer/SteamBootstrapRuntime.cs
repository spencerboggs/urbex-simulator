using UnityEngine;

internal static class SteamBootstrapRuntime
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void EnsureSteamBootstrap()
    {
        // Avoid duplicates when domain reload is disabled or when entering play mode repeatedly
        if (Object.FindAnyObjectByType<SteamBootstrap>() != null)
            return;

        GameObject go = new GameObject("SteamBootstrap");
        go.hideFlags = HideFlags.DontSaveInBuild | HideFlags.DontSaveInEditor;
        go.AddComponent<SteamBootstrap>();
    }
}

