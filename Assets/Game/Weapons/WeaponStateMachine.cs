using System;
using System.Collections.Generic;
using RingBuffer;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public struct WeaponInput : INetworkSerializable
{
    public ulong Tick;
    public Vector3 InputCameraDir;
    public bool InputDownWeaponShoot;
    public bool InputHoldWeaponAim;
    public bool InputDownWeaponReload;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Tick);
        serializer.SerializeValue(ref InputCameraDir);
        serializer.SerializeValue(ref InputDownWeaponShoot);
        serializer.SerializeValue(ref InputHoldWeaponAim);
        serializer.SerializeValue(ref InputDownWeaponReload);
    }

    public void ResetInputDown()
    {
        InputDownWeaponShoot = false;
        InputDownWeaponReload = false;
    }
}

public enum WeaponTickDataType : ulong
{
    GunPistol,
}

public interface IWeaponTickData
{
    public WeaponTickDataHeader GetHeader();
}

public struct WeaponTickDataHeader : INetworkSerializeByMemcpy
{
    public ulong Type;
    public ulong Tick;
    public uint StateIndex;
}

public abstract class WeaponContext<TickData> where TickData : struct, IWeaponTickData
{
    public WeaponState<TickData>[] States;
    public uint CurrentStateIndex;
    public ulong CommonFlags;

    public InputAction InputWeaponShoot;
    public InputAction InputWeaponAim;
    public InputAction InputWeaponReload;

    public virtual void Init(WeaponStateMachine<TickData> stateMachine)
    {
        if (stateMachine.Player.IsOwner)
        {
            InputWeaponShoot = InputSystem.actions.FindAction("Player/WeaponShoot");
            InputWeaponAim = InputSystem.actions.FindAction("Player/WeaponAim");
            InputWeaponReload = InputSystem.actions.FindAction("Player/WeaponReload");
        }
    }

    public abstract TickData GetTickData(ulong tick);
    public abstract void ApplyTickData<T>(T tickData);

    public WeaponState<TickData> GetState<T>(T index) where T : Enum
    {
        return States[(int)(object)index];
    }
    public abstract uint GetNextState(WeaponStateMachine<TickData> stateMachine, WeaponInput input);
}

// NOTE:
// WeaponState는 Context에 있는 정보만 읽고 써야한다.
// 그렇지 않으면 Rollback이 제대로 되지 않는다.
public class WeaponState<TickData> where TickData : struct, IWeaponTickData
{
    public WeaponStateMachine<TickData> StateMachine;

    public virtual void Init(WeaponStateMachine<TickData> stateMachine)
    {
        StateMachine = stateMachine;
    }

    public virtual void Rollback<T>(WeaponStateMachine<TickData> stateMachine, T correctTickData)
    where T : struct, IWeaponTickData
    { }

    public virtual bool IsRestart() => true;
    public virtual bool IsDone() => false;

    public virtual void OnStateEnter() { }
    public virtual void OnStateExit() { }
    public virtual void OnStateUpdate(WeaponInput input, float deltaTime) { }
}

public class WeaponStateMachine<TickData> where TickData : struct, IWeaponTickData
{
    public Player Player;
    public WeaponContext<TickData> Context;
    public WeaponState<TickData> CurrentState;

    public TickData? LatestTickData = null;
    public RingBuffer<WeaponInput> InputBuffer = new(20);
    public RingBuffer<TickData> TickBuffer = new(20);

    public void Init(Player player, WeaponContext<TickData> context)
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

    public void PushTickData(WeaponInput input, TickData tickData)
    {
        if (InputBuffer.Count == InputBuffer.Capacity)
            InputBuffer.PopFirst();
        InputBuffer.Add(input);

        if (TickBuffer.Count == TickBuffer.Capacity)
            InputBuffer.PopFirst();
        TickBuffer.Add(tickData);
    }

    public int GetTickDataIndexFromBuffer(ulong tick)
    {
        for (var i = 0; i < TickBuffer.Count; ++i)
        {
            if (TickBuffer[i].GetHeader().Tick == tick)
                return i;
        }
        return -1;
    }

    public void DoTransition(WeaponInput input)
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

    }

    public virtual void OnUpdate(WeaponInput input, float deltaTime)
    {
        if (CurrentState is null)
        {
            Debug.LogError("CurrentState is null");
            return;
        }

        DoTransition(input);
        CurrentState.OnStateUpdate(input, deltaTime);
    }
}
