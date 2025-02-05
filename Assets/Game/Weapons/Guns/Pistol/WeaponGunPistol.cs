using System;
using System.Runtime.InteropServices;
using UnityEngine;
using Unity.Netcode;
using RingBuffer;

public struct WeaponTickDataGunPistol : IWeaponTickData
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

    public static WeaponTickDataGunPistol NewFromReader(WeaponTickDataHeader header, FastBufferReader reader)
    {
        var result = new WeaponTickDataGunPistol { Header = header };
        reader.ReadValue(out result.MagazineSize);
        reader.ReadValue(out result.AmmoCount);
        reader.ReadValue(out result.ShootTimer);
        reader.ReadValue(out result.ReloadTimer);
        return result;
    }

    public byte[] Serialize()
    {
        var size = Marshal.SizeOf(typeof(WeaponTickDataGunPistol));
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

public class WeaponContextGunPistol : WeaponContext<WeaponTickDataGunPistol>
{
    public enum StateIndex
    {
        None,
        Idle,
        Shoot,
        Reload,
    }

    public int MagazineSize = 7;
    public int AmmoCount;
    public GameTimer ShootTimer = new(0.3f);
    public GameTimer ReloadTimer = new(1.0f);

    public override void Init(WeaponStateMachine<WeaponTickDataGunPistol> stateMachine)
    {
        base.Init(stateMachine);

        AmmoCount = MagazineSize;

        States = new WeaponState<WeaponTickDataGunPistol>[]
        {
            null,
            new WeaponStateGunPistolIdle(),
            new WeaponStateGunPistolShoot(),
            new WeaponStateGunPistolReload(),
        };

        foreach (var state in States)
        {
            state?.Init(stateMachine);
        }

        stateMachine.SetCurrentState((int)StateIndex.Idle);
    }

    public override WeaponTickDataGunPistol GetTickData(ulong tick)
    {
        return new WeaponTickDataGunPistol
        {
            Header = new WeaponTickDataHeader
            {
                Type = (ulong)WeaponType.GunPistol,
                Tick = tick,
                StateStartTick = StateMachine.IsStateStartedThisFrame ? tick : PrevStateStartTick,
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
        if (tickData is WeaponTickDataGunPistol tickDataGunPistol)
        {
            PrevStateStartTick = tickDataGunPistol.Header.StateStartTick; // REVIEW: 이게 맞나??
            CurrentStateIndex = tickDataGunPistol.Header.StateIndex;
            MagazineSize = tickDataGunPistol.MagazineSize;
            AmmoCount = tickDataGunPistol.AmmoCount;
            ShootTimer.CopyWithoutCallbacks(tickDataGunPistol.ShootTimer);
            ReloadTimer.CopyWithoutCallbacks(tickDataGunPistol.ReloadTimer);
        }
        else
        {
            Debug.LogError("ApplyTickData failed: Wrong type.");
        }
    }

    public override uint GetNextState(WeaponStateMachine<WeaponTickDataGunPistol> stateMachine, PlayerInput input)
    {
        var ctx = stateMachine.Context as WeaponContextGunPistol;

        switch (ctx.CurrentStateIndex)
        {
            case (uint)StateIndex.Idle:
                if (ctx.AmmoCount > 0 && input.InputDownWeaponShoot)
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

public class WeaponStateGunPistolIdle : WeaponState<WeaponTickDataGunPistol> { }

public class WeaponStateGunPistolShoot : WeaponState<WeaponTickDataGunPistol>
{
    private PlayerInput _input;
    private int _damage = 20;

    public override void Init(WeaponStateMachine<WeaponTickDataGunPistol> stateMachine)
    {
        base.Init(stateMachine);

        var ctx = StateMachine.Context as WeaponContextGunPistol;

        ctx.ShootTimer.AddCallback(0, () =>
        {
            if (ctx.AmmoCount == 0)
                return;

            ctx.AmmoCount -= 1;

            // 이펙트 및 애니메이션 실행 (클라 예측)

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

    public override void Rollback<T>(WeaponStateMachine<WeaponTickDataGunPistol> stateMachine, T correctTickData)
    {
        var ctx = StateMachine.Context as WeaponContextGunPistol;
        if (correctTickData is WeaponTickDataGunPistol correctTickDataGunPistol)
        {
            var correctTimerTime = correctTickDataGunPistol.ShootTimer.Time;
            ctx.ShootTimer.RollbackTo(correctTimerTime);
            // TODO: 이미 실행된 것들도 올바른 시간으로 되돌려야 함.
        }
    }

    public override bool IsDone()
    {
        var ctx = StateMachine.Context as WeaponContextGunPistol;
        return ctx.ShootTimer.IsEnded;
    }

    public override void OnStateExit()
    {
        var ctx = StateMachine.Context as WeaponContextGunPistol;
        ctx.ShootTimer.Reset();
    }

    public override void OnStateUpdate(PlayerInput input, float deltaTime)
    {
        _input = input;

        var ctx = StateMachine.Context as WeaponContextGunPistol;
        ctx.ShootTimer.Tick(deltaTime);
    }
}

public class WeaponStateGunPistolReload : WeaponState<WeaponTickDataGunPistol>
{
    public override void Init(WeaponStateMachine<WeaponTickDataGunPistol> stateMachine)
    {
        base.Init(stateMachine);

        var ctx = StateMachine.Context as WeaponContextGunPistol;

        ctx.ReloadTimer.AddCallback(ctx.ReloadTimer.Duration - 0.2f, () =>
        {
            Debug.Log("reload");
            ctx.AmmoCount = ctx.MagazineSize;
        });
    }

    public override bool IsDone()
    {
        var ctx = StateMachine.Context as WeaponContextGunPistol;
        return ctx.ReloadTimer.IsEnded;
    }

    public override void OnStateEnter()
    {
        Debug.Log("reload start");
    }

    public override void OnStateExit()
    {
        var ctx = StateMachine.Context as WeaponContextGunPistol;
        ctx.ReloadTimer.Reset();
        Debug.Log("reload done");
    }

    public override void OnStateUpdate(PlayerInput input, float deltaTime)
    {
        var ctx = StateMachine.Context as WeaponContextGunPistol;
        ctx.ReloadTimer.Tick(deltaTime);
    }
}

public class WeaponGunPistol : Weapon
{
    private readonly WeaponContextGunPistol _context = new();
    private readonly WeaponStateMachine<WeaponTickDataGunPistol> _stateMachine = new();

    public override Weapon Init(Player player)
    {
        WeaponType = WeaponType.GunPistol;
        _player = player;
        _stateMachine.Init(player, _context);
        return this;
    }

    public override void ResetWeapon()
    {
        // Reset state machine
        _stateMachine.SetCurrentState((uint)WeaponContextGunPistol.StateIndex.Idle);
        _stateMachine.TickBuffer.Clear();

        // Reset context
        _context.AmmoCount = _context.MagazineSize;
        _context.ShootTimer.Reset();
        _context.ReloadTimer.Reset();
    }

    public override void SetLatestTickData<T>(T tickData)
    {
        if (tickData is WeaponTickDataGunPistol tickDataGunPistol)
        {
            _stateMachine.LatestTickData = tickDataGunPistol;
        }
        else
        {
            Debug.LogError("SetLatestTickData: Wrong TickData type!");
        }
    }

    public override void SetStateToIdle()
    {
        _stateMachine.CurrentState.OnStateExit();
        _stateMachine.SetCurrentState((uint)WeaponContextGunPistol.StateIndex.Idle);
        _stateMachine.CurrentState.OnStateEnter();
    }

    public override void OnUpdate(PlayerInput input, float deltaTime)
    {
        _stateMachine.OnUpdate(input, deltaTime);
    }

    public override byte[] GetSerializedTickData(ulong tick)
    {
        return _stateMachine.Context.GetTickData(tick).Serialize();
    }

    public override void PushCurrentTickData(ulong tick)
    {
        _stateMachine.PushTickData(_stateMachine.Context.GetTickData(tick));
    }

    public override void Reconcile()
    {
        var serverTickDataOpt = _stateMachine.LatestTickData;
        if (serverTickDataOpt is { } serverTickData)
        {
            _stateMachine.LatestTickData = null;

            var i = _stateMachine.GetTickDataIndexFromBuffer(serverTickData.Header.Tick);
            if (i == -1) return;

            var predictedTickData = _stateMachine.TickBuffer[i];

            // Remove old data.
            _stateMachine.TickBuffer.ConsumeSpan(i + 1);

            // Check prediction.
            if (!serverTickData.Equals(predictedTickData))
            {
                // Rollback.
                foreach (var tickData in _stateMachine.TickBuffer)
                {
                    _stateMachine.Context.States[tickData.GetHeader().StateIndex].Rollback(_stateMachine, serverTickData);
                }

                // Resimulate.
                _stateMachine.Context.ApplyTickData(serverTickData);
                for (var j = 0; j < _player.InputBuffer.Count; ++j)
                {
                    var input = _player.InputBuffer[j];
                    _stateMachine.OnUpdate(input, Time.fixedDeltaTime);
                    _stateMachine.TickBuffer[j] = _stateMachine.Context.GetTickData(input.Tick);
                }
            }
        }
    }
}
