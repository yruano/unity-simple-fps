using UnityEngine;
using UnityEngine.UIElements;
using Unity.Netcode;

public class LobbyChatManager : NetworkBehaviour
{
    private LobbyDashboardMenu _lobbyDashboardMenu;
    private TextField _lobbyChatTextField;

    private void Awake()
    {
        _lobbyDashboardMenu = FindFirstObjectByType<LobbyDashboardMenu>();
        _lobbyDashboardMenu.LobbyChatManager = this;
        _lobbyChatTextField = _lobbyDashboardMenu.GetLobbyChatTextField();
    }

    public override void OnNetworkSpawn()
    {
        // TODO: Client일 때 기존 채팅 다 가져오기
        _lobbyChatTextField.SetEnabled(true);
        base.OnNetworkSpawn();
    }

    public override void OnDestroy()
    {
        _lobbyChatTextField.SetEnabled(false);
    }

    [Rpc(SendTo.Everyone)]
    public void BroadcastMessageRpc(string name, string message)
    {
        _lobbyDashboardMenu.AddChatMessage(name, message);
    }
}
