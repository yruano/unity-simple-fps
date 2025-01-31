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

        var user = LobbyManager.Singleton.GetUserByClientId(rpcParams.Receive.SenderClientId);
        user.Player = player.GetComponent<Player>();

        var networkPlayer = player.GetComponent<NetworkObject>();
        networkPlayer.transform.position = new(0, 3.0f, 0);
        networkPlayer.SpawnWithOwnership(rpcParams.Receive.SenderClientId);
    }
}
