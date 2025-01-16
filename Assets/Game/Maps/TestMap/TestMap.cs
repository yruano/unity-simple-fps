using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;

public class TestMap : MapBase
{
    [SerializeField]
    private GameObject PlayerPrefab;

    protected override void Start()
    {
        base.Start();
        SpawnPlayerRpc();
    }

    protected override void OnClientStopped(bool isHost)
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