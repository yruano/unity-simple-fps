using System;
using System.Runtime.InteropServices;
using UnityEngine;
using Unity.Netcode;

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
                Type = (ulong)WeaponTickDataType.GunPistol,
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
        if (tickData is WeaponTickDataGunPistol tickDataGunPistol)
        {
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

    public override uint GetNextState(WeaponStateMachine<WeaponTickDataGunPistol> stateMachine, WeaponInput input)
    {
        var ctx = stateMachine.Context as WeaponContextGunPistol;

        switch (ctx.CurrentStateIndex)
        {
            case (uint)StateIndex.Idle:
                if (ctx.AmmoCount > 0 && input.InputWeaponShoot)
                {
                    return (uint)StateIndex.Shoot;
                }

                if (ctx.AmmoCount < ctx.MagazineSize && input.InputWeaponReload)
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
    private WeaponInput _input;
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
                        player.Health -= _damage;
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

    public override void OnStateUpdate(WeaponInput input, float deltaTime)
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

    public override void OnStateUpdate(WeaponInput input, float deltaTime)
    {
        var ctx = StateMachine.Context as WeaponContextGunPistol;
        ctx.ReloadTimer.Tick(deltaTime);
    }
}

public class WeaponGunPistol : Weapon
{
    private readonly WeaponContextGunPistol _context = new();
    private readonly WeaponStateMachine<WeaponTickDataGunPistol> _stateMachine = new();

    public override void Init(Player player)
    {
        _player = player;
        _stateMachine.Init(player, _context);
    }

    public override void ResetWeapon()
    {
        _tick = 0;

        // Reset state machine
        _stateMachine.SetCurrentState((uint)WeaponContextGunPistol.StateIndex.Idle);
        _stateMachine.InputBuffer.Clear();
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

    private void Update()
    {
        if (!_player.IsSpawned)
            return;

        if (_player.IsDead)
            return;

        if (_player.IsOwner)
        {
            _tick += 1;

            // Get user input.
            var input = new WeaponInput
            {
                Tick = _tick,
                DeltaTime = Time.deltaTime,
                InputCameraDir = _player.GetCameraDir(),
                InputWeaponShoot = _context.InputWeaponShoot.IsPressed(),
                InputWeaponAim = _context.InputWeaponAim.IsPressed(),
                InputWeaponReload = _context.InputWeaponReload.IsPressed()
            };

            if (_stateMachine.Player.IsHost)
            {
                // Run state machine.
                _stateMachine.OnUpdate(input, Time.deltaTime);
            }
            else
            {
                // Send user input.
                _player.SendWeaponInputToServerRpc(input);

                // Run state machine. (client-side prediction)
                _stateMachine.OnUpdate(input, Time.deltaTime);

                // Store tick data.
                _stateMachine.PushTickData(input, _stateMachine.Context.GetTickData(_tick));

                // Reconcile.
                Reconcile();
            }
        }
    }

    private void FixedUpdate()
    {
        if (_player.IsHost)
        {
            if (_player.IsDead)
                return;

            // Process input.
            ulong lastProcessedTick = 0;
            while (_player.RecivedWeaponInputs.Count > 0)
            {
                var input = _player.RecivedWeaponInputs.Dequeue();
                _stateMachine.OnUpdate(input, input.DeltaTime);

                lastProcessedTick = input.Tick;
            }

            // Send state to client.
            var tickData = _stateMachine.Context.GetTickData(lastProcessedTick);
            _player.SendWeaponStateToOwnerRpc(tickData.Serialize());

            // TODO: 다른 캐릭터의 데이터도 동기화 (이펙트랑 애니만??)
        }
    }

    private void Reconcile()
    {
        var serverTickDataOpt = _stateMachine.LatestTickData;
        if (serverTickDataOpt is { } serverTickData)
        {
            _stateMachine.LatestTickData = null;

            var i = _stateMachine.GetTickDataIndexFromBuffer(serverTickData.Header.Tick);
            if (i == -1) return;

            var predictedTickData = _stateMachine.TickBuffer[i];

            // Remove old data.
            _stateMachine.InputBuffer.RemoveRange(0, i + 1);
            _stateMachine.TickBuffer.RemoveRange(0, i + 1);

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
                for (var j = 0; j < _stateMachine.InputBuffer.Count; ++j)
                {
                    var input = _stateMachine.InputBuffer[j];
                    _stateMachine.OnUpdate(input, input.DeltaTime);
                    _stateMachine.TickBuffer[j] = _stateMachine.Context.GetTickData(input.Tick);
                }
            }
        }
    }
}
