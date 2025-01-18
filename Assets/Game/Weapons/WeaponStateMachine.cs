using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public struct WeaponInput : INetworkSerializeByMemcpy
{
    public Vector3 InputCameraDir;
    public bool InputWeaponShoot;
    public bool InputWeaponAim;
    public bool InputWeaponReload;
}

public class WeaponState
{
    public int Id;
    public WeaponStateMachine StateMachine;
    public Func<WeaponStateMachine, bool> IsRestart = (_) => true;
    public Func<WeaponStateMachine, bool> IsDone = (_) => false;

    public virtual void Init(WeaponStateMachine stateMachine)
    {
        StateMachine = stateMachine;
        Id = StateMachine.StateCount++;
    }

    public virtual void OnStateEnter() { }
    public virtual void OnStateExit() { }
    public virtual void OnStateUpdate() { }
    public virtual void OnStateFixedUpdate() { }
}

public abstract class WeaponStateTransition
{
    public abstract WeaponState GetNextState(WeaponStateMachine stateMachine);
}

public class WeaponStateMachineContext
{
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
}

public class WeaponStateMachine : MonoBehaviour
{
    [HideInInspector] public Player Player;
    [HideInInspector] public int StateCount = 0;
    public WeaponInput ClientInput;
    public WeaponStateMachineContext Context;
    public WeaponState CurrentState;
    public WeaponStateTransition CurrentTransition;

    protected virtual void Start()
    {
        Context.Init(this);
    }

    public virtual void OnUpdate()
    {
        // FIXME: Start gets run after Update...
        if (CurrentState is null)
        {
            return;
        }

        if (CurrentState is null)
        {
            Debug.LogError("CurrentState is null!");
            Debug.Break();
        }
        else
        {
            CurrentState.OnStateUpdate();

            if (CurrentTransition is null)
            {
                Debug.LogError("CurrentTransition is null!");
                Debug.Break();
            }
            else
            {
                var nextState = CurrentTransition.GetNextState(this);
                if (nextState is not null)
                {
                    if (nextState == CurrentState)
                    {
                        if (CurrentState.IsRestart(this))
                        {
                            CurrentState.OnStateExit();
                            CurrentState.OnStateEnter();
                        }
                    }
                    else
                    {
                        CurrentState.OnStateExit();
                        CurrentState = nextState;
                        CurrentState.OnStateEnter();
                    }
                }
            }
        }
    }

    public virtual void OnFixedUpdate()
    {
        if (CurrentState is null)
        {
            return;
        }

        if (CurrentState is null)
        {
            Debug.LogError("Current State is null!");
            Debug.Break();
        }
        else
        {
            CurrentState.OnStateFixedUpdate();
        }
    }
}
