using FishNet.Managing;
using FishNet.Managing.Scened;
using FishNet.Object;
using FishNet.Component.Spawning;

public static class NetworkSceneFlow
{
    public const string MainMenu = "MainMenu";
    public const string Lobby = "Lobby";
    public const string World = "World";

    // Loads the lobby for every connection and future joiners (global scene)
    public static void LoadLobby(NetworkManager networkManager)
    {
        if (networkManager == null || !networkManager.IsServerStarted)
            return;

        SceneLoadData data = new(Lobby);
        data.ReplaceScenes = ReplaceOption.All;
        networkManager.SceneManager.LoadGlobalScenes(data);
    }

    // Host-only progression into the match
    // Assigns the player prefab on PlayerSpawner then loads the world scene globally
    public static void LoadWorld(NetworkManager networkManager, NetworkObject playerPrefab)
    {
        if (networkManager == null || !networkManager.IsServerStarted)
            return;

        if (networkManager.TryGetComponent(out PlayerSpawner spawner) && playerPrefab != null)
            spawner.SetPlayerPrefab(playerPrefab);

        SceneLoadData data = new(World);
        data.ReplaceScenes = ReplaceOption.All;
        networkManager.SceneManager.LoadGlobalScenes(data);
    }
}
