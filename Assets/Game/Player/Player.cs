using System;
using System.Collections.Generic;
using RingBuffer;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Properties;
using Unity.Cinemachine;
using Unity.Netcode;
using Unity.Netcode.Components;

public struct PlayerInput : INetworkSerializable
{
    public ulong Tick;
    public float InputRotaionY;
    public Vector3 InputCameraDir;
    public Vector2 InputWalkDir;
    public bool InputDownWeaponSwap;
    public bool InputDownWeaponShoot;
    public bool InputHoldWeaponShoot;
    public bool InputHoldWeaponAim;
    public bool InputDownWeaponReload;
    public bool InputDownGrenadeThrow;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Tick);
        serializer.SerializeValue(ref InputRotaionY);
        serializer.SerializeValue(ref InputCameraDir);
        serializer.SerializeValue(ref InputWalkDir);
        serializer.SerializeValue(ref InputDownWeaponSwap);
        serializer.SerializeValue(ref InputDownWeaponShoot);
        serializer.SerializeValue(ref InputHoldWeaponShoot);
        serializer.SerializeValue(ref InputHoldWeaponAim);
        serializer.SerializeValue(ref InputDownWeaponReload);
        serializer.SerializeValue(ref InputDownGrenadeThrow);
    }

    public void ResetInputDown()
    {
        InputDownWeaponSwap = false;
        InputDownWeaponShoot = false;
        InputDownWeaponReload = false;
        InputDownGrenadeThrow = false;
    }
}

public struct PlayerTickData : INetworkSerializable
{
    public ulong Tick;
    public int HealthMax;
    public int Health;
    public float VelocityY;
    public Vector3 Position;
    public WeaponType CurrentWeaponType;
    public bool IsThrowingGrenade;
    public float GrenadeThrowTimerTime;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Tick);
        serializer.SerializeValue(ref HealthMax);
        serializer.SerializeValue(ref Health);
        serializer.SerializeValue(ref VelocityY);
        serializer.SerializeValue(ref Position);
        serializer.SerializeValue(ref CurrentWeaponType);
        serializer.SerializeValue(ref IsThrowingGrenade);
        serializer.SerializeValue(ref GrenadeThrowTimerTime);
    }
}

public struct OtherPlayerTickData : INetworkSerializable
{
    public ulong Tick;
    public int HealthMax;
    public int Health;
    public float RotaionY;
    public Vector3 Position;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Tick);
        serializer.SerializeValue(ref Health);
        serializer.SerializeValue(ref HealthMax);
        serializer.SerializeValue(ref RotaionY);
        serializer.SerializeValue(ref Position);
    }
}

public class Player : NetworkBehaviour
{
    [SerializeField] private float WalkSpeed = 4.0f;
    [SerializeField] private CinemachineCamera PrefabCmFirstPersonCamera;
    [SerializeField] private PlayerCameraTarget PrefabPlayerCameraTarget;
    [SerializeField] private Weapon PrefabWeaponGunPistol;
    [SerializeField] private Weapon PrefabWeaponGunAssaultRifle;
    [SerializeField] private Grenade PrefabGrenade;

    private GameUser _user;

    // Components
    private GameObject _visual;
    private Collider _collider;
    private CharacterController _characterController;

    // Camera
    private PlayerCameraTarget _cameraTarget;
    private CinemachineCamera _cmFirstPersonCamera;
    private CinemachineInputAxisController _cmInputAxisController;

    // Inputs
    private InputAction _inputMove;
    private InputAction _inputWeaponSwap;
    private InputAction _inputWeaponShoot;
    private InputAction _inputWeaponAim;
    private InputAction _inputWeaponReload;
    private InputAction _inputGrenadeThrow;

    // Stats
    public bool IsDead { get; private set; } = false;
    [CreateProperty] public int HealthMax { get; private set; } = 100;
    [CreateProperty] public int Health { get; private set; } = 0;
    private float _velocityY = 0;

    // Weapons
    private Dictionary<WeaponType, Weapon> _weapons = new();
    private Weapon _weapon;
    private WeaponType _weaponSwapTarget = WeaponType.None;

    // Grenade
    public bool IsThrowingGrenade = false;
    private GameTimer _grenadeThrowTimer = new(0.5f);

    // Networking
    private ulong _tick = 0;
    private ulong _delayTick = 0;
    private ulong _serverTick = 0;
    private ulong _lastServerTick = 0;
    private bool _startTick = false;
    private bool _tickSpeedUp = false;
    private bool _tickSlowDown = false;
    public RingBuffer<PlayerInput> InputBuffer = new(20);
    public RingBuffer<PlayerTickData> TickBuffer = new(20);
    public PlayerTickData? LatestTickData = null;
    public PlayerInput LastPlayerInput = new();
    public Queue<PlayerInput> RecivedPlayerInputs = new();

    // Interpolation
    private Vector3 _startPos;
    private Vector3 _nextPos;
    private float _interpolateTime;

    private void Awake()
    {
        _visual = transform.GetChild(0).gameObject;
        _collider = GetComponent<Collider>();
        _characterController = GetComponent<CharacterController>();

        _inputMove = InputSystem.actions.FindAction("Player/Move");
        _inputWeaponSwap = InputSystem.actions.FindAction("Player/WeaponSwap");
        _inputWeaponShoot = InputSystem.actions.FindAction("Player/WeaponShoot");
        _inputWeaponAim = InputSystem.actions.FindAction("Player/WeaponAim");
        _inputWeaponReload = InputSystem.actions.FindAction("Player/WeaponReload");
        _inputGrenadeThrow = InputSystem.actions.FindAction("Player/GrenadeThrow");

        Health = HealthMax;
    }

    public override void OnNetworkSpawn()
    {
        if (IsHost || IsOwner)
        {
            _user = LobbyManager.Singleton.GetUserByClientId(OwnerClientId);
            _user.Player = this;
        }

        // Setup camera target.
        _cameraTarget = Instantiate(PrefabPlayerCameraTarget);
        _cameraTarget.Target = transform;
        _cameraTarget.Offset = Vector3.up * 0.5f;
        _cameraTarget.TeleportToTarget();

        // Setup weapons.
        _weapons.Add(WeaponType.GunPistol, Instantiate(PrefabWeaponGunPistol).Init(this));
        _weapons.Add(WeaponType.GunAssaultRifle, Instantiate(PrefabWeaponGunAssaultRifle).Init(this));
        _weapon = _weapons[WeaponType.GunPistol];

        if (IsOwner)
        {
            // Enable player input.
            SetPlayerInputActive(true);

            // Setup camera.
            _cmFirstPersonCamera = Instantiate(PrefabCmFirstPersonCamera);
            _cmFirstPersonCamera.Follow = _cameraTarget.transform;
            _cmFirstPersonCamera.Priority = 1;
            _cmInputAxisController = _cmFirstPersonCamera.GetComponent<CinemachineInputAxisController>();

            // Setup HUD.
            var inGameHud = FindFirstObjectByType<InGameHud>();
            inGameHud.SetTargetPlayer(this);
        }

        if (!IsHost && !IsOwner)
        {
            _characterController.enabled = false;
            _startPos = transform.position;
            _nextPos = transform.position;
        }

        base.OnNetworkSpawn();
    }

    public override void OnDestroy()
    {
        Destroy(_cameraTarget);
        Destroy(_cmFirstPersonCamera);
        Destroy(_weapon);

        base.OnDestroy();
    }

    private void Update()
    {
        if (!IsSpawned)
            return;

        if (!IsHost && !IsOwner)
        {
            // Interpolation
            transform.position = Vector3.LerpUnclamped(_startPos, _nextPos, _interpolateTime * 50);
            _interpolateTime += Time.deltaTime;
            _visual.transform.Teleport(Vector3.zero, Quaternion.identity);
        }
    }

    private void FixedUpdate()
    {
        if (!IsSpawned)
            return;

        if (IsHost)
        {
            CheckDeath();
        }

        if (IsOwner)
        {
            _tick += 1;

            // Get input.
            var input = new PlayerInput
            {
                Tick = _tick,
                InputRotaionY = _cmFirstPersonCamera.transform.eulerAngles.y,
                InputCameraDir = GetCameraDir(),
                InputWalkDir = _inputMove.ReadValue<Vector2>(),
                InputDownWeaponSwap = _inputWeaponSwap.WasPressedThisFrame(),
                InputDownWeaponShoot = _inputWeaponShoot.WasPressedThisFrame(),
                InputHoldWeaponShoot = _inputWeaponShoot.IsPressed(),
                InputHoldWeaponAim = _inputWeaponAim.IsPressed(),
                InputDownWeaponReload = _inputWeaponReload.WasPressedThisFrame(),
                InputDownGrenadeThrow = _inputGrenadeThrow.WasPressedThisFrame(),
            };

            if (IsHost)
            {
                OnUpdate(input, Time.fixedDeltaTime);
            }
            else
            {
                // Send input.
                SendPlayerInputRpc(input);

                // Client-side prediction.
                OnUpdate(input, Time.fixedDeltaTime);

                // Store input and tick data.
                PushInputData(input);
                PushCurrentTickData(_tick);

                // Reconclie.
                Reconcile();
            }
        }

        if (IsHost && !IsOwner)
        {
            if (!_startTick && RecivedPlayerInputs.Count >= 5)
            {
                _startTick = true;
            }

            if (_startTick)
            {
                ulong lastProcessedTick = 0;
                if (RecivedPlayerInputs.Count > 0)
                {
                    // Stop tick speed up / down.
                    if (RecivedPlayerInputs.Count <= 5) _tickSpeedUp = false;
                    if (RecivedPlayerInputs.Count >= 5) _tickSlowDown = false;

                    // Start tick speed up / down.
                    if (RecivedPlayerInputs.Count >= 8) _tickSpeedUp = true;
                    if (RecivedPlayerInputs.Count <= 3) _tickSlowDown = true;

                    if (!_tickSlowDown || RecivedPlayerInputs.Count % 2 == 0)
                    {
                        var input = RecivedPlayerInputs.Dequeue();
                        OnUpdate(input, Time.fixedDeltaTime);
                        LastPlayerInput = input;
                        lastProcessedTick = input.Tick;
                    }

                    if (_tickSpeedUp && RecivedPlayerInputs.Count % 2 == 0)
                    {
                        var input = RecivedPlayerInputs.Dequeue();
                        OnUpdate(input, Time.fixedDeltaTime);
                        LastPlayerInput = input;
                        lastProcessedTick = input.Tick;
                    }
                }
                else
                {
                    // Simulate using the last input.
                    LastPlayerInput.ResetInputDown();
                    OnUpdate(LastPlayerInput, Time.fixedDeltaTime);
                }

                // Send state to the client.
                _delayTick += 1;
                if (lastProcessedTick != 0)
                {
                    _delayTick = 0;
                    var tick = lastProcessedTick;
                    SendOwnerPlayerTickDataRpc(GetTickData(tick), _weapon.GetSerializedTickData(tick));
                }
                else
                {
                    var tick = lastProcessedTick + _delayTick;
                    SendOwnerPlayerTickDataRpc(GetTickData(tick), _weapon.GetSerializedTickData(tick));
                }
            }
        }

        if (IsHost)
        {
            // Send state to other clients.
            _serverTick += 1;
            SendOtherPlayerTickDataRpc(new OtherPlayerTickData
            {
                Tick = _serverTick,
                HealthMax = HealthMax,
                Health = Health,
                RotaionY = transform.eulerAngles.y,
                Position = transform.position,
            });
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (IsHost)
        {
            if (other.CompareTag("Pickup"))
            {
                other.GetComponent<Pickup>().OnPickup(this);
            }
        }
    }

    public void SetPlayerInputActive(bool value)
    {
        if (_cmInputAxisController != null)
        {
            _cmInputAxisController.enabled = value;
        }

        if (value)
        {
            InputSystem.actions.FindActionMap("Player").Enable();
        }
        else
        {
            InputSystem.actions.FindActionMap("Player").Disable();
        }
    }

    public void SetPlayerActive(bool value)
    {
        _visual.SetActive(value);
        _collider.enabled = value;
        _characterController.enabled = value;
    }

    public Vector3 GetHeadPos()
    {
        return transform.position + _cameraTarget.Offset;
    }

    public Vector3 GetCameraDir()
    {
        return _cmFirstPersonCamera.transform.forward;
    }

    private PlayerTickData GetTickData(ulong tick)
    {
        return new PlayerTickData
        {
            Tick = tick,
            HealthMax = HealthMax,
            Health = Health,
            VelocityY = _velocityY,
            Position = transform.position,
            CurrentWeaponType = _weapon.WeaponType,
            IsThrowingGrenade = IsThrowingGrenade,
            GrenadeThrowTimerTime = _grenadeThrowTimer.Time,
        };
    }

    private int GetTickDataIndexFromBuffer(ulong tick)
    {
        for (var i = 0; i < TickBuffer.Count; ++i)
        {
            if (TickBuffer[i].Tick == tick)
                return i;
        }
        return -1;
    }

    private void ApplyTickData(PlayerTickData tickData)
    {
        // Apply stats.
        HealthMax = tickData.HealthMax;
        Health = tickData.Health;

        // Apply velocity and position.
        _velocityY = tickData.VelocityY;
        _characterController.enabled = false;
        transform.position = tickData.Position;
        _characterController.enabled = true;

        // Apply weapon.
        _weapon = _weapons[tickData.CurrentWeaponType];

        // Apply grenade.
        IsThrowingGrenade = tickData.IsThrowingGrenade;
        _grenadeThrowTimer.Time = tickData.GrenadeThrowTimerTime;
    }

    private void PushInputData(PlayerInput input)
    {
        if (InputBuffer.Count == InputBuffer.Capacity)
            InputBuffer.PopFirst();
        InputBuffer.Add(input);
    }

    private void PushCurrentTickData(ulong tick)
    {
        if (TickBuffer.Count == TickBuffer.Capacity)
            TickBuffer.PopFirst();
        TickBuffer.Add(GetTickData(tick));

        _weapon.PushCurrentTickData(tick);
    }

    private void CharacterRotate(float angle)
    {
        var rotation = transform.eulerAngles;
        rotation.y = angle;
        transform.eulerAngles = rotation;
    }

    private void CharacterMovement(PlayerInput input, float deltaTime)
    {
        var forwardSpeed = input.InputWalkDir.y * WalkSpeed * deltaTime;
        var rightSpeed = input.InputWalkDir.x * WalkSpeed * deltaTime;
        _velocityY = _characterController.isGrounded ? 0 : (_velocityY + -10f * deltaTime);

        _characterController.Move(
            (transform.forward * forwardSpeed) +
            (transform.right * rightSpeed) +
            (transform.up * _velocityY * deltaTime));
    }

    private void CharacterSwapWeapon(PlayerInput input)
    {
        if (_weaponSwapTarget != WeaponType.None)
        {
            Debug.Log($"WeaponSwap: {_weaponSwapTarget}");
            _weapon = _weapons[_weaponSwapTarget];
            _weaponSwapTarget = WeaponType.None;
            _weapon.SetStateToIdle();
        }

        if (input.InputDownWeaponSwap)
        {
            _weapon.SetStateToHolster();
            _weaponSwapTarget =
                _weapon.WeaponType == WeaponType.GunPistol
                ? WeaponType.GunAssaultRifle
                : WeaponType.GunPistol;
        }
    }

    private void CharacterGrenadeThrow(PlayerInput input, float deltaTime)
    {
        if (input.InputDownGrenadeThrow)
        {
            IsThrowingGrenade = true;
            // TODO: play animation and vfx
        }

        if (IsThrowingGrenade)
        {
            _grenadeThrowTimer.Tick(deltaTime);
            if (_grenadeThrowTimer.IsEnded)
            {
                IsThrowingGrenade = false;

                if (IsHost)
                {
                    var grenade = Instantiate(PrefabGrenade);
                    var networkGrenade = grenade.GetComponent<NetworkObject>();
                    var forward = Quaternion.AngleAxis(-50f, transform.right) * transform.forward;
                    networkGrenade.transform.position = GetHeadPos() + transform.forward;
                    networkGrenade.transform.forward = forward;
                    networkGrenade.Spawn();
                    grenade.GetComponent<Rigidbody>().AddForce(forward * 500f);
                }
            }
        }
    }

    private void OnUpdate(PlayerInput input, float deltaTime)
    {
        if (!IsDead)
        {
            CharacterRotate(input.InputRotaionY);
            CharacterMovement(input, deltaTime);
            CharacterSwapWeapon(input);
            CharacterGrenadeThrow(input, deltaTime);

            // Update weapon.
            _weapon.OnUpdate(input, deltaTime);
        }
    }

    private bool IsDesynced()
    {
        if (LatestTickData is { } serverTickData)
        {
            var i = GetTickDataIndexFromBuffer(serverTickData.Tick);
            if (i == -1)
            {
                Debug.LogWarning("Player.IsDesynced: LatestTickData is too old.");
                return false;
            }

            var clientTickData = TickBuffer[i];
            return
                serverTickData.HealthMax != clientTickData.HealthMax ||
                serverTickData.Health != clientTickData.Health ||
                serverTickData.Position != clientTickData.Position ||
                serverTickData.CurrentWeaponType != clientTickData.CurrentWeaponType ||
                _weapons[serverTickData.CurrentWeaponType].IsDesynced();
        }
        else
        {
            Debug.LogWarning("Player.IsDesynced: LatestTickData is null.");
            return false;
        }
    }

    private void Reconcile()
    {
        try
        {
            if (LatestTickData is { } serverTickData)
            {
                var tickIndex = GetTickDataIndexFromBuffer(serverTickData.Tick);
                if (tickIndex == -1)
                    return;

                // Check prediction.
                var isDesynced = IsDesynced();

                // Remove old data.
                InputBuffer.RemoveFrontItems(tickIndex + 1);
                TickBuffer.RemoveFrontItems(tickIndex + 1);

                if (isDesynced)
                {
                    // Rollback.
                    for (var i = TickBuffer.Count - 1; i >= 0; --i)
                    {
                        var tick = TickBuffer[i].Tick;
                        if (tick < serverTickData.Tick)
                            break;

                        var weapon = _weapons[TickBuffer[i].CurrentWeaponType];
                        weapon.RollbackToTick(tick);
                    }

                    // Clear player tick data.
                    TickBuffer.Clear();

                    // Clear weapons tick data.
                    foreach (var weapon in _weapons.Values)
                        weapon.ClearTickData();

                    // Apply server tick data.
                    ApplyTickData(serverTickData);
                    _weapon.ApplyLatestTickData();
                    // TODO:
                    // 서버가 배열열 위치와 회전값, 애니/이펙트/사운드 ID, 소환 또는 삭제 정보를 전송함
                    // 클라는 그 정보를 받아서 실행함.

                    // Resimulate.
                    for (var i = 0; i < InputBuffer.Count; ++i)
                    {
                        var input = InputBuffer[i];
                        OnUpdate(input, Time.fixedDeltaTime);
                        PushCurrentTickData(input.Tick);
                        // TODO: 애니, 이펙트, 사운드 시간 진행
                    }
                }
            }
        }
        finally
        {
            LatestTickData = null;
        }
    }

    private void OnRespawn()
    {
        IsDead = false;
        Health = HealthMax;
        SetPlayerActive(true);
    }

    private void OnDeath()
    {
        IsDead = true;
        SetPlayerActive(false);

        foreach (var weapon in _weapons.Values)
            weapon.ResetWeapon();
    }

    public void Respawn()
    {
        if (!IsDead)
            return;

        OnRespawn();
        OnRespawnRpc();
    }

    public void CheckDeath()
    {
        if (IsDead)
            return;

        if (Health == 0)
        {
            OnDeath();
            OnDeathRpc();
            Invoke(nameof(Respawn), 3.0f);
        }
    }

    public void ApplyDamage(int value)
    {
        Health = Mathf.Clamp(Health - value, 0, HealthMax);
    }

    public void ApplyHeal(int value)
    {
        Health = Mathf.Clamp(Health + value, 0, HealthMax);
    }

    [Rpc(SendTo.NotServer)]
    private void OnRespawnRpc(RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != NetworkManager.Singleton.NetworkConfig.NetworkTransport.ServerClientId)
            return;

        OnRespawn();
    }

    [Rpc(SendTo.NotServer)]
    private void OnDeathRpc(RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != NetworkManager.Singleton.NetworkConfig.NetworkTransport.ServerClientId)
            return;

        OnDeath();
    }

    [Rpc(SendTo.Owner)]
    private void SendOwnerPlayerTickDataRpc(PlayerTickData tickData, byte[] weaponTickData, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != NetworkManager.Singleton.NetworkConfig.NetworkTransport.ServerClientId)
            return;

        // Set player latest tick data.
        LatestTickData = tickData;

        unsafe
        {
            fixed (byte* bytePtr = weaponTickData)
            {
                var size = weaponTickData.Length;
                using var reader = new FastBufferReader(bytePtr, Unity.Collections.Allocator.None, size);
                if (!reader.TryBeginRead(size))
                    throw new OverflowException("Not enough space in the buffer");

                // Read header.
                reader.ReadValue(out WeaponTickDataHeader header);

                // Set weapon latest tick data.
                var weapon = _weapons[header.Type];
                if (header.Type == weapon.WeaponType)
                {
                    weapon.SetLatestTickData(header, reader);
                }
                else
                {
                    Debug.LogError($"SetLatestTickData: WeaponType mismatch {header.Type} != {weapon.WeaponType}");
                }
            }
        }
    }

    [Rpc(SendTo.NotServer, Delivery = RpcDelivery.Unreliable)]
    private void SendOtherPlayerTickDataRpc(OtherPlayerTickData tickData, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != NetworkManager.Singleton.NetworkConfig.NetworkTransport.ServerClientId)
            return;

        if (IsOwner || _lastServerTick >= tickData.Tick)
            return;

        _lastServerTick = tickData.Tick;

        CharacterRotate(tickData.RotaionY);

        _startPos = transform.position;
        _nextPos = tickData.Position;
        _interpolateTime = 0;
    }

    [Rpc(SendTo.Server)]
    public void SendPlayerInputRpc(PlayerInput input)
    {
        RecivedPlayerInputs.Enqueue(input);
    }

    [Rpc(SendTo.Everyone)]
    public void SpawnBulletTrailRpc(WeaponType weaponType, Vector3 start, Vector3 end, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != NetworkManager.Singleton.NetworkConfig.NetworkTransport.ServerClientId)
            return;

        _weapons[weaponType].SpawnBulletTrail(start, end);
    }
}
