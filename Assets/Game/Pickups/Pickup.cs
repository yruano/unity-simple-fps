using Unity.Netcode;

public abstract class Pickup : NetworkBehaviour
{
    public abstract void OnPickup(Player plyer);
}
