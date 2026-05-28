using FishNet.Component.Spawning;
using FishNet.Managing;
using FishNet.Managing.Scened;
using FishNet.Object;
using UnityEngine;

/// <summary>FishNet global scene load helpers for menu, lobby, and gameplay maps.</summary>
public static class NetworkSceneFlow
{
    /// <summary>Main menu scene name.</summary>
    public const string MainMenu = "MainMenu";

    /// <summary>Lobby scene name.</summary>
    public const string Lobby = "Lobby";

    /// <summary>Legacy default gameplay map when no catalog selection exists.</summary>
    public const string DefaultMap = "World";

    /// <summary>Loads the lobby for all connections, replacing existing global scenes.</summary>
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

    /// <summary>
    /// Host-only load of a gameplay map. Assigns the player prefab on <see cref="PlayerSpawner"/> when provided.
    /// <paramref name="sceneName"/> must be in EditorBuildSettings (kept in sync via MapCatalogAutoSync).
    /// </summary>
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

    /// <summary>Loads <see cref="DefaultMap"/> (legacy callers).</summary>
    public static void LoadWorld(NetworkManager networkManager, NetworkObject playerPrefab)
    {
        LoadMap(networkManager, DefaultMap, playerPrefab);
    }

    /// <summary>True when <paramref name="sceneName"/> is a gameplay map (not menu or lobby).</summary>
    public static bool IsGameplayScene(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return false;
        return sceneName != MainMenu && sceneName != Lobby;
    }
}
