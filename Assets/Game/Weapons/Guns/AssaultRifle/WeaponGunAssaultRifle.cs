using System;
using System.Runtime.InteropServices;
using UnityEngine;
using Unity.Netcode;

using TickData = WeaponTickDataGunAssaultRifle;
using Context = WeaponContextGunAssaultRifle;

public struct WeaponTickDataGunAssaultRifle : IWeaponTickData
{
    public WeaponTickDataHeader Header;
    public int MagazineSize;
    public int AmmoCount;
    public GameTimer ShootTimer;
    public GameTimer ReloadTimer;

    public WeaponTickDataHeader GetHeader() => Header;

    public static TickData NewFromReader(WeaponTickDataHeader header, FastBufferReader reader)
    {
        var result = new TickData { Header = header };
        reader.ReadValue(out result.MagazineSize);
        reader.ReadValue(out result.AmmoCount);
        reader.ReadValue(out result.ShootTimer);
        reader.ReadValue(out result.ReloadTimer);
        return result;
    }

    public byte[] Serialize()
    {
        var size = Marshal.SizeOf(typeof(TickData));
        using var writer = new FastBufferWriter(size, Unity.Collections.Allocator.Temp);
        if (!writer.TryBeginWrite(size))
            throw new OverflowException("Not enough space in the buffer");

        writer.WriteValue(Header);
        writer.WriteValue(MagazineSize);
        writer.WriteValue(AmmoCount);
        writer.WriteValue(ShootTimer);
        writer.WriteValue(ReloadTimer);
        return writer.ToArray();
    }
}

[Serializable]
public class WeaponContextGunAssaultRifle : WeaponContext<TickData>
{
    public BulletTrail BulletTrailPrefab;

    public enum StateIndex : uint
    {
        None,
        Idle,
        Holster,
        Shoot,
        Reload,
    }

    public int MagazineSize = 20;
    [HideInInspector] public int AmmoCount;
    public GameTimer ShootTimer = new(0.1f);
    public GameTimer ReloadTimer = new(1.0f);

    public override void Init(WeaponType weaponType, WeaponStateMachine<TickData> stateMachine)
    {
        base.Init(weaponType, stateMachine);

        States = new WeaponState<TickData>[5];
        States[(uint)StateIndex.Idle] = new WeaponStateGunAssaultRifleIdle(stateMachine);
        States[(uint)StateIndex.Holster] = new WeaponStateGunAssaultRifleHolster(stateMachine);
        States[(uint)StateIndex.Shoot] = new WeaponStateGunAssaultRifleShoot(stateMachine);
        States[(uint)StateIndex.Reload] = new WeaponStateGunAssaultRifleReload(stateMachine);

        stateMachine.SetState((int)StateIndex.Idle);
    }

    public override void Reset()
    {
        AmmoCount = MagazineSize;
        ShootTimer.Reset();
        ReloadTimer.Reset();
    }

    public override TickData GetTickData(ulong tick)
    {
        return new TickData
        {
            Header = GetTickDataHeader(tick),
            MagazineSize = MagazineSize,
            AmmoCount = AmmoCount,
            ShootTimer = ShootTimer,
            ReloadTimer = ReloadTimer,
        };
    }

    public override void ApplyTickData<T>(T tickDataT)
    {
        if (tickDataT is TickData tickData)
        {
            CurrentStateIndex = tickData.Header.StateIndex;
            MagazineSize = tickData.MagazineSize;
            AmmoCount = tickData.AmmoCount;
            ShootTimer.CopyWithoutCallbacks(tickData.ShootTimer);
            ReloadTimer.CopyWithoutCallbacks(tickData.ReloadTimer);
        }
        else
        {
            Debug.LogError("ApplyTickData failed: Wrong type.");
        }
    }

    public override uint GetNextState(WeaponStateMachine<TickData> stateMachine, PlayerInput input)
    {
        var ctx = (Context)stateMachine.Context;

        switch ((StateIndex)ctx.CurrentStateIndex)
        {
            case StateIndex.Idle:
                if (input.InputHoldWeaponShoot && ctx.AmmoCount > 0)
                {
                    return (uint)StateIndex.Shoot;
                }
                if (input.InputDownWeaponReload && ctx.AmmoCount < ctx.MagazineSize)
                {
                    return (uint)StateIndex.Reload;
                }
                break;

            case StateIndex.Shoot:
                if (input.InputDownWeaponReload && ctx.AmmoCount < ctx.MagazineSize)
                {
                    return (uint)StateIndex.Reload;
                }
                if (stateMachine.CurrentState.IsDone())
                {
                    return (uint)StateIndex.Idle;
                }
                break;

            case StateIndex.Reload:
                if (stateMachine.CurrentState.IsDone())
                {
                    return (uint)StateIndex.Idle;
                }
                break;
        }

        return (uint)StateIndex.None;
    }
}

public class WeaponStateGunAssaultRifleIdle : WeaponState<TickData>
{
    public WeaponStateGunAssaultRifleIdle(WeaponStateMachine<TickData> stateMachine) : base(stateMachine) { }
}

public class WeaponStateGunAssaultRifleHolster : WeaponState<TickData>
{
    public WeaponStateGunAssaultRifleHolster(WeaponStateMachine<TickData> stateMachine) : base(stateMachine) { }
}

public class WeaponStateGunAssaultRifleShoot : WeaponState<TickData>
{
    private readonly Context _context;
    private PlayerInput _input;
    private int _damage = 5;

    public WeaponStateGunAssaultRifleShoot(WeaponStateMachine<TickData> stateMachine) : base(stateMachine)
    {
        _context = (Context)StateMachine.Context;

        _context.ShootTimer.AddCallback(0, () =>
        {
            if (_context.AmmoCount == 0)
                return;

            // Client prediction.
            _context.AmmoCount -= 1;
            // 이펙트 및 애니메이션 실행

            // Hit check.
            if (NetworkManager.Singleton.IsHost)
            {
                var rayPos = StateMachine.Player.GetHeadPos();
                var rayDir = _input.InputCameraDir;
                var rayDist = 100f;

                if (Physics.Raycast(rayPos, rayDir, out var rayHitInfo, rayDist))
                {
                    var collider = rayHitInfo.collider;
                    if (collider != StateMachine.Player && collider.CompareTag("Player"))
                    {
                        var player = collider.GetComponent<Player>();
                        player.ApplyDamage(_damage);
                    }

                    stateMachine.Player.SpawnBulletTrailRpc(_context.WeaponType, rayPos, rayHitInfo.point);
                }
                else
                {
                    stateMachine.Player.SpawnBulletTrailRpc(_context.WeaponType, rayPos, rayPos + rayDir * rayDist);
                }
            }
        });
    }

    public override bool IsDone()
    {
        return _context.ShootTimer.IsEnded;
    }

    public override void OnStateExit()
    {
        _context.ShootTimer.Reset();
    }

    public override void OnStateUpdate(PlayerInput input, float deltaTime)
    {
        _input = input;
        _context.ShootTimer.Tick(deltaTime);
    }
}

public class WeaponStateGunAssaultRifleReload : WeaponState<TickData>
{
    private readonly Context _context;

    public WeaponStateGunAssaultRifleReload(WeaponStateMachine<TickData> stateMachine) : base(stateMachine)
    {
        _context = (Context)StateMachine.Context;

        _context.ReloadTimer.AddCallback(_context.ReloadTimer.Duration - 0.2f, () =>
        {
            _context.AmmoCount = _context.MagazineSize;
        });
    }

    public override bool IsDone()
    {
        return _context.ReloadTimer.IsEnded;
    }

    public override void OnStateExit()
    {
        _context.ReloadTimer.Reset();
    }

    public override void OnStateUpdate(PlayerInput input, float deltaTime)
    {
        _context.ReloadTimer.Tick(deltaTime);
    }
}

public class WeaponGunAssaultRifle : Weapon
{
    [SerializeField] private Context _context = new();
    private readonly WeaponStateMachine<TickData> _stateMachine = new();

    public override Weapon Init(Player player)
    {
        WeaponType = WeaponType.GunAssaultRifle;
        _player = player;
        _stateMachine.Init(player, _context, WeaponType);
        return this;
    }

    public override void ResetWeapon()
    {
        // Reset context
        _context.Reset();

        // Reset state machine
        _stateMachine.SetState((uint)Context.StateIndex.Idle);
        _stateMachine.TickBuffer.Clear();
    }

    public override void SetLatestTickData(WeaponTickDataHeader header, FastBufferReader reader)
    {
        _stateMachine.LatestTickData = TickData.NewFromReader(header, reader);
    }

    public override void SetStateToIdle()
    {
        _stateMachine.CurrentState.OnStateExit();
        _stateMachine.SetState((uint)Context.StateIndex.Idle);
        _stateMachine.CurrentState.OnStateEnter();
    }

    public override void SetStateToHolster()
    {
        _stateMachine.CurrentState.OnStateExit();
        _stateMachine.SetState((uint)Context.StateIndex.Holster);
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
        _stateMachine.RollbackToTick(tick);
    }

    public override void OnUpdate(PlayerInput input, float deltaTime)
    {
        _stateMachine.OnUpdate(input, deltaTime);
    }

    public override void SpawnBulletTrail(Vector3 start, Vector3 end)
    {
        if (_context.BulletTrailPrefab != null)
        {
            var bulletTrail = Instantiate(_context.BulletTrailPrefab);
            bulletTrail.Init(0.2f, start, end);
        }
    }
}
