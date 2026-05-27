using FishNet.Broadcast;

// Lobby-time messages exchanged between the host and connected clients. We use
// FishNet Broadcasts (not RPCs) because no NetworkBehaviour exists in the lobby
// scene yet - broadcasts only require the ServerManager / ClientManager
//
// All broadcasts are registered/unregistered from NetworkSessionController so the
// handlers persist across scene loads and are available before any UI script
// awakes on a late-joining client

// Server -> clients. Sent by the host whenever the lobby map selection changes,
// and also sent targeted to each client right after they connect so late-joiners
// see the current selection without having to ask
public struct MapSelectionBroadcast : IBroadcast
{
    public string SceneName;
}

// Client -> server. A client (specifically the host in the current design, but
// the server enforces the host check) requests that the lobby selection change
// to the named scene. The server validates against the MapCatalog and, if
// accepted, fans the result back out via MapSelectionBroadcast
public struct RequestMapSelectionBroadcast : IBroadcast
{
    public string SceneName;
}
