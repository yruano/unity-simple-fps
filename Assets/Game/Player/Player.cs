using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Properties;
using Unity.Cinemachine;
using Unity.Netcode;

public class Player : NetworkBehaviour
{
    [SerializeField] private float WalkSpeed = 4.0f;

    [SerializeField] private CinemachineCamera PrefabCmFirstPersonCamera;

    private PlayerCameraTarget _cameraTarget;
    private CinemachineCamera _cmFirstPersonCamera;

    private Rigidbody _rb;

    private InputAction _inputMove;
    private InputAction _inputShoot;

    private NetworkVariable<int> _healthMax = new(100);
    [CreateProperty]
    public int HealthMax
    {
        get => _healthMax.Value;
        set => _healthMax.Value = Mathf.Max(value, 0);
    }

    private NetworkVariable<int> _health = new();
    [CreateProperty]
    public int Health
    {
        get => _health.Value;
        set => _health.Value = Mathf.Clamp(value, 0, HealthMax);
    }

    private int _bulletCount = 10;
    private int BulletCount
    {
        get => _bulletCount;
        set => _bulletCount = Mathf.Clamp(value, 0, 10);
    }

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();

        _inputMove = InputSystem.actions.FindAction("Player/Move");
        _inputShoot = InputSystem.actions.FindAction("Player/Shoot");
    }

    public override void OnNetworkSpawn()
    {
        if (IsHost)
        {
            Init();
        }

        if (IsOwner)
        {
            _cameraTarget = new GameObject().AddComponent<PlayerCameraTarget>();
            _cameraTarget.Target = transform;
            _cameraTarget.Offset = Vector3.up * 0.5f;
            _cameraTarget.MoveToTarget();

            _cmFirstPersonCamera = Instantiate(PrefabCmFirstPersonCamera);
            _cmFirstPersonCamera.Target.TrackingTarget = _cameraTarget.transform;
            _cmFirstPersonCamera.Priority = 1;

            _inputShoot.performed += OnInputShoot;

            var inGameHud = FindFirstObjectByType<InGameHud>();
            inGameHud.InitPlayer(this);
        }

        base.OnNetworkSpawn();
    }

    public override void OnDestroy()
    {
        if (IsOwner)
        {
            _inputShoot.performed -= OnInputShoot;
        }

        base.OnDestroy();
    }

    private void Update()
    {
        if (IsOwner)
        {
            CameraLook();
        }
    }

    private void FixedUpdate()
    {
        if (IsOwner)
        {
            Movement();
        }
    }

    private void CameraLook()
    {
        var cameraRotation = _cmFirstPersonCamera.transform.eulerAngles;
        var rotation = transform.eulerAngles;
        rotation.y = cameraRotation.y;
        transform.eulerAngles = rotation;
    }

    private void Movement()
    {
        var inputDir = _inputMove.ReadValue<Vector2>();

        var targetForwardSpeed = inputDir.y * WalkSpeed;
        var targetRightSpeed = inputDir.x * WalkSpeed;
        var velocity = _rb.linearVelocity;

        var forwardSpeed = Vector3.Dot(transform.forward, velocity);
        var rightSpeed = Vector3.Dot(transform.right, velocity);

        // TODO: Check for _rb.GetAccumulatedForce()

        if (Mathf.Abs(targetForwardSpeed) > Mathf.Abs(forwardSpeed))
        {
            var addSpeed = (targetForwardSpeed - forwardSpeed) / Time.fixedDeltaTime;
            _rb.AddForce(transform.forward * addSpeed, ForceMode.Acceleration);
        }

        if (Mathf.Abs(targetRightSpeed) > Mathf.Abs(rightSpeed))
        {
            var addSpeed = (targetRightSpeed - rightSpeed) / Time.fixedDeltaTime;
            _rb.AddForce(transform.right * addSpeed, ForceMode.Acceleration);
        }
    }

    private void OnInputShoot(InputAction.CallbackContext ctx)
    {
        Debug.Log("Shoot");
        AttemptShootRpc(_cmFirstPersonCamera.transform.forward);
    }

    public void CheckDeath()
    {
        if (Health == 0)
        {
            OnDeathRpc();
            GetComponent<NetworkObject>().Despawn();
        }
    }

    [Rpc(SendTo.Owner)]
    private void OnDeathRpc()
    {
        Destroy(_cameraTarget);
    }

    [Rpc(SendTo.Server)]
    private void AttemptShootRpc(Vector3 shootDir)
    {
        if (BulletCount > 0)
        {
            var rayStartPos = _cmFirstPersonCamera.transform.position;
            var rayDir = shootDir;

            Debug.DrawRay(rayStartPos, rayDir * 100, Color.red, 2);

            if (Physics.Raycast(rayStartPos, rayDir, out var rayHitInfo, 100))
            {
                if (rayHitInfo.collider != this && rayHitInfo.collider.CompareTag("Player"))
                {
                    var player = rayHitInfo.collider.GetComponent<Player>();
                    player.Health -= 20;
                    player.CheckDeath();
                }
            }
        }
    }

    public void Init()
    {
        Health = HealthMax;
    }
}
