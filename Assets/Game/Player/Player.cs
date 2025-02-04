using System;
using System.Collections.Generic;
using RingBuffer;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Properties;
using Unity.Cinemachine;
using Unity.Netcode;

public struct PlayerInput : INetworkSerializable
{
    public ulong Tick;
    public float InputRotaionY;
    public Vector2 InputWalkDir;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Tick);
        serializer.SerializeValue(ref InputRotaionY);
        serializer.SerializeValue(ref InputWalkDir);
    }
}

public struct PlayerTickData : INetworkSerializable
{
    public ulong Tick;
    public float VelocityY;
    public Vector3 Position;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Tick);
        serializer.SerializeValue(ref VelocityY);
        serializer.SerializeValue(ref Position);
    }
}

public struct OtherPlayerTickData : INetworkSerializable
{
    public ulong Tick;
    public float RotaionY;
    public Vector3 Position;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Tick);
        serializer.SerializeValue(ref RotaionY);
        serializer.SerializeValue(ref Position);
    }
}

public class Player : NetworkBehaviour
{
    [SerializeField] private float WalkSpeed = 4.0f;
    [SerializeField] private CinemachineCamera PrefabCmFirstPersonCamera;
    [SerializeField] private PlayerCameraTarget PrefabPlayerCameraTarget;
    [SerializeField] private Weapon PrefabWeaponPistol;

    private GameUser _user;

    private GameObject _visual;
    private Collider _collider;
    private CharacterController _characterController;

    private InputAction _inputMove;

    private PlayerCameraTarget _cameraTarget;
    private CinemachineCamera _cmFirstPersonCamera;

    private Weapon _weapon;
    public Queue<WeaponInput> RecivedWeaponInputs = new();

    public bool IsDead { get; private set; } = false;
    private float _velocityY = 0;

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

    private void Awake()
    {
        _visual = transform.GetChild(0).gameObject;
        _collider = GetComponent<Collider>();
        _characterController = GetComponent<CharacterController>();

        _inputMove = InputSystem.actions.FindAction("Player/Move");
    }

    public override void OnNetworkSpawn()
    {
        if (IsHost)
        {
            _user = LobbyManager.Singleton.GetUserByClientId(OwnerClientId);
            Health = HealthMax;
        }

        // Setup camera target.
        _cameraTarget = Instantiate(PrefabPlayerCameraTarget);
        _cameraTarget.Target = transform;
        _cameraTarget.Offset = Vector3.up * 0.5f;
        _cameraTarget.TeleportToTarget();

        // Setup weapon.
        _weapon = Instantiate(PrefabWeaponPistol);
        _weapon.Init(this);

        if (IsOwner)
        {
            // Setup camera.
            _cmFirstPersonCamera = Instantiate(PrefabCmFirstPersonCamera);
            _cmFirstPersonCamera.Follow = _cameraTarget.transform;
            _cmFirstPersonCamera.Priority = 1;

            // Setup HUD.
            var inGameHud = FindFirstObjectByType<InGameHud>();
            inGameHud.SetTargetPlayer(this);
        }

        if (!IsHost && !IsOwner)
        {
            _characterController.enabled = false;
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

    private void FixedUpdate()
    {
        if (!IsSpawned)
            return;

        if (IsOwner)
        {
            _tick += 1;

            // Get input.
            var input = new PlayerInput
            {
                Tick = _tick,
                InputRotaionY = _cmFirstPersonCamera.transform.eulerAngles.y,
                InputWalkDir = _inputMove.ReadValue<Vector2>(),
            };

            if (IsHost)
            {
                OnUpdate(input, Time.fixedDeltaTime);
            }
            else
            {
                // Send input.
                SendPlayerInputToServerRpc(input);

                // Client-side prediction.
                OnUpdate(input, Time.fixedDeltaTime);

                // Store tick data.
                PushTickData(input, GetTickData(_tick));

                // Reconclie.
                Reconcile();
            }
        }

        if (IsHost)
        {
            CheckDeath();
        }

        if (IsHost && !IsOwner)
        {
            if (!_startTick && RecivedPlayerInputs.Count >= 5)
            {
                _startTick = true;
            }

            ulong lastProcessedTick = 0;
            if (_startTick)
            {
                Debug.Log(RecivedPlayerInputs.Count);
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
                    OnUpdate(LastPlayerInput, Time.fixedDeltaTime);
                }

                // Send state to the client.
                _delayTick += 1;
                if (lastProcessedTick != 0)
                {
                    _delayTick = 0;
                    SendPlayerTickDataToOwnerRpc(GetTickData(lastProcessedTick));
                }
                else
                {
                    SendPlayerTickDataToOwnerRpc(GetTickData(lastProcessedTick + _delayTick));
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

    public Vector3 GetHeadPos()
    {
        return transform.position + _cameraTarget.Offset;
    }

    public Vector3 GetCameraDir()
    {
        return _cmFirstPersonCamera.transform.forward;
    }

    public PlayerTickData GetTickData(ulong tick)
    {
        return new PlayerTickData
        {
            Tick = tick,
            VelocityY = _velocityY,
            Position = transform.position,
        };
    }

    public void ApplyTickData(PlayerTickData tickData)
    {
        _velocityY = tickData.VelocityY;

        _characterController.enabled = false;
        transform.position = tickData.Position;
        _characterController.enabled = true;
    }

    public void PushTickData(PlayerInput input, PlayerTickData tickData)
    {
        if (InputBuffer.Count == InputBuffer.Capacity)
            InputBuffer.PopFirst();
        InputBuffer.Add(input);

        if (TickBuffer.Count == TickBuffer.Capacity)
            InputBuffer.PopFirst();
        TickBuffer.Add(tickData);
    }

    public void SetPlayerActive(bool value)
    {
        _visual.SetActive(value);
        _collider.enabled = value;
        _characterController.enabled = value;
    }

    private void Movement(Vector2 inputWalkDir, float deltaTime)
    {
        if (IsDead)
            return;

        var forwardSpeed = inputWalkDir.y * WalkSpeed * deltaTime;
        var rightSpeed = inputWalkDir.x * WalkSpeed * deltaTime;
        _velocityY = _characterController.isGrounded ? 0 : (_velocityY + -10f * deltaTime);

        _characterController.Move(
            (transform.forward * forwardSpeed) +
            (transform.right * rightSpeed) +
            (transform.up * _velocityY * deltaTime));
    }

    private void OnUpdate(PlayerInput input, float deltaTime)
    {
        var rotation = transform.eulerAngles;
        rotation.y = input.InputRotaionY;
        transform.eulerAngles = rotation;

        Movement(input.InputWalkDir, deltaTime);
    }

    private void Reconcile()
    {
        var serverTickDataOpt = LatestTickData;
        if (serverTickDataOpt is { } serverTickData)
        {
            LatestTickData = null;

            var i = -1;
            for (var j = 0; j < TickBuffer.Count; ++j)
            {
                if (TickBuffer[j].Tick == serverTickData.Tick)
                {
                    i = j;
                    break;
                }
            }
            if (i == -1) return;

            var predictedTickData = TickBuffer[i];

            // Remove old data.
            InputBuffer.ConsumeSpan(i + 1);
            TickBuffer.ConsumeSpan(i + 1);

            // Check prediction.
            if (serverTickData.Position != predictedTickData.Position)
            {
                Debug.Log("prediction failed");

                // Resimulate.
                ApplyTickData(serverTickData);
                for (var j = 0; j < InputBuffer.Count; ++j)
                {
                    var input = InputBuffer[j];
                    OnUpdate(input, Time.fixedDeltaTime);
                    TickBuffer[j] = GetTickData(input.Tick);
                }
            }
            else
            {
                Debug.Log("prediction success");
            }
        }
    }

    public void Respawn()
    {
        if (!_user.IsDead)
            return;

        _user.IsDead = false;
        Health = HealthMax;
        PlayerRespawnRpc();
    }

    public void CheckDeath()
    {
        if (_user.IsDead)
            return;

        if (Health == 0)
        {
            _user.IsDead = true;
            PlayerDeathRpc();

            Invoke(nameof(Respawn), 3.0f);
        }
    }

    [Rpc(SendTo.Everyone)]
    private void PlayerRespawnRpc()
    {
        IsDead = false;
        SetPlayerActive(true);
    }

    [Rpc(SendTo.Everyone)]
    private void PlayerDeathRpc()
    {
        IsDead = true;
        SetPlayerActive(false);

        if (_weapon)
        {
            _weapon.ResetWeapon();
        }
    }

    [Rpc(SendTo.Owner)]
    private void SendPlayerTickDataToOwnerRpc(PlayerTickData tickData)
    {
        LatestTickData = tickData;
    }

    [Rpc(SendTo.NotServer, Delivery = RpcDelivery.Unreliable)]
    private void SendOtherPlayerTickDataRpc(OtherPlayerTickData tickData)
    {
        if (IsOwner)
            return;

        if (_lastServerTick >= tickData.Tick)
            return;

        _lastServerTick = tickData.Tick;

        var rotation = transform.eulerAngles;
        rotation.y = tickData.RotaionY;
        transform.eulerAngles = rotation;

        transform.position = tickData.Position;
        _visual.transform.Teleport(Vector3.zero, Quaternion.identity);
    }

    [Rpc(SendTo.Server)]
    public void SendPlayerInputToServerRpc(PlayerInput input)
    {
        RecivedPlayerInputs.Enqueue(input);
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
            reader.ReadValue(out WeaponTickDataHeader header);
            switch ((WeaponTickDataType)header.Type)
            {
                case WeaponTickDataType.GunPistol:
                    _weapon.SetLatestTickData(WeaponTickDataGunPistol.NewFromReader(header, reader));
                    break;
            }
        }
    }
}
