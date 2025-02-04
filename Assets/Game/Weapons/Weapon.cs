using UnityEngine;

public abstract class Weapon : MonoBehaviour
{
    protected Player _player;
    protected ulong _tick = 0;
    protected ulong _delayTick = 0;
    protected bool _startTick = false;
    protected bool _tickSpeedUp = false;
    protected bool _tickSlowDown = false;

    public abstract void Init(Player player);
    public abstract void ResetWeapon();
    public abstract void SetLatestTickData<T>(T tickData) where T : struct, IWeaponTickData;
}
