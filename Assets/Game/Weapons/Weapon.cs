using UnityEngine;
using Unity.Netcode;

public abstract class Weapon : MonoBehaviour
{
    [HideInInspector] public WeaponType WeaponType;

    protected Player _player;

    public abstract Weapon Init(Player player);

    public abstract void ResetWeapon();
    public abstract void SetLatestTickData(WeaponTickDataHeader header, FastBufferReader reader);
    public abstract void SetStateToIdle();
    public abstract void SetStateToHolster();

    public abstract byte[] GetSerializedTickData(ulong tick);
    public abstract void PushCurrentTickData(ulong tick);

    public abstract void ClearTickData();
    public abstract void ApplyLatestTickData();

    public abstract bool IsDesynced();
    public abstract void RollbackToTick(ulong tick);

    public abstract void OnUpdate(PlayerInput input, float deltaTime);
}
