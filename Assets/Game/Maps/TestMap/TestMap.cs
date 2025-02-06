using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;

public class TestMap : MapBase
{
    [SerializeField]
    private Player PlayerPrefab;

    protected override void Start()
    {
        base.Start();

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        if (IsHost)
        {
            foreach (var user in LobbyManager.Singleton.Users.Values)
            {
                SpawnPlayer(user.ClientId);
            }
        }
    }

    protected override void OnClientConnected(ulong clientId)
    {
        if (IsHost)
        {
            SpawnPlayer(clientId);
        }
    }

    protected override void OnClientStopped(bool isHost)
    {
        SceneManager.LoadScene(Scenes.LobbyListMenu);
    }

    private void SpawnPlayer(ulong clientId)
    {
        var player = Instantiate(PlayerPrefab);
        player.SetInputActive(true);

        var networkPlayer = player.GetComponent<NetworkObject>();
        networkPlayer.transform.position = new(0, 3.0f, 0);
        networkPlayer.SpawnWithOwnership(clientId);
    }
}
