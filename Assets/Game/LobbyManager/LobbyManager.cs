using System.Collections.Generic;
using System.Reflection;
using Steamworks;
using UnityEngine;
using Unity.Netcode;
using Netcode.Transports;

public class GameUser
{
    public ulong NetId = 0;
    public string Name;
    public bool IsDead = false;
    public Player Player = null;
}

public class LobbyManager : MonoBehaviour
{
    public static LobbyManager Singleton { get; private set; }

    public CSteamID? JoinedLobbyId { get; private set; }

    // Key: TransportId
    public Dictionary<ulong, GameUser> Users = new();

    // Key: ClientId, Value: TransportId
    public Dictionary<ulong, ulong> UserTransportId = new();

    private Dictionary<ulong, ulong> _clientToTransportId;

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
        GetClientAndTransportIdMapping();

        if (SteamManager.IsInitialized)
        {
            _steamOnJoinLobby = new(SteamOnJoinLobby);
            _steamOnClientLobbyEvent = new(SteamOnClientLobbyEvent);
            _steamOnGameLobbyJoinRequested = new(SteamOnGameLobbyJoinRequested);
        }

        NetworkManager.Singleton.OnClientConnectedCallback += (ulong clientId) =>
        {
            Debug.Log($"[NetworkManager] client connected: {clientId}");
            if (clientId == NetworkManager.Singleton.LocalClientId)
            {
                _clientToTransportId.Add(clientId, SteamUser.GetSteamID().m_SteamID);
            }

            var steamId = _clientToTransportId[clientId];
            UserTransportId.Add(clientId, steamId);
            Users.Add(steamId, new GameUser
            {
                NetId = clientId,
                Name = SteamFriends.GetFriendPersonaName(new(steamId))
            });
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
                    var steamId = UserTransportId[data.ClientId];
                    UserTransportId.Remove(data.ClientId);
                    Users.Remove(steamId);
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

    private void GetClientAndTransportIdMapping()
    {
        var bindingAttr = BindingFlags.NonPublic | BindingFlags.Instance;
        var netConnManagerInstance = typeof(NetworkManager)
            .GetField("ConnectionManager", bindingAttr)
            .GetValue(NetworkManager.Singleton);

        var conManType = typeof(NetworkConnectionManager);

        _clientToTransportId = conManType
            .GetField("ClientIdToTransportIdMap", bindingAttr)
            .GetValue(netConnManagerInstance) as Dictionary<ulong, ulong>;

        // TransportToClientId = conManType
        //     .GetField("TransportIdToClientIdMap", bindingAttr)
        //     .GetValue(netConnManagerInstance) as Dictionary<ulong, ulong>;
    }

    public GameUser GetUserByClientId(ulong clientId)
    {
        var steamId = UserTransportId[clientId];
        return Users[steamId];
    }

    public GameUser GetUserBySteamId(ulong steamId)
    {
        return Users[steamId];
    }

    public GameUser GetLocalUser()
    {
        return Users[SteamUser.GetSteamID().m_SteamID];
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
            UserTransportId.Clear();
            Users.Clear();
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
        if (NetworkManager.Singleton.IsHost)
        {
            // On entered.
            if ((arg.m_rgfChatMemberStateChange & (uint)EChatMemberStateChange.k_EChatMemberStateChangeEntered) != 0)
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

            // On disconnected.
            if ((arg.m_rgfChatMemberStateChange & (uint)EChatMemberStateChange.k_EChatMemberStateChangeDisconnected) != 0)
            {
                Debug.Log($"[Steamworks.NET] Client disconnected: {arg.m_ulSteamIDUserChanged}");
            }
        }
    }

    private void SteamOnGameLobbyJoinRequested(GameLobbyJoinRequested_t arg)
    {
        Debug.Log("[Steamworks.NET] You accepted the invite.");
        JoinLobby(arg.m_steamIDLobby, arg.m_steamIDFriend);
    }
}
