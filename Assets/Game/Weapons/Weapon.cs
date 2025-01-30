using UnityEngine;

public abstract class Weapon : MonoBehaviour
{
    protected Player _player;
    protected ulong _tick = 0;

    public abstract void Init(Player player);
    public abstract void ResetWeapon();
    public abstract void SetLatestTickData<T>(T tickData) where T : struct, IWeaponTickData;
}
