using System;
using System.Threading.Tasks;
using Unity.Services.RemoteConfig;
using UnityEngine;

// Reads the liveops_enabled kill switch from Remote Config without blocking UI/matchmaking calls.
public static class LiveOps
{
    public const string EnabledKey = "liveops_enabled";
    private static bool _hasFetched;
    private static bool _cachedEnabled = true;
    private static Task _inFlightFetch;

    struct UserAttributes { }
    struct AppAttributes { }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        _hasFetched = false;
        _cachedEnabled = true;
        _inFlightFetch = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void SubscribeAuthPrewarm()
    {
        PlayerAuthentication.OnSignedInCompleted -= StartBackgroundFetch;
        PlayerAuthentication.OnSignedInCompleted += StartBackgroundFetch;
    }

    public static void StartBackgroundFetch()
    {
        if (_hasFetched || (_inFlightFetch != null && !_inFlightFetch.IsCompleted)) return;
        _inFlightFetch = FetchInternalAsync();
    }

    private static async Task FetchInternalAsync()
    {
        try
        {
            if (!await PlayerAuthentication.SignedIn) return;
            var config = await RemoteConfigService.Instance.FetchConfigsAsync(
                new UserAttributes(), new AppAttributes());
            _cachedEnabled = config.GetBool(EnabledKey, true);
            _hasFetched = true;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Remote Config fetch skipped, live features stay on: {e.Message}");
        }
    }

    // Never blocks room Create/Join waiting on HTTP timeout; uses fetched cache or optimistic true while background fetch finishes.
    public static Task<bool> IsEnabledAsync()
    {
        if (_hasFetched) return Task.FromResult(_cachedEnabled);
        StartBackgroundFetch();
        return Task.FromResult(_cachedEnabled);
    }
}
