using UnityEngine;

public abstract class Weapon : MonoBehaviour
{
    public abstract void Init(Player player);
    public abstract void ResetWeapon();
    public abstract void SetLatestTickData<T>(T tickData) where T : struct, IWeaponTickData;
}
