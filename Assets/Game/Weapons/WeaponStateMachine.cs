using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public struct WeaponInput : INetworkSerializeByMemcpy
{
    public UInt64 Tick;
    public float DeltaTime;
    public Vector3 InputCameraDir;
    public bool InputWeaponShoot;
    public bool InputWeaponAim;
    public bool InputWeaponReload;
}

public class WeaponState
{
    public WeaponStateMachine StateMachine;

    public virtual void Init(WeaponStateMachine stateMachine)
    {
        StateMachine = stateMachine;
    }
    public virtual void Rollback(WeaponStateMachine stateMachine) { }

    public virtual bool IsRestart() => true;
    public virtual bool IsDone() => false;

    public virtual void OnStateEnter() { }
    public virtual void OnStateExit() { }
    public virtual void OnStateUpdate(WeaponInput input, float deltaTime) { }
}

public enum WeaponTickDataType
{
    GunPistol,
}

[StructLayout(LayoutKind.Sequential)]
public class WeaponTickData
{
    public UInt64 Type;
    public UInt64 Tick;

    public virtual byte[] Serialize()
    {
        return null;
    }
}

public class WeaponContext
{
    // 무기 State machine은 Context에 있는 정보만 읽고 써야한다.
    // 그렇지 않으면 Rollback을 제대로 못한다.

    public WeaponState[] States;
    public int CurrentStateIndex;
    public UInt64 CommonFlags;

    public InputAction InputWeaponShoot;
    public InputAction InputWeaponAim;
    public InputAction InputWeaponReload;

    public virtual void Init(WeaponStateMachine stateMachine)
    {
        if (stateMachine.Player.IsOwner)
        {
            InputWeaponShoot = InputSystem.actions.FindAction("Player/WeaponShoot");
            InputWeaponAim = InputSystem.actions.FindAction("Player/WeaponAim");
            InputWeaponReload = InputSystem.actions.FindAction("Player/WeaponReload");
        }
    }

    public WeaponState GetState<T>(T index) where T : Enum
    {
        return States[(int)(object)index];
    }

    public virtual WeaponTickData GetTickData(UInt64 tick)
    {
        return null;
    }

    public virtual void ApplyTickData(WeaponTickData tickData) { }

    public virtual int GetNextState(WeaponStateMachine stateMachine, WeaponInput input)
    {
        return 0;
    }
}

public class WeaponStateMachine
{
    public Player Player;
    public WeaponContext Context;
    public WeaponState CurrentState;

    public List<WeaponInput> InputBuffer = new();
    public List<WeaponTickData> TickBuffer = new();

    public void Init(Player player, WeaponContext context)
    {
        Player = player;
        Context = context;
        Context.Init(this);
    }

    public void SetCurrentState(int stateIndex)
    {
        Context.CurrentStateIndex = stateIndex;
        CurrentState = Context.States[stateIndex];
    }

    public void PushTickData(WeaponInput input, WeaponTickData tickData)
    {
        InputBuffer.Add(input);
        if (InputBuffer.Count >= 30)
            InputBuffer.RemoveAt(0);

        TickBuffer.Add(tickData);
        if (TickBuffer.Count >= 30)
            TickBuffer.RemoveAt(0);
    }

    public int GetTickDataIndexFromBuffer(UInt64 tick)
    {
        return TickBuffer.FindIndex(item => item.Tick == tick);
    }

    public virtual void OnUpdate(WeaponInput input, float deltaTime)
    {
        // FIXME: Start gets run after Update...
        if (CurrentState is null)
        {
            return;
        }

        var nextState = Context.GetNextState(this, input);
        if (nextState != 0)
        {
            if (nextState == Context.CurrentStateIndex)
            {
                if (CurrentState.IsRestart())
                {
                    CurrentState.OnStateExit();
                    CurrentState.OnStateEnter();
                }
            }
            else
            {
                CurrentState.OnStateExit();
                SetCurrentState(nextState);
                CurrentState.OnStateEnter();
            }
        }

        CurrentState.OnStateUpdate(input, deltaTime);
    }
}
