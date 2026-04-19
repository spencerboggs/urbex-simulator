using FishNet.Managing;
using UnityEngine;
using UnityEngine.InputSystem;

// Editor / local test helpers. FishNet "host" is server + client on the same machine
public class NetworkStarter : MonoBehaviour
{
    [SerializeField]
    private NetworkManager _networkManager;

    private void Awake()
    {
        if (_networkManager == null)
            _networkManager = FindFirstObjectByType<NetworkManager>();
    }

    private void Update()
    {
        if (_networkManager == null || Keyboard.current == null)
            return;

        // Host: server then client (required for local play + PlayerSpawner)
        if (Keyboard.current.hKey.wasPressedThisFrame)
        {
            if (!_networkManager.ServerManager.Started)
                _networkManager.ServerManager.StartConnection();
            if (!_networkManager.ClientManager.Started)
                _networkManager.ClientManager.StartConnection();
        }

        // Dedicated server only (headless)
        if (Keyboard.current.sKey.wasPressedThisFrame && !_networkManager.ServerManager.Started)
            _networkManager.ServerManager.StartConnection();

        // Client only (connect to machine where S or H already started a server)
        if (Keyboard.current.cKey.wasPressedThisFrame && !_networkManager.ClientManager.Started)
            _networkManager.ClientManager.StartConnection();
    }
}
