using FishNet.Component.Spawning;
using FishNet.Managing;
using FishNet.Managing.Scened;
using FishNet.Object;
using UnityEngine;

public static class NetworkSceneFlow
{
    public const string MainMenu = "MainMenu";
    public const string Lobby = "Lobby";

    // Default map used when a session has no selection yet (e.g. legacy code paths
    // calling LoadWorld). Kept for back-compat; new code should always go through
    // LoadMap with a name resolved from the MapCatalog
    public const string DefaultMap = "World";

    // Loads the lobby for every connection and future joiners (global scene)
    public static void LoadLobby(NetworkManager networkManager)
    {
        if (networkManager == null || !networkManager.IsServerStarted)
            return;

        SceneLoadData data = new(Lobby)
        {
            ReplaceScenes = ReplaceOption.All,
        };
        networkManager.SceneManager.LoadGlobalScenes(data);
    }

    // Host-only progression into a specific gameplay map. Assigns the player
    // prefab on PlayerSpawner (if provided) so spawning works for late-joiners
    // too, then global-loads the scene so all connected clients receive it
    //
    // Caller must guarantee `sceneName` is in EditorBuildSettings - for runtime
    // safety, code that picks the map should resolve it via MapCatalog (which is
    // kept in sync with the build settings by MapCatalogAutoSync)
    public static void LoadMap(NetworkManager networkManager, string sceneName, NetworkObject playerPrefab)
    {
        if (networkManager == null || !networkManager.IsServerStarted)
            return;

        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogWarning("[Net] LoadMap called with empty scene name; aborting.");
            return;
        }

        if (networkManager.TryGetComponent(out PlayerSpawner spawner) && playerPrefab != null)
            spawner.SetPlayerPrefab(playerPrefab);

        SceneLoadData data = new(sceneName)
        {
            ReplaceScenes = ReplaceOption.All,
        };
        networkManager.SceneManager.LoadGlobalScenes(data);
    }

    // Legacy shim - equivalent to LoadMap with the DefaultMap scene. Existing
    // callers that haven't migrated to selecting a specific map keep working
    public static void LoadWorld(NetworkManager networkManager, NetworkObject playerPrefab)
    {
        LoadMap(networkManager, DefaultMap, playerPrefab);
    }

    // Convenience: true for any scene name that represents in-game play (i.e.
    // not the menu or lobby). Used to recognize gameplay-load completion
    // regardless of which map was loaded
    public static bool IsGameplayScene(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return false;
        return sceneName != MainMenu && sceneName != Lobby;
    }
}
