using Unity.Netcode;
using UnityEngine;

public class LobbyChatManager : MonoBehaviour
{
    private LobbyListMenu _lobbyListMenu;

    private void Awake()
    {
        _lobbyListMenu = FindFirstObjectByType<LobbyListMenu>();
        _lobbyListMenu.LobbyChatManager = this;
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
    public void SendMessageRpc(string name, string message)
    {
        BroadcastMessageRpc(name, message);
    }

    [Rpc(SendTo.Everyone)]
    public void BroadcastMessageRpc(string name, string message)
    {
        _lobbyListMenu.AddChatMessage(name, message);
    }
}
