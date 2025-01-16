using Steamworks;
using UnityEngine;

public class SteamManager : MonoBehaviour
{
    public static SteamManager Singleton { get; private set; }
    public static readonly AppId_t AppId = new(480); // https://steamdb.info/app/480/info/
    public static bool IsInitialized { get; private set; } = false;

    private void Awake()
    {
        if (Singleton != null)
        {
            Destroy(gameObject);
            return;
        }
        Singleton = this;
        DontDestroyOnLoad(gameObject);

        InitSteamworks();
    }

    private void OnDestroy()
    {
        if (Singleton != this)
        {
            return;
        }
        Singleton = null;

        if (IsInitialized)
        {
            SteamAPI.Shutdown();
        }
    }

    private void Update()
    {
        if (IsInitialized)
        {
            // Run Steam client callbacks
            SteamAPI.RunCallbacks();
        }
    }

    private void InitSteamworks()
    {
        if (!Packsize.Test())
        {
            Debug.LogError("[Steamworks.NET] Packsize Test returned false, the wrong version of Steamworks.NET is being run in this platform.", this);
        }

        if (!DllCheck.Test())
        {
            Debug.LogError("[Steamworks.NET] DllCheck Test returned false, One or more of the Steamworks binaries seems to be the wrong version.", this);
        }

        try
        {
            if (SteamAPI.RestartAppIfNecessary(AppId))
            {
                Application.Quit();
                return;
            }
        }
        catch (System.DllNotFoundException e)
        {
            Debug.LogError("[Steamworks.NET] Could not load [lib]steam_api.dll/so/dylib. It's likely not in the correct location. Refer to the README for more details.\n" + e, this);
            Application.Quit();
            return;
        }

        IsInitialized = SteamAPI.Init();
        if (!IsInitialized)
        {
            Debug.LogError("[Steamworks.NET] SteamAPI_Init() failed. Refer to Valve's documentation or the comment above this line for more information.", this);
            return;
        }

        SteamClient.SetWarningMessageHook(
            new SteamAPIWarningMessageHook_t((int nSeverity, System.Text.StringBuilder pchDebugText) =>
            {
                Debug.LogWarning(pchDebugText);
            })
        );

        Debug.Log("[Steamworks.NET] Initialized.");
    }
}
