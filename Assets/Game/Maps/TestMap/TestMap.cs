using UnityEngine;
using Unity.Netcode;

public class TestMap : NetworkBehaviour
{
    [SerializeField]
    private GameObject PlayerPrefab;

    private void Start()
    {
        SpawnPlayerRpc();
    }

    [Rpc(SendTo.Server)]
    private void SpawnPlayerRpc(RpcParams rpcParams = default)
    {
        var player = Instantiate(PlayerPrefab);
        var network_player = player.GetComponent<NetworkObject>();
        network_player.transform.position = new(0, 2, 0);
        network_player.SpawnWithOwnership(rpcParams.Receive.SenderClientId);
    }
}