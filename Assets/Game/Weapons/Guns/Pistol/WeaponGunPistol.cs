using System;
using System.Runtime.InteropServices;
using UnityEngine;
using Unity.Netcode;

[StructLayout(LayoutKind.Sequential)]
public class WeaponTickDataGunPistol : WeaponTickData
{
    public int MagazineSize;
    public int AmmoCount;
    public float ShootTimerDuration;
    public float ShootTimerTime;
    public int ShootTimerCallbackIndex;

    public static WeaponTickDataGunPistol NewFromReader(ulong type, ulong tick, uint stateIndex, FastBufferReader reader)
    {
        var result = new WeaponTickDataGunPistol
        {
            Type = type,
            Tick = tick,
            StateIndex = stateIndex,
        };
        reader.ReadValue(out result.MagazineSize);
        reader.ReadValue(out result.AmmoCount);
        reader.ReadValue(out result.ShootTimerDuration);
        reader.ReadValue(out result.ShootTimerTime);
        reader.ReadValue(out result.ShootTimerCallbackIndex);
        return result;
    }

    public override byte[] Serialize()
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
            writer.WriteValue(Type);
            writer.WriteValue(Tick);
            writer.WriteValue(StateIndex);
            writer.WriteValue(MagazineSize);
            writer.WriteValue(AmmoCount);
            writer.WriteValue(ShootTimerDuration);
            writer.WriteValue(ShootTimerTime);
            writer.WriteValue(ShootTimerCallbackIndex);
            result = writer.ToArray();
        }
        return result;
    }

    public override bool IsEqual(WeaponTickData other)
    {
        if (other is WeaponTickDataGunPistol otherGunPistol)
        {
            var result = base.IsEqual(other);
            if (!result) return false;

            result = result && StateIndex == otherGunPistol.StateIndex;
            result = result && MagazineSize == otherGunPistol.MagazineSize;
            result = result && AmmoCount == otherGunPistol.AmmoCount;
            result = result && ShootTimerDuration == otherGunPistol.ShootTimerDuration;
            result = result && ShootTimerTime == otherGunPistol.ShootTimerTime;
            result = result && ShootTimerCallbackIndex == otherGunPistol.ShootTimerCallbackIndex;
            return result;
        }
        else
        {
            return false;
        }
    }
}

public class WeaponStateGunPistolIdle : WeaponState { }

public class WeaponStateGunPistolShoot : WeaponState
{
    private WeaponInput _input;

    public override void Init(WeaponStateMachine stateMachine)
    {
        base.Init(stateMachine);

        var ctx = StateMachine.Context as WeaponContextGunPistol;

        ctx.ShootTimer.AddCallback(0, (_) =>
        {
            if (ctx.AmmoCount == 0)
                return;

            ctx.AmmoCount -= 1;

            // 이펙트 및 애니메이션 실행 (클라 예측)

            // Hit check.
            if (NetworkManager.Singleton.IsHost)
            {
                var rayPos = StateMachine.Player.GetHeadPosition();
                var rayDir = _input.InputCameraDir;
                var rayDist = 100f;

                Debug.DrawRay(rayPos, rayDir * rayDist, Color.red, 2);

                if (Physics.Raycast(rayPos, rayDir, out var rayHitInfo, rayDist))
                {
                    var collider = rayHitInfo.collider;
                    if (collider != StateMachine.Player && collider.CompareTag("Player"))
                    {
                        var player = collider.GetComponent<Player>();
                        player.Health -= 20;
                    }
                }
            }
        });
    }

    public override void Rollback(WeaponStateMachine stateMachine, WeaponTickData correctTickData)
    {
        var ctx = StateMachine.Context as WeaponContextGunPistol;
        var correctTickDataGunPistol = correctTickData as WeaponTickDataGunPistol;

        var correctTimerTime = correctTickDataGunPistol.ShootTimerTime;
        ctx.ShootTimer.RollbackTo(correctTimerTime);
        // TODO: 이미 실행된 것들도 올바른 시간으로 되돌려야 함.
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

public class WeaponStateGunPistolReload : WeaponState
{
    public override bool IsDone()
    {
        return true;
    }

    public override void OnStateEnter()
    {
        var ctx = StateMachine.Context as WeaponContextGunPistol;
        ctx.AmmoCount = ctx.MagazineSize;
    }
}

public class WeaponContextGunPistol : WeaponContext
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

    public override void Init(WeaponStateMachine stateMachine)
    {
        base.Init(stateMachine);

        AmmoCount = MagazineSize;

        States = new WeaponState[]
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

    public override WeaponTickData GetTickData(ulong tick)
    {
        return new WeaponTickDataGunPistol
        {
            Type = (ulong)WeaponTickDataType.GunPistol,
            Tick = tick,
            StateIndex = CurrentStateIndex,
            MagazineSize = MagazineSize,
            AmmoCount = AmmoCount,
            ShootTimerDuration = ShootTimer.Duration,
            ShootTimerTime = ShootTimer.Time,
            ShootTimerCallbackIndex = ShootTimer.CallbackIndex,
        };
    }

    public override void ApplyTickData(WeaponTickData tickData)
    {
        if (tickData is WeaponTickDataGunPistol tickDataGunPistol)
        {
            CurrentStateIndex = tickDataGunPistol.StateIndex;
            MagazineSize = tickDataGunPistol.MagazineSize;
            AmmoCount = tickDataGunPistol.AmmoCount;
            ShootTimer.Duration = tickDataGunPistol.ShootTimerDuration;
            ShootTimer.Time = tickDataGunPistol.ShootTimerTime;
            ShootTimer.CallbackIndex = tickDataGunPistol.ShootTimerCallbackIndex;
        }
        else
        {
            Debug.LogError("ApplyTickData failed: Wrong type.");
        }
    }

    public override uint GetNextState(WeaponStateMachine stateMachine, WeaponInput input)
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

public class WeaponGunPistol : Weapon
{
    private Player _player;
    private WeaponStateMachine _stateMachine = new();
    private WeaponContextGunPistol _context = new();
    private ulong _tick = 0;

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
        _context.CurrentStateIndex = (uint)WeaponContextGunPistol.StateIndex.Idle;
        _context.AmmoCount = _context.MagazineSize;
        _context.ShootTimer.Reset();
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
        var serverTickData = _player.LatestWeaponTickData;
        if (serverTickData is not null)
        {
            _player.LatestWeaponTickData = null;

            var i = _stateMachine.GetTickDataIndexFromBuffer(serverTickData.Tick);
            if (i == -1) return;

            var predictedTickData = _stateMachine.TickBuffer[i];

            // Remove old data.
            _stateMachine.InputBuffer.RemoveRange(0, i + 1);
            _stateMachine.TickBuffer.RemoveRange(0, i + 1);

            // Check prediction.
            if (!serverTickData.IsEqual(predictedTickData))
            {
                // Rollback.
                foreach (var tickData in _stateMachine.TickBuffer)
                {
                    _stateMachine.Context.States[tickData.StateIndex].Rollback(_stateMachine, serverTickData);
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
