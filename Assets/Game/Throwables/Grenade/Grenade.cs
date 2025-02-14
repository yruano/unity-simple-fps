using UnityEngine;
using Unity.Netcode;

public class Grenade : NetworkBehaviour
{
    private NetworkObject _networkObject;
    private GameTimer _despawnTimer = new(3.0f);

    private void Awake()
    {
        _networkObject = GetComponent<NetworkObject>();
    }

    private void Update()
    {
        if (!IsSpawned)
            return;

        _despawnTimer.Tick(Time.deltaTime);
        if (_despawnTimer.IsEnded)
        {
            // TODO: damage player in area
            _networkObject.Despawn();
        }
    }
}
