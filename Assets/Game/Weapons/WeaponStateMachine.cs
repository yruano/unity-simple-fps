using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class WeaponState
{
    public WeaponStateMachine StateMachine;
    public Func<WeaponStateMachine, bool> IsRestart = (_) => true;
    public Func<WeaponStateMachine, bool> IsDone = (_) => false;

    public virtual void Init(WeaponStateMachine stateMachine)
    {
        StateMachine = stateMachine;
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
        InputWeaponShoot = InputSystem.actions.FindAction("Player/WeaponShoot");
        InputWeaponAim = InputSystem.actions.FindAction("Player/WeaponAim");
        InputWeaponReload = InputSystem.actions.FindAction("Player/WeaponReload");
    }
}

public class WeaponStateMachine : MonoBehaviour
{
    [HideInInspector]
    public Player Player;
    public WeaponStateMachineContext Context;
    public WeaponState CurrentState;
    public WeaponStateTransition CurrentTransition;

    protected virtual void Start()
    {
        Context.Init(this);
    }

    protected virtual void Update()
    {
        OnUpdate();
    }

    protected virtual void FixedUpdate()
    {
        OnFixedUpdate();
    }

    public virtual void OnUpdate()
    {
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
            Debug.LogError("Current State is null!");
            Debug.Break();
        }
        else
        {
            CurrentState.OnStateFixedUpdate();
        }
    }
}
