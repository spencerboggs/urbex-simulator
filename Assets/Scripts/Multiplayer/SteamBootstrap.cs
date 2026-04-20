using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

[DefaultExecutionOrder(-10000)]
internal sealed class SteamBootstrap : MonoBehaviour
{
    private bool _initialized;
    private MethodInfo _steamApiInit;
    private MethodInfo _steamApiRunCallbacks;
    private MethodInfo _steamApiShutdown;

    internal bool IsInitialized => _initialized;

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
        TryBindSteamworks();
        TryInitSteam();
    }

    private void Update()
    {
        if (!_initialized)
            return;
        _steamApiRunCallbacks?.Invoke(null, null);
    }

    private void OnApplicationQuit()
    {
        Shutdown();
    }

    private void OnDestroy()
    {
        Shutdown();
    }

    private void Shutdown()
    {
        if (!_initialized)
            return;
        _initialized = false;
        try { _steamApiShutdown?.Invoke(null, null); }
        catch { /* best-effort */ }
    }

    private void TryBindSteamworks()
    {
        // Prefer already-loaded assemblies
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

