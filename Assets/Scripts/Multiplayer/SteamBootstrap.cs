using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Early-runtime Steamworks.SteamAPI init via reflection so the project builds without Steam DLLs.
/// </summary>
[DefaultExecutionOrder(-10000)]
internal sealed class SteamBootstrap : MonoBehaviour
{
    /// <summary>True after SteamAPI.Init succeeds.</summary>
    private bool _initialized;

    /// <summary>Cached SteamAPI.Init when Steamworks is loaded.</summary>
    private MethodInfo _steamApiInit;

    /// <summary>Cached SteamAPI.RunCallbacks when Steamworks is loaded.</summary>
    private MethodInfo _steamApiRunCallbacks;

    /// <summary>Cached SteamAPI.Shutdown when Steamworks is loaded.</summary>
    private MethodInfo _steamApiShutdown;

    internal bool IsInitialized => _initialized;

    /// <summary>Persists across scenes and attempts Steam API bind and init.</summary>
    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
        TryBindSteamworks();
        TryInitSteam();
    }

    /// <summary>Pumps Steam callbacks each frame while initialized.</summary>
    private void Update()
    {
        if (!_initialized)
            return;
        _steamApiRunCallbacks?.Invoke(null, null);
    }

    /// <summary>Shuts down Steam when the application quits.</summary>
    private void OnApplicationQuit()
    {
        Shutdown();
    }

    /// <summary>Shuts down Steam when this bootstrap object is destroyed.</summary>
    private void OnDestroy()
    {
        Shutdown();
    }

    /// <summary>Calls SteamAPI.Shutdown once if init had succeeded.</summary>
    private void Shutdown()
    {
        if (!_initialized)
            return;
        _initialized = false;
        try { _steamApiShutdown?.Invoke(null, null); }
        catch
        {
            // Best effort.
        }
    }

    /// <summary>Resolves SteamAPI static methods from loaded Steamworks assemblies.</summary>
    private void TryBindSteamworks()
    {
        Type steamApiType = AppDomain.CurrentDomain
            .GetAssemblies()
            .Select(a => a.GetType("Steamworks.SteamAPI", throwOnError: false))
            .FirstOrDefault(t => t != null);

        if (steamApiType == null)
            return;

        _steamApiInit = steamApiType.GetMethod("Init", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
        _steamApiRunCallbacks = steamApiType.GetMethod("RunCallbacks", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
        _steamApiShutdown = steamApiType.GetMethod("Shutdown", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
    }

    /// <summary>Invokes SteamAPI.Init and logs success or failure.</summary>
    private void TryInitSteam()
    {
        if (_steamApiInit == null)
            return;

        try
        {
            object result = _steamApiInit.Invoke(null, null);
            _initialized = result is bool b ? b : true;
            if (_initialized)
                Debug.Log("[Steam] SteamAPI initialized.");
            else
                Debug.LogWarning("[Steam] SteamAPI.Init returned false. Is Steam running / steam_appid.txt present?");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Steam] SteamAPI init failed: {e.GetType().Name}: {e.Message}");
        }
    }
}

