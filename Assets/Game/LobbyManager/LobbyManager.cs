using System.Collections.Generic;
using Steamworks;
using UnityEngine;
using Unity.Netcode;
using Netcode.Transports;

public class GameUser
{
    public ulong NetId;
    public Player Player;
}

public class LobbyManager : MonoBehaviour
{
    public static LobbyManager Singleton { get; private set; }

    public CSteamID? JoinedLobbyId { get; private set; }
    public Dictionary<CSteamID, GameUser> Users = new();

    private CallResult<LobbyEnter_t> _steamOnJoinLobby;
    private Callback<LobbyChatUpdate_t> _steamOnClientLobbyEvent;
    private Callback<GameLobbyJoinRequested_t> _steamOnGameLobbyJoinRequested;

    private void Awake()
    {
        if (Singleton != null)
        {
            Destroy(gameObject);
            return;
        }
        Singleton = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        if (SteamManager.IsInitialized)
        {
            _steamOnJoinLobby = new(SteamOnJoinLobby);
            _steamOnClientLobbyEvent = new(SteamOnClientLobbyEvent);
            _steamOnGameLobbyJoinRequested = new(SteamOnGameLobbyJoinRequested);
        }

        NetworkManager.Singleton.OnClientConnectedCallback += (ulong clientId) =>
        {
            Debug.Log($"[NetworkManager] client connected: {clientId}");

            if (NetworkManager.Singleton.IsHost)
            {
                // TODO: SteamNetworkingSocketsTransport 파일 복사해서 connectionMapping public으로 만들기

                if (NetworkManager.Singleton.LocalClientId == clientId)
                {
                    Users.Add(SteamUser.GetSteamID(), new GameUser { NetId = clientId });
                }
            }
        };

        NetworkManager.Singleton.OnClientStopped += (bool isHost) =>
        {
            Debug.Log($"[NetworkManager] OnClientStopped");
            if (NetworkManager.Singleton.IsClient)
            {
                LeaveLobby();
            }
        };

        NetworkManager.Singleton.OnConnectionEvent += (NetworkManager _, ConnectionEventData data) =>
        {
            switch (data.EventType)
            {
                case ConnectionEvent.PeerDisconnected:
                    Debug.Log($"[NetworkManager] PeerDisconnected: {data.ClientId}");
                    break;
            }
        };
    }

    private void OnDestroy()
    {
        if (Singleton != this)
        {
            return;
        }
        Singleton = null;
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

    public void JoinLobby(CSteamID lobbyId, CSteamID lobbyOwnerId)
    {
        // Leave current lobby.
        if (JoinedLobbyId is { } id)
        {
            if (id.m_SteamID == lobbyId.m_SteamID)
                return;

            LeaveLobby();
        }

        // Check server is full.
        var maxPlayers = SteamMatchmaking.GetLobbyMemberLimit(lobbyId);
        var curPlayers = SteamMatchmaking.GetNumLobbyMembers(lobbyId);
        if (curPlayers >= maxPlayers)
        {
            return;
        }

        // Join lobby.
        var transport = NetworkManager.Singleton.NetworkConfig.NetworkTransport as SteamNetworkingSocketsTransport;
        transport.ConnectToSteamID = lobbyOwnerId.m_SteamID;

        SetJoinedLobbyId(lobbyId);
        _steamOnJoinLobby.Set(SteamMatchmaking.JoinLobby(lobbyId));
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

    private void SteamOnJoinLobby(LobbyEnter_t arg, bool bIOFailure)
    {
        if (bIOFailure)
        {
            Debug.LogError("[Steamworks.NET] OnJoinLobby IOFailure.");
            ClearJoinedLobbyId();
            return;
        }

        Debug.Log("[Steamworks.NET] You joined the lobby.");
        NetworkManager.Singleton.StartClient();
    }

    private void SteamOnClientLobbyEvent(LobbyChatUpdate_t arg)
    {
        // Lobby entered.
        if ((arg.m_rgfChatMemberStateChange & (uint)EChatMemberStateChange.k_EChatMemberStateChangeEntered) != 0)
        {
            if (NetworkManager.Singleton.IsHost)
            {
                Debug.Log($"[Steamworks.NET] Client joined: {arg.m_ulSteamIDUserChanged}");
                var maxPlayers = SteamMatchmaking.GetLobbyMemberLimit(JoinedLobbyId.Value);
                var curPlayers = SteamMatchmaking.GetNumLobbyMembers(JoinedLobbyId.Value);
                if (curPlayers >= maxPlayers)
                {
                    var userName = SteamFriends.GetFriendPersonaName(new(arg.m_ulSteamIDUserChanged));
                    Debug.LogWarning($"[Steamworks.NET] Client joined while the server is full: {userName}, {arg.m_ulSteamIDUserChanged}");
                    // Steamworks api has no way to kick user...
                }
            }
        }
    }

    private void SteamOnGameLobbyJoinRequested(GameLobbyJoinRequested_t arg)
    {
        Debug.Log("[Steamworks.NET] You accepted the invite.");
        JoinLobby(arg.m_steamIDLobby, arg.m_steamIDFriend);
    }
}
