using System.Collections.Generic;
using System.Reflection;
using Steamworks;
using UnityEngine;
using Unity.Netcode;
using Netcode.Transports;

public class GameUser
{
    public ulong ClientId = 0;
    public string Name;
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

        _steamOnJoinLobby = new(SteamOnJoinLobby);
        _steamOnClientLobbyEvent = new(SteamOnClientLobbyEvent);
        _steamOnGameLobbyJoinRequested = new(SteamOnGameLobbyJoinRequested);

        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnectedCallback;
        NetworkManager.Singleton.OnClientStopped += OnClientStopped;
        NetworkManager.Singleton.OnConnectionEvent += OnConnectionEvent;
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
        Debug.Log("JoinLobby");

        // Leave current lobby.
        if (JoinedLobbyId is { } id)
        {
            if (id.m_SteamID == lobbyId.m_SteamID)
            {
                return;
            }

            LeaveLobby();
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

    private void OnClientConnectedCallback(ulong clientId)
    {
        Debug.Log($"[NetworkManager] client connected: {clientId}");

        if (NetworkManager.Singleton.IsHost)
        {
            // Check if server is full.
            if (clientId != NetworkManager.Singleton.LocalClientId)
            {
                if (JoinedLobbyId is { } lobbyId)
                {
                    var maxPlayers = SteamMatchmaking.GetLobbyMemberLimit(lobbyId);
                    var curPlayers = SteamMatchmaking.GetNumLobbyMembers(lobbyId);
                    Debug.Log(maxPlayers);
                    Debug.Log(curPlayers);
                    if (curPlayers >= maxPlayers)
                    {
                        NetworkManager.Singleton.DisconnectClient(clientId, "Server is full.");
                        return;
                    }
                }
            }
        }

        // Add self to _clientToTransportId mapping.
        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            _clientToTransportId.Add(clientId, SteamUser.GetSteamID().m_SteamID);
        }

        // Add GameUser.
        var steamId = _clientToTransportId[clientId];
        UserTransportId.Add(clientId, steamId);
        Users.Add(steamId, new GameUser
        {
            ClientId = clientId,
            Name = SteamFriends.GetFriendPersonaName(new(steamId))
        });
    }

    private void OnClientStopped(bool isHost)
    {
        Debug.Log($"[NetworkManager] OnClientStopped");
        if (NetworkManager.Singleton.IsClient)
        {
            LeaveLobby();
        }
    }

    private void OnConnectionEvent(NetworkManager _, ConnectionEventData data)
    {
        switch (data.EventType)
        {
            case ConnectionEvent.PeerDisconnected:
                Debug.Log($"[NetworkManager] PeerDisconnected: {data.ClientId}");
                if (UserTransportId.ContainsKey(data.ClientId))
                {
                    var steamId = UserTransportId[data.ClientId];
                    UserTransportId.Remove(data.ClientId);
                    Users.Remove(steamId);
                }
                break;
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
