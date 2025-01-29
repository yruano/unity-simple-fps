using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public struct WeaponInput : INetworkSerializeByMemcpy
{
    public ulong Tick;
    public float DeltaTime;
    public Vector3 InputCameraDir;
    public bool InputWeaponShoot;
    public bool InputWeaponAim;
    public bool InputWeaponReload;
}

public enum WeaponTickDataType : ulong
{
    GunPistol,
}

[StructLayout(LayoutKind.Sequential)]
public abstract class WeaponTickData
{
    public ulong Type;
    public ulong Tick;
    public uint StateIndex;

    public abstract byte[] Serialize();

    public virtual bool IsEqual(WeaponTickData other)
    {
        if (other == null)
            return false;

        return Type == other.Type
            && Tick == other.Tick
            && StateIndex == other.StateIndex;
    }
}

public class WeaponState
{
    public WeaponStateMachine StateMachine;

    public virtual void Init(WeaponStateMachine stateMachine)
    {
        StateMachine = stateMachine;
    }
    public virtual void Rollback(WeaponStateMachine stateMachine, WeaponTickData correctTickData) { }

    public virtual bool IsRestart() => true;
    public virtual bool IsDone() => false;

    public virtual void OnStateEnter() { }
    public virtual void OnStateExit() { }
    public virtual void OnStateUpdate(WeaponInput input, float deltaTime) { }
}

// NOTE:
// WeaponState는 Context에 있는 정보만 읽고 써야한다.
// 그렇지 않으면 Rollback을 제대로 할 수 없다.
public class WeaponContext
{
    public WeaponState[] States;
    public uint CurrentStateIndex;
    public ulong CommonFlags;

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

    public virtual WeaponTickData GetTickData(ulong tick)
    {
        return null;
    }

    public virtual void ApplyTickData(WeaponTickData tickData) { }

    public virtual uint GetNextState(WeaponStateMachine stateMachine, WeaponInput input)
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

    public void SetCurrentState(uint stateIndex)
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

    public int GetTickDataIndexFromBuffer(ulong tick)
    {
        return TickBuffer.FindIndex(item => item.Tick == tick);
    }

    public virtual void OnUpdate(WeaponInput input, float deltaTime)
    {
        if (CurrentState is null)
        {
            Debug.LogError("CurrentState is null");
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
