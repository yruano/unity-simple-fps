using UnityEngine;
using Unity.Netcode;

public class LobbyChatManager : NetworkBehaviour
{
    private LobbyDashboardMenu _lobbyDashboardMenu;

    private void Awake()
    {
        Debug.Log("LobbyChatManager spawned");
        _lobbyDashboardMenu = FindFirstObjectByType<LobbyDashboardMenu>();
        _lobbyDashboardMenu.LobbyChatManager = this;
    }

    private void Start()
    {
        // Client일 때 기존 채팅 다 가져오기
    }

    public void SendMessage(string name, string message)
    {
        SendMessageRpc(name, message);
    }

    [Rpc(SendTo.Server)]
    private void SendMessageRpc(string name, string message)
    {
        BroadcastMessageRpc(name, message);
    }

    [Rpc(SendTo.Everyone)]
    public void BroadcastMessageRpc(string name, string message)
    {
        _lobbyDashboardMenu.AddChatMessage(name, message);
    }
}
