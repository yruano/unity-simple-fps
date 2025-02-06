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
    public uint Type;
    public uint StateIndex;
}

public abstract class WeaponContext<TickData> where TickData : struct, IWeaponTickData
{
    public WeaponStateMachine<TickData> StateMachine;
    public WeaponState<TickData>[] States;
    public ulong CommonFlags;
    public uint CurrentStateIndex;

    public virtual void Init(WeaponStateMachine<TickData> stateMachine)
    {
        StateMachine = stateMachine;
    }

    public abstract TickData GetTickData(ulong tick);
    public abstract void ApplyTickData<T>(T tickData);

    public WeaponState<TickData> GetState<T>(T index) where T : Enum
    {
        return States[(int)(object)index];
    }
    public abstract uint GetNextState(WeaponStateMachine<TickData> stateMachine, PlayerInput input);
}

public struct RollbackData
{
    public ulong Tick;
    public Action Rollback;
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

    public virtual bool IsRestart() => true;
    public virtual bool IsDone() => false;

    public virtual void OnStateEnter() { }
    public virtual void OnStateExit() { }
    public virtual void OnStateUpdate(PlayerInput input, float deltaTime) { }
}

public class WeaponStateMachine<TickData> where TickData : struct, IWeaponTickData
{
    public Player Player;
    public WeaponContext<TickData> Context;
    public WeaponState<TickData> CurrentState;

    public TickData? LatestTickData = null;
    public RingBuffer<TickData> TickBuffer = new(20);
    public Stack<RollbackData> RollbackBuffer = new();

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
                SetCurrentState(nextState);
                CurrentState.OnStateEnter();
            }
        }

        CurrentState.OnStateUpdate(input, deltaTime);
    }

    public void RollbackTick(ulong tick)
    {
        while (RollbackBuffer.Count > 0)
        {
            if (RollbackBuffer.Peek().Tick < tick)
                break;

            var data = RollbackBuffer.Pop();
            data.Rollback();
        }
    }
}
