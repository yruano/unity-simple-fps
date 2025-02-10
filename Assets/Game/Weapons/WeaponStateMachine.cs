using System;
using System.Collections.Generic;
using RingBuffer;
using UnityEngine;
using Unity.Netcode;

public enum WeaponType : uint
{
    None,
    GunPistol,
    GunAssaultRifle,
}

public interface IWeaponTickData
{
    public WeaponTickDataHeader GetHeader();
}

public struct WeaponTickDataHeader : INetworkSerializeByMemcpy
{
    public ulong Tick;
    public WeaponType Type;
    public uint StateIndex;
}

public abstract class WeaponContext<TickData> where TickData : struct, IWeaponTickData
{
    [HideInInspector] public WeaponType WeaponType;
    [HideInInspector] public WeaponStateMachine<TickData> StateMachine;
    [HideInInspector] public WeaponState<TickData>[] States;
    [HideInInspector] public uint CurrentStateIndex;

    public virtual void Init(WeaponType weaponType, WeaponStateMachine<TickData> stateMachine)
    {
        WeaponType = weaponType;
        StateMachine = stateMachine;
        Reset();
    }
    public abstract void Reset();

    public WeaponTickDataHeader GetTickDataHeader(ulong tick)
    {
        return new WeaponTickDataHeader
        {
            Tick = tick,
            Type = WeaponType,
            StateIndex = CurrentStateIndex,
        };
    }
    public abstract TickData GetTickData(ulong tick);
    public abstract void ApplyTickData<T>(T tickData);

    public WeaponState<TickData> GetState<T>(T index) where T : Enum
    {
        return States[(int)(object)index];
    }
    public abstract uint GetNextState(WeaponStateMachine<TickData> stateMachine, PlayerInput input);
}

public class WeaponState<TickData> where TickData : struct, IWeaponTickData
{
    public WeaponStateMachine<TickData> StateMachine;

    public WeaponState(WeaponStateMachine<TickData> stateMachine)
    {
        StateMachine = stateMachine;
    }

    public virtual bool IsRestart() => true;
    public virtual bool IsDone() => false;

    public virtual void OnStateEnter() { }
    public virtual void OnStateExit() { }
    public virtual void OnStateUpdate(PlayerInput input, float deltaTime) { }
}

public struct WeaponRollbackData
{
    public ulong Tick;
    public Action FnRollback;
}

public class WeaponStateMachine<TickData> where TickData : struct, IWeaponTickData
{
    public Player Player;
    public WeaponContext<TickData> Context;
    public WeaponState<TickData> CurrentState;

    public TickData? LatestTickData = null;
    public RingBuffer<TickData> TickBuffer = new(20);
    public Stack<WeaponRollbackData> RollbackBuffer = new();

    public void Init(Player player, WeaponContext<TickData> context, WeaponType weaponType)
    {
        Player = player;
        Context = context;
        Context.Init(weaponType, this);
    }

    public void SetState(uint stateIndex)
    {
        Context.CurrentStateIndex = stateIndex;
        CurrentState = Context.States[stateIndex];
    }

    public void PushTickData(TickData tickData)
    {
        if (TickBuffer.Count == TickBuffer.Capacity)
            TickBuffer.PopFirst();
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

    public virtual void OnUpdate(PlayerInput input, float deltaTime)
    {
        if (CurrentState is null)
        {
            Debug.LogError("CurrentState is null");
            return;
        }

        // TODO: TickData에서 이전 상태 인덱스를 보내줘야 함.
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
                SetState(nextState);
                CurrentState.OnStateEnter();
            }
        }

        CurrentState.OnStateUpdate(input, deltaTime);
    }

    public void RollbackToTick(ulong tick)
    {
        while (RollbackBuffer.Count > 0)
        {
            if (RollbackBuffer.Peek().Tick < tick)
                break;

            var data = RollbackBuffer.Pop();
            data.FnRollback();
        }
    }
}
