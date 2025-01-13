using UnityEngine;
using Unity.Netcode;
using UnityEngine.SceneManagement;
using Steamworks;

public class TestMap : NetworkBehaviour
{
    [SerializeField]
    private GameObject PlayerPrefab;

    private void Start()
    {
        NetworkManager.Singleton.OnClientStopped += OnClientStopped;

        SpawnPlayerRpc();
    }

    public override void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientStopped -= OnClientStopped;
        }
    }

    private void OnClientStopped(bool isHost)
    {
        SceneManager.LoadScene(Scenes.LobbyListMenu);
    }

    [Rpc(SendTo.Server)]
    private void SpawnPlayerRpc(RpcParams rpcParams = default)
    {
        var player = Instantiate(PlayerPrefab);
        var network_player = player.GetComponent<NetworkObject>();
        network_player.transform.position = new(0, 1.5f, 0);
        network_player.SpawnWithOwnership(rpcParams.Receive.SenderClientId);
    }
}
