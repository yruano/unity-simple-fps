using Unity.Netcode;

public abstract class Pickup : NetworkBehaviour
{
    private void Start()
    {
        if (!IsHost && !IsSpawned)
        {
            Destroy(gameObject);
        }
    }

    public abstract void OnPickup(Player player);
}
