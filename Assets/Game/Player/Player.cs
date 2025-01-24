using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Properties;
using Unity.Cinemachine;
using Unity.Netcode;

public class Player : NetworkBehaviour
{
    [SerializeField] private float WalkSpeed = 4.0f;

    [SerializeField] private CinemachineCamera PrefabCmFirstPersonCamera;
    [SerializeField] private Weapon PrefabWeaponPistol;

    private Rigidbody _rb;

    private InputAction _inputMove;

    private PlayerCameraTarget _cameraTarget;
    private CinemachineCamera _cmFirstPersonCamera;

    private Weapon _weapon;
    public WeaponTickData LatestWeaponTickData;
    public Queue<WeaponInput> RecivedWeaponInputs = new();

    public bool IsDead { get; private set; } = false;

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

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();

        _inputMove = InputSystem.actions.FindAction("Player/Move");
    }

    public override void OnNetworkSpawn()
    {
        if (IsHost)
        {
            HostInit();
        }

        _cameraTarget = new GameObject().AddComponent<PlayerCameraTarget>();
        _cameraTarget.Target = transform;
        _cameraTarget.Offset = Vector3.up * 0.5f;
        _cameraTarget.MoveToTarget();

        _weapon = Instantiate(PrefabWeaponPistol);
        _weapon.Init(this);

        if (IsOwner)
        {
            _cmFirstPersonCamera = Instantiate(PrefabCmFirstPersonCamera);
            _cmFirstPersonCamera.Follow = _cameraTarget.transform;
            _cmFirstPersonCamera.Priority = 1;

            var inGameHud = FindFirstObjectByType<InGameHud>();
            inGameHud.InitPlayer(this);
        }

        base.OnNetworkSpawn();
    }

    public override void OnDestroy()
    {
        if (_weapon != null)
        {
            Destroy(_weapon);
        }

        base.OnDestroy();
    }

    private void Update()
    {
        if (IsOwner)
        {
            CameraLook();
        }

        if (IsHost)
        {
            CheckDeath();
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

    public void HostInit()
    {
        Health = HealthMax;
    }

    public Vector3 GetHeadPosition()
    {
        return _cameraTarget.Target.position + _cameraTarget.Offset;
    }

    public Vector3 GetCameraDir()
    {
        return _cmFirstPersonCamera.transform.forward;
    }

    public void CheckDeath()
    {
        if (IsDead)
        {
            return;
        }

        if (Health == 0)
        {
            IsDead = true;
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
    public void SendWeaponInputToServerRpc(WeaponInput input)
    {
        RecivedWeaponInputs.Enqueue(input);
    }

    [Rpc(SendTo.Owner)]
    public void SendWeaponStateToOwnerRpc(byte[] weaponTickData)
    {
        var reader = new FastBufferReader(weaponTickData, Unity.Collections.Allocator.Temp);
        if (!reader.TryBeginRead(weaponTickData.Length))
        {
            throw new OverflowException("Not enough space in the buffer");
        }

        using (reader)
        {
            reader.ReadValue(out ulong type);
            reader.ReadValue(out ulong tick);
            reader.ReadValue(out uint stateIndex);
            switch (type)
            {
                case (ulong)WeaponTickDataType.GunPistol:
                    LatestWeaponTickData = WeaponTickDataGunPistol.NewFromReader(type, tick, stateIndex, reader);
                    break;
            }
        }
    }
}
