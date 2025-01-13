using UnityEngine;
using Unity.Netcode;
using Netcode.Transports;
using Steamworks;

public class LobbyManager : MonoBehaviour
{
    private static LobbyManager s_instance = null;
    public static LobbyManager Singleton => s_instance;

    public CSteamID? JoinedLobbyId { get; private set; }

    private CallResult<LobbyEnter_t> _onJoinLobby;
    private Callback<GameLobbyJoinRequested_t> _onGameLobbyJoinRequested;

    private void Awake()
    {
        if (s_instance != null)
        {
            Destroy(gameObject);
            return;
        }
        s_instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        if (SteamManager.IsInitialized)
        {
            _onJoinLobby = new(OnJoinLobby);
            _onGameLobbyJoinRequested = new(OnGameLobbyJoinRequested);
        }

        NetworkManager.Singleton.OnClientConnectedCallback += (ulong clientId) =>
        {
            Debug.Log($"NetworkManager client connected: {clientId}");
        };

        NetworkManager.Singleton.OnClientStopped += (bool isHost) =>
        {
            Debug.Log($"OnClientStopped");

            if (NetworkManager.Singleton.IsClient)
            {
                NetworkManager.Singleton.Shutdown();
            }
            // Client has already left the Steam Lobby at this point.
            ClearJoinedLobbyId();
        };

        NetworkManager.Singleton.OnConnectionEvent += (NetworkManager _, ConnectionEventData data) =>
        {
            switch (data.EventType)
            {
                case ConnectionEvent.PeerDisconnected:
                    Debug.Log($"PeerDisconnected: {data.ClientId}");
                    break;
            }
        };
    }

    private void OnDestroy()
    {
        if (s_instance != this)
        {
            return;
        }
        s_instance = null;
    }

    public void SetJoinedLobbyId(ulong lobbyId)
    {
        JoinedLobbyId = new(lobbyId);
    }

    public void SetJoinedLobbyId(CSteamID lobbyId)
    {
        JoinedLobbyId = lobbyId;
    }

    public void ClearJoinedLobbyId()
    {
        JoinedLobbyId = null;
    }

    public void LeaveLobby()
    {
        if (JoinedLobbyId is { } id)
        {
            Debug.Log("You left the lobby.");
            SteamMatchmaking.LeaveLobby(id);
            NetworkManager.Singleton.Shutdown();
            ClearJoinedLobbyId();
        }
    }

    private void OnJoinLobby(LobbyEnter_t arg, bool bIOFailure)
    {
        if (bIOFailure)
        {
            Debug.LogError("OnJoinLobby IOFailure.");
            ClearJoinedLobbyId();
            return;
        }

        Debug.Log("You joined the lobby.");
        NetworkManager.Singleton.StartClient();
    }

    private void OnGameLobbyJoinRequested(GameLobbyJoinRequested_t arg)
    {
        Debug.Log("You accepted the invite.");

        if (JoinedLobbyId is { } id)
        {
            if (id.m_SteamID == arg.m_steamIDFriend.m_SteamID)
                return;

            LeaveLobby();
        }

        var transport = NetworkManager.Singleton.NetworkConfig.NetworkTransport as SteamNetworkingSocketsTransport;
        transport.ConnectToSteamID = arg.m_steamIDFriend.m_SteamID;

        SetJoinedLobbyId(arg.m_steamIDLobby);
        _onJoinLobby.Set(SteamMatchmaking.JoinLobby(arg.m_steamIDLobby));
    }
}
