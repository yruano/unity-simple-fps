using System;
using System.Runtime.InteropServices;
using UnityEngine;
using Unity.Netcode;

public class WeaponStateGunPistolIdle : WeaponState
{
}

public class WeaponStateGunPistolShoot : WeaponState
{
    private WeaponInput _input;

    public override void Init(WeaponStateMachine stateMachine)
    {
        base.Init(stateMachine);

        var ctx = StateMachine.Context as WeaponContextGunPistol;
        ctx.ShootTimer.RegisterCallback(0, (_) =>
        {
            if (ctx.AmmoCount == 0)
            {
                return;
            }

            ctx.AmmoCount -= 1;

            // 이펙트 및 애니메이션 실행 (클라 예측)

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

    public override void Rollback(WeaponStateMachine stateMachine)
    {
        var ctx = StateMachine.Context as WeaponContextGunPistol;
        ctx.ShootTimer.Reset();
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
}

[StructLayout(LayoutKind.Sequential)]
public class WeaponTickDataGunPistol : WeaponTickData
{
    public int StateIndex;
    public int MagazineSize;
    public int AmmoCount;
    public float ShootTimerDuration;
    public float ShootTimerTime;

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
            result = writer.ToArray();
        }
        return result;
    }

    public static WeaponTickDataGunPistol NewFromReader(UInt64 type, UInt64 tick, FastBufferReader reader)
    {
        var result = new WeaponTickDataGunPistol
        {
            Type = type,
            Tick = tick,
        };
        reader.ReadValue(out result.StateIndex);
        reader.ReadValue(out result.MagazineSize);
        reader.ReadValue(out result.AmmoCount);
        reader.ReadValue(out result.ShootTimerDuration);
        reader.ReadValue(out result.ShootTimerTime);
        return result;
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

    public override WeaponTickData GetTickData(UInt64 tick)
    {
        return new WeaponTickDataGunPistol
        {
            Type = (UInt64)WeaponTickDataType.GunPistol,
            Tick = tick,
            StateIndex = CurrentStateIndex,
            MagazineSize = MagazineSize,
            AmmoCount = AmmoCount,
            ShootTimerDuration = ShootTimer.Duration,
            ShootTimerTime = ShootTimer.Time,
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
        }
        else
        {
            Debug.LogError("ApplyTickData failed: Wrong type.");
        }
    }

    public override int GetNextState(WeaponStateMachine stateMachine, WeaponInput input)
    {
        var ctx = stateMachine.Context as WeaponContextGunPistol;

        if (stateMachine.CurrentState is WeaponStateGunPistolIdle)
        {
            if (ctx.AmmoCount > 0 && input.InputWeaponShoot)
            {
                return (int)StateIndex.Shoot;
            }
        }

        if (stateMachine.CurrentState is WeaponStateGunPistolShoot)
        {
            if (stateMachine.CurrentState.IsDone())
            {
                return (int)StateIndex.Idle;
            }
        }

        return 0;
    }
}

public class WeaponGunPistol : Weapon
{
    private WeaponStateMachine _stateMachine = new();
    private WeaponContextGunPistol _context = new();
    private UInt64 _tick = 0;

    public override void Init(Player player)
    {
        _stateMachine.Init(player, _context);
    }

    private void Update()
    {
        if (NetworkManager.Singleton == null)
        {
            return;
        }

        if (_stateMachine.Player.IsOwner)
        {
            _tick += 1;

            // Get user input.
            var input = new WeaponInput
            {
                Tick = _tick,
                DeltaTime = Time.deltaTime,
                InputCameraDir = _stateMachine.Player.GetCameraDir(),
                InputWeaponShoot = _context.InputWeaponShoot.IsPressed(),
                InputWeaponAim = _context.InputWeaponAim.IsPressed(),
                InputWeaponReload = _context.InputWeaponReload.IsPressed()
            };

            if (_stateMachine.Player.IsHost)
            {
                _stateMachine.OnUpdate(input, Time.deltaTime);
            }
            else
            {
                // Send user input.
                _stateMachine.Player.SendWeaponInputToServerRpc(input);

                // Client-side prediction.
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
        if (_stateMachine.Player.IsHost)
        {
            // Process input.
            UInt64 lastProcessedTick = 0;
            while (_stateMachine.Player.RecivedWeaponInputs.Count > 0)
            {
                var input = _stateMachine.Player.RecivedWeaponInputs.Dequeue();
                _stateMachine.OnUpdate(input, input.DeltaTime);

                lastProcessedTick = input.Tick;
            }

            // Send state to client.
            var tickData = _stateMachine.Context.GetTickData(lastProcessedTick);
            _stateMachine.Player.SendWeaponStateToOwnerRpc(tickData.Serialize());

            // TODO: 다른 캐릭터의 데이터도 동기화 (이펙트랑 애니만??)
        }
    }

    private void Reconcile()
    {
        var serverTickData = _stateMachine.Player.LatestWeaponTickData as WeaponTickDataGunPistol;
        if (serverTickData is not null)
        {
            _stateMachine.Player.LatestWeaponTickData = null;

            var i = _stateMachine.GetTickDataIndexFromBuffer(serverTickData.Tick);
            if (i == -1) return;

            var predictedTickData = _stateMachine.TickBuffer[i] as WeaponTickDataGunPistol;

            // Remove old data.
            _stateMachine.InputBuffer.RemoveRange(0, i + 1);
            _stateMachine.TickBuffer.RemoveRange(0, i + 1);

            var isEqual = true;
            isEqual = isEqual && (serverTickData.Type == predictedTickData.Type);
            isEqual = isEqual && (serverTickData.StateIndex == predictedTickData.StateIndex);
            isEqual = isEqual && (serverTickData.MagazineSize == predictedTickData.MagazineSize);
            isEqual = isEqual && (serverTickData.AmmoCount == predictedTickData.AmmoCount);
            isEqual = isEqual && (serverTickData.ShootTimerDuration == predictedTickData.ShootTimerDuration);
            isEqual = isEqual && (serverTickData.ShootTimerTime == predictedTickData.ShootTimerTime);

            // Check prediction.
            if (isEqual)
            {
                // Prediction success.
                Debug.Log("Prediction success");
            }
            else
            {
                // Prediction failed.
                Debug.Log("Prediction failed");

                // Rollback.
                foreach (var tickData in _stateMachine.TickBuffer)
                {
                    var tickDataGunPistol = tickData as WeaponTickDataGunPistol;
                    // TODO: 상태 중간까지는 에측이 성공했을 경우 잘못된 시점 부터만 롤백해야 함.
                    _stateMachine.Context.States[tickDataGunPistol.StateIndex].Rollback(_stateMachine);
                }
                _stateMachine.Context.ApplyTickData(serverTickData);

                // Resimulate.
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
