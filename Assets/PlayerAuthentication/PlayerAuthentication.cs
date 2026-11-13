using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

// Signs the player in anonymously at startup so online room operations start warmed up.
public static class PlayerAuthentication
{
    static TaskCompletionSource<bool> signedIn = new TaskCompletionSource<bool>();
    static Task<bool> activeAttempt;

    public static Task<bool> SignedIn => signedIn.Task;
    public static string LastError { get; private set; } = "";
    public static event Action OnSignedInCompleted;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetForPlaySession()
    {
        signedIn = new TaskCompletionSource<bool>();
        activeAttempt = null;
        LastError = "";
        OnSignedInCompleted = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static async void SignInAtStartup()
    {
        await EnsureSignedInAsync();
    }

    public static async Task<bool> EnsureSignedInAsync()
    {
        if (signedIn.Task.IsCompleted && signedIn.Task.Result)
            return true;

        if (activeAttempt != null && !activeAttempt.IsCompleted)
            return await activeAttempt;

        if (signedIn.Task.IsCompleted && !signedIn.Task.Result)
            signedIn = new TaskCompletionSource<bool>();

        var pending = signedIn;
        activeAttempt = SignInAsync();
        bool ok = await activeAttempt;
        pending.TrySetResult(ok);
        if (ok)
        {
            OnSignedInCompleted?.Invoke();
        }
        return ok;
    }

    static async Task<bool> SignInAsync()
    {
        try
        {
            LastError = "";
            await Task.Yield();
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                await UnityServices.InitializeAsync();
            }
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
            Debug.Log($"Signed in anonymously as player {AuthenticationService.Instance.PlayerId}");
            return true;
        }
        catch (Exception e)
        {
            LastError = $"{e.GetType().Name}: {e.Message}";
            Debug.LogWarning($"Anonymous sign-in failed: {LastError}");
            return false;
        }
    }
}
