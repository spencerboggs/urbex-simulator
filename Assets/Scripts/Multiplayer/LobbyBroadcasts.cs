using FishNet.Broadcast;

/// <summary>
/// Server-to-clients map selection update (also sent to new connections on join).
/// </summary>
public struct MapSelectionBroadcast : IBroadcast
{
    /// <summary>Gameplay scene name from MapCatalog.</summary>
    public string SceneName;
}

/// <summary>
/// Client-to-server request to change the lobby map (host-only; validated on server).
/// </summary>
public struct RequestMapSelectionBroadcast : IBroadcast
{
    /// <summary>Requested gameplay scene name from MapCatalog.</summary>
    public string SceneName;
}
