using System;
using System.Runtime.InteropServices;
using UnityEngine;
using Unity.Netcode;

public struct WeaponTickDataGunAssaultRifle : IWeaponTickData
{
    public WeaponTickDataHeader Header;
    public int MagazineSize;
    public int AmmoCount;
    public GameTimer ShootTimer;
    public GameTimer ReloadTimer;

    public WeaponTickDataHeader GetHeader()
    {
        return Header;
    }

    public static WeaponTickDataGunAssaultRifle NewFromReader(WeaponTickDataHeader header, FastBufferReader reader)
    {
        var result = new WeaponTickDataGunAssaultRifle { Header = header };
        reader.ReadValue(out result.MagazineSize);
        reader.ReadValue(out result.AmmoCount);
        reader.ReadValue(out result.ShootTimer);
        reader.ReadValue(out result.ReloadTimer);
        return result;
    }

    public byte[] Serialize()
    {
        var size = Marshal.SizeOf(typeof(WeaponTickDataGunAssaultRifle));
        var writer = new FastBufferWriter(size, Unity.Collections.Allocator.Temp);
        if (!writer.TryBeginWrite(size))
        {
            throw new OverflowException("Not enough space in the buffer");
        }

        byte[] result;
        using (writer)
        {
            writer.WriteValue(Header);
            writer.WriteValue(MagazineSize);
            writer.WriteValue(AmmoCount);
            writer.WriteValue(ShootTimer);
            writer.WriteValue(ReloadTimer);
            result = writer.ToArray();
        }
        return result;
    }
}

public class WeaponContextGunAssaultRifle : WeaponContext<WeaponTickDataGunAssaultRifle>
{
    public enum StateIndex
    {
        None,
        Idle,
        Holster,
        Shoot,
        Reload,
    }

    public int MagazineSize = 20;
    public int AmmoCount;
    public GameTimer ShootTimer = new(0.1f);
    public GameTimer ReloadTimer = new(1.0f);

    public override void Init(WeaponStateMachine<WeaponTickDataGunAssaultRifle> stateMachine)
    {
        base.Init(stateMachine);

        AmmoCount = MagazineSize;

        States = new WeaponState<WeaponTickDataGunAssaultRifle>[]
        {
            null,
            new WeaponStateGunAssaultRifleIdle(),
            new WeaponStateGunAssaultRifleHolster(),
            new WeaponStateGunAssaultRifleShoot(),
            new WeaponStateGunAssaultRifleReload(),
        };

        foreach (var state in States)
        {
            state?.Init(stateMachine);
        }

        stateMachine.SetCurrentState((int)StateIndex.Idle);
    }

    public override WeaponTickDataGunAssaultRifle GetTickData(ulong tick)
    {
        return new WeaponTickDataGunAssaultRifle
        {
            Header = new WeaponTickDataHeader
            {
                Type = (ulong)WeaponType.GunAssaultRifle,
                Tick = tick,
                StateIndex = CurrentStateIndex,
            },
            MagazineSize = MagazineSize,
            AmmoCount = AmmoCount,
            ShootTimer = ShootTimer,
            ReloadTimer = ReloadTimer,
        };
    }

    public override void ApplyTickData<T>(T tickData)
    {
        if (tickData is WeaponTickDataGunAssaultRifle tickDataGunAssaultRifle)
        {
            CurrentStateIndex = tickDataGunAssaultRifle.Header.StateIndex;
            MagazineSize = tickDataGunAssaultRifle.MagazineSize;
            AmmoCount = tickDataGunAssaultRifle.AmmoCount;
            ShootTimer.CopyWithoutCallbacks(tickDataGunAssaultRifle.ShootTimer);
            ReloadTimer.CopyWithoutCallbacks(tickDataGunAssaultRifle.ReloadTimer);
        }
        else
        {
            Debug.LogError("ApplyTickData failed: Wrong type.");
        }
    }

    public override uint GetNextState(WeaponStateMachine<WeaponTickDataGunAssaultRifle> stateMachine, PlayerInput input)
    {
        var ctx = stateMachine.Context as WeaponContextGunAssaultRifle;

        switch (ctx.CurrentStateIndex)
        {
            case (uint)StateIndex.Idle:
                if (ctx.AmmoCount > 0 && input.InputHoldWeaponShoot)
                {
                    return (uint)StateIndex.Shoot;
                }

                if (ctx.AmmoCount < ctx.MagazineSize && input.InputDownWeaponReload)
                {
                    return (uint)StateIndex.Reload;
                }
                break;

            case (uint)StateIndex.Shoot:
                if (stateMachine.CurrentState.IsDone())
                {
                    return (uint)StateIndex.Idle;
                }
                break;

            case (uint)StateIndex.Reload:
                if (stateMachine.CurrentState.IsDone())
                {
                    return (uint)StateIndex.Idle;
                }
                break;
        }

        return 0;
    }
}

public class WeaponStateGunAssaultRifleIdle : WeaponState<WeaponTickDataGunAssaultRifle> { }

public class WeaponStateGunAssaultRifleHolster : WeaponState<WeaponTickDataGunAssaultRifle> { }

public class WeaponStateGunAssaultRifleShoot : WeaponState<WeaponTickDataGunAssaultRifle>
{
    private PlayerInput _input;
    private int _damage = 5;

    public override void Init(WeaponStateMachine<WeaponTickDataGunAssaultRifle> stateMachine)
    {
        base.Init(stateMachine);

        var ctx = StateMachine.Context as WeaponContextGunAssaultRifle;

        ctx.ShootTimer.AddCallback(0, () =>
        {
            if (ctx.AmmoCount == 0)
                return;

            // Client prediction.
            ctx.AmmoCount -= 1;
            // 이펙트 및 애니메이션 실행

            // Hit check.
            if (NetworkManager.Singleton.IsHost)
            {
                var rayPos = StateMachine.Player.GetHeadPos();
                var rayDir = _input.InputCameraDir;
                var rayDist = 100f;

                Debug.DrawRay(rayPos, rayDir * rayDist, Color.red, 2);
                if (Physics.Raycast(rayPos, rayDir, out var rayHitInfo, rayDist))
                {
                    var collider = rayHitInfo.collider;
                    if (collider != StateMachine.Player && collider.CompareTag("Player"))
                    {
                        var player = collider.GetComponent<Player>();
                        player.ApplyDamage(_damage);
                    }
                }
            }
        });
    }

    public override bool IsDone()
    {
        var ctx = StateMachine.Context as WeaponContextGunAssaultRifle;
        return ctx.ShootTimer.IsEnded;
    }

    public override void OnStateExit()
    {
        var ctx = StateMachine.Context as WeaponContextGunAssaultRifle;
        ctx.ShootTimer.Reset();
    }

    public override void OnStateUpdate(PlayerInput input, float deltaTime)
    {
        _input = input;

        var ctx = StateMachine.Context as WeaponContextGunAssaultRifle;
        ctx.ShootTimer.Tick(deltaTime);
    }
}

public class WeaponStateGunAssaultRifleReload : WeaponState<WeaponTickDataGunAssaultRifle>
{
    public override void Init(WeaponStateMachine<WeaponTickDataGunAssaultRifle> stateMachine)
    {
        base.Init(stateMachine);

        var ctx = StateMachine.Context as WeaponContextGunAssaultRifle;

        ctx.ReloadTimer.AddCallback(ctx.ReloadTimer.Duration - 0.2f, () =>
        {
            ctx.AmmoCount = ctx.MagazineSize;
        });
    }

    public override bool IsDone()
    {
        var ctx = StateMachine.Context as WeaponContextGunAssaultRifle;
        return ctx.ReloadTimer.IsEnded;
    }

    public override void OnStateExit()
    {
        var ctx = StateMachine.Context as WeaponContextGunAssaultRifle;
        ctx.ReloadTimer.Reset();
    }

    public override void OnStateUpdate(PlayerInput input, float deltaTime)
    {
        var ctx = StateMachine.Context as WeaponContextGunAssaultRifle;
        ctx.ReloadTimer.Tick(deltaTime);
    }
}

public class WeaponGunAssaultRifle : Weapon
{
    private readonly WeaponContextGunAssaultRifle _context = new();
    private readonly WeaponStateMachine<WeaponTickDataGunAssaultRifle> _stateMachine = new();

    public override Weapon Init(Player player)
    {
        WeaponType = WeaponType.GunAssaultRifle;
        _player = player;
        _stateMachine.Init(player, _context);
        return this;
    }

    public override void ResetWeapon()
    {
        // Reset state machine
        _stateMachine.SetCurrentState((uint)WeaponContextGunAssaultRifle.StateIndex.Idle);
        _stateMachine.TickBuffer.Clear();

        // Reset context
        _context.AmmoCount = _context.MagazineSize;
        _context.ShootTimer.Reset();
        _context.ReloadTimer.Reset();
    }

    public override void SetLatestTickData<T>(T tickData)
    {
        if (tickData is WeaponTickDataGunAssaultRifle tickDataGunAssaultRifle)
        {
            _stateMachine.LatestTickData = tickDataGunAssaultRifle;
        }
        else
        {
            Debug.LogError("SetLatestTickData: Wrong TickData type!");
        }
    }

    public override void SetStateToIdle()
    {
        _stateMachine.CurrentState.OnStateExit();
        _stateMachine.SetCurrentState((uint)WeaponContextGunAssaultRifle.StateIndex.Idle);
        _stateMachine.CurrentState.OnStateEnter();
    }

    public override void SetStateToHolster()
    {
        _stateMachine.CurrentState.OnStateExit();
        _stateMachine.SetCurrentState((uint)WeaponContextGunAssaultRifle.StateIndex.Holster);
        _stateMachine.CurrentState.OnStateEnter();
    }

    public override byte[] GetSerializedTickData(ulong tick)
    {
        return _stateMachine.Context.GetTickData(tick).Serialize();
    }

    public override void PushCurrentTickData(ulong tick)
    {
        _stateMachine.PushTickData(_stateMachine.Context.GetTickData(tick));
    }

    public override void ClearTickData()
    {
        _stateMachine.TickBuffer.Clear();
    }

    public override void ApplyLatestTickData()
    {
        if (_stateMachine.LatestTickData != null)
        {
            _context.ApplyTickData(_stateMachine.LatestTickData);
        }
    }

    public override bool IsDesynced()
    {
        if (_stateMachine.LatestTickData is { } serverTickData)
        {
            var i = _stateMachine.GetTickDataIndexFromBuffer(serverTickData.Header.Tick);
            if (i == -1)
            {
                Debug.LogWarning("WeaponGunAssaultRifle.IsDesynced: LatestTickData is too old.");
                return false;
            }

            var clientTickData = _stateMachine.TickBuffer[i];
            return
                serverTickData.Header.StateIndex != clientTickData.Header.StateIndex ||
                serverTickData.MagazineSize != clientTickData.MagazineSize ||
                serverTickData.AmmoCount != clientTickData.AmmoCount ||
                serverTickData.ShootTimer.Time != clientTickData.ShootTimer.Time ||
                serverTickData.ReloadTimer.Time != clientTickData.ReloadTimer.Time;
        }
        else
        {
            Debug.LogWarning("WeaponGunAssaultRifle.IsDesynced: LatestTickData is null.");
            return false;
        }
    }

    public override void RollbackToTick(ulong tick)
    {
        _stateMachine.RollbackTick(tick);
    }

    public override void OnUpdate(PlayerInput input, float deltaTime)
    {
        _stateMachine.OnUpdate(input, deltaTime);
    }
}
