using UnityEngine;

/// <summary>Creates a hidden SteamBootstrap before the first scene loads.</summary>
internal static class SteamBootstrapRuntime
{
    /// <summary>Spawns SteamBootstrap when none exists in the loaded scenes.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void EnsureSteamBootstrap()
    {
        if (Object.FindAnyObjectByType<SteamBootstrap>() != null)
            return;

        GameObject go = new GameObject("SteamBootstrap");
        go.hideFlags = HideFlags.DontSaveInBuild | HideFlags.DontSaveInEditor;
        go.AddComponent<SteamBootstrap>();
    }
}
