using UnityEngine;
using Unity.Netcode;

public class HealthPickup : Pickup
{
    public override void OnPickup(Player player)
    {
        player.ApplyHeal(20);
        GetComponent<NetworkObject>().Despawn();
    }
}
