using UnityEngine;

public abstract class Weapon : MonoBehaviour
{
    [HideInInspector] public WeaponType WeaponType;

    protected Player _player;

    public abstract Weapon Init(Player player);

    public abstract void ResetWeapon();
    public abstract void SetLatestTickData<T>(T tickData) where T : struct, IWeaponTickData;
    public abstract void SetStateToIdle();

    public abstract byte[] GetSerializedTickData(ulong tick);
    public abstract void PushCurrentTickData(ulong tick);

    public abstract void ClearTickData(ulong latestTick);
    public abstract void ApplyLatestTickData();

    public abstract void OnUpdate(PlayerInput input, float deltaTime);

    public abstract void RollbackToTick(ulong tick);
}
