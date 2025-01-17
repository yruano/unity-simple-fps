using UnityEngine;

public class StateGunPistolIdle : WeaponState
{
}

public class StateGunPistolShoot : WeaponState
{
    public override void Init(WeaponStateMachine stateMachine)
    {
        base.Init(stateMachine);

        IsDone = (stateMachine) =>
        {
            var ctx = StateMachine.Context as ContextGunPistol;
            return ctx.ShootTimer.IsEnded;
        };

        var ctx = StateMachine.Context as ContextGunPistol;
        ctx.ShootTimer.RegisterCallback(0, (_) =>
        {
            if (ctx.AmmoCount == 0)
            {
                return;
            }

            ctx.AmmoCount -= 1;

            var rayPos = StateMachine.Player.GetHeadPosition();
            var rayDir = StateMachine.Player.GetCameraDir();
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
        });
    }

    public override void OnStateExit()
    {
        var ctx = StateMachine.Context as ContextGunPistol;
        ctx.ShootTimer.Reset();
    }

    public override void OnStateUpdate()
    {
        var ctx = StateMachine.Context as ContextGunPistol;
        ctx.ShootTimer.Tick(Time.deltaTime);
    }
}

public class StateGunPistolReload : WeaponState
{
}

public class TransitionPisolNormal : WeaponStateTransition
{
    public override WeaponState GetNextState(WeaponStateMachine stateMachine)
    {
        var ctx = stateMachine.Context as ContextGunPistol;

        if (stateMachine.CurrentState is StateGunPistolIdle)
        {
            if (ctx.AmmoCount > 0 && ctx.InputWeaponShoot.IsPressed())
                return ctx.StateShoot;
        }

        if (stateMachine.CurrentState is StateGunPistolShoot)
        {
            if (stateMachine.CurrentState.IsDone(stateMachine))
                return ctx.StateIdle;
        }

        return null;
    }
}

public class ContextGunPistol : WeaponStateMachineContext
{
    public StateGunPistolIdle StateIdle = new();
    public StateGunPistolShoot StateShoot = new();
    public StateGunPistolReload StateReload = new();

    public TransitionPisolNormal TransitionNormal = new();

    public int MagazineSize = 7;
    public int AmmoCount;
    public GameTimer ShootTimer = new(0.3f);

    public override void Init(WeaponStateMachine stateMachine)
    {
        base.Init(stateMachine);

        StateIdle.Init(stateMachine);
        StateShoot.Init(stateMachine);
        StateReload.Init(stateMachine);

        AmmoCount = MagazineSize;

        stateMachine.CurrentState = StateIdle;
        stateMachine.CurrentTransition = TransitionNormal;
    }
}

public class WeaponGunPistol : MonoBehaviour
{
    private WeaponStateMachine _stateMachine;

    private void Awake()
    {
        Debug.Log("WeaponGunPistol");
        _stateMachine = GetComponent<WeaponStateMachine>();
        _stateMachine.Context = new ContextGunPistol();
    }
}
