using UnityEngine;
using Steamworks;

public class SteamManager : MonoBehaviour
{
    private static SteamManager s_instance = null;
    private static SteamManager Instance => s_instance;
    public static bool IsInitialized { get; private set; } = false;

    private void Awake()
    {
        if (s_instance != null)
        {
            Destroy(gameObject);
            return;
        }
        s_instance = this;

        DontDestroyOnLoad(gameObject);

        InitSteamworks();

        if (IsInitialized)
        {
            string name = SteamFriends.GetPersonaName();
			Debug.Log(name);
        }
    }

    private void OnDestroy()
    {
        if (s_instance != this)
        {
            return;
        }
        s_instance = null;

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
            if (SteamAPI.RestartAppIfNecessary((AppId_t)480))
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

        SteamClient.SetWarningMessageHook(new SteamAPIWarningMessageHook_t(
            (int nSeverity, System.Text.StringBuilder pchDebugText) =>
            {
                Debug.LogWarning(pchDebugText);
            }
        ));

        Debug.Log("Steamwork Initialized");
    }
}
