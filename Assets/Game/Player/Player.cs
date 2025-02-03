using System;
using System.Collections.Generic;
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
    public float RotaionY;
    public Vector3 Position;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
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
    private ulong _serverTick = 0;
    private bool _startTick = false;
    private bool _startSpeedUp = false;
    private bool _startSlowDown = false;
    public List<PlayerInput> InputBuffer = new();
    public List<PlayerTickData> TickBuffer = new();
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
                OnInput(input);
                OnUpdate(input, Time.fixedDeltaTime);
            }
            else
            {
                // Send input.
                SendPlayerInputToServerRpc(input);

                // Client-side prediction.
                OnInput(input);
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
                    if (RecivedPlayerInputs.Count <= 5) _startSpeedUp = false;
                    if (RecivedPlayerInputs.Count >= 5) _startSlowDown = false;

                    if (RecivedPlayerInputs.Count >= 10) _startSpeedUp = true;
                    if (RecivedPlayerInputs.Count <= 3) _startSlowDown = true;

                    if (!_startSlowDown || RecivedPlayerInputs.Count % 2 == 0)
                    {
                        var input = RecivedPlayerInputs.Dequeue();
                        OnInput(input);
                        OnUpdate(input, Time.fixedDeltaTime);

                        LastPlayerInput = input;
                        lastProcessedTick = input.Tick;
                    }

                    if (_startSpeedUp && RecivedPlayerInputs.Count % 2 == 0)
                    {
                        var input = RecivedPlayerInputs.Dequeue();
                        OnInput(input);
                        OnUpdate(input, Time.fixedDeltaTime);

                        LastPlayerInput = input;
                        lastProcessedTick = input.Tick;
                    }
                }
                else
                {
                    OnUpdate(LastPlayerInput, Time.fixedDeltaTime);
                }
            }

            _serverTick += 1;
            if (lastProcessedTick != 0)
            {
                _serverTick = 0;
                SendPlayerTickDataToOwnerRpc(GetTickData(lastProcessedTick));
            }
            else
            {
                SendPlayerTickDataToOwnerRpc(GetTickData(lastProcessedTick + _serverTick));
            }
        }

        if (IsHost)
        {
            SendOtherPlayerTickDataRpc(new OtherPlayerTickData
            {
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
        InputBuffer.Add(input);
        if (InputBuffer.Count >= 30)
            InputBuffer.RemoveAt(0);

        TickBuffer.Add(tickData);
        if (TickBuffer.Count >= 30)
            TickBuffer.RemoveAt(0);
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

    private void OnInput(PlayerInput input)
    {
        var rotation = transform.eulerAngles;
        rotation.y = input.InputRotaionY;
        transform.eulerAngles = rotation;
    }

    private void OnUpdate(PlayerInput input, float deltaTime)
    {
        Movement(input.InputWalkDir, deltaTime);
    }

    private void Reconcile()
    {
        var serverTickDataOpt = LatestTickData;
        if (serverTickDataOpt is { } serverTickData)
        {
            LatestTickData = null;

            var i = TickBuffer.FindIndex(item => item.Tick == serverTickData.Tick);
            if (i == -1) return;

            var predictedTickData = TickBuffer[i];

            // Remove old data.
            InputBuffer.RemoveRange(0, i + 1);
            TickBuffer.RemoveRange(0, i + 1);

            // Check prediction.
            if (serverTickData.Position != predictedTickData.Position)
            {
                Debug.Log("prediction failed");

                // Resimulate.
                ApplyTickData(serverTickData);
                for (var j = 0; j < InputBuffer.Count; ++j)
                {
                    var input = InputBuffer[j];
                    OnInput(input);
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

    [Rpc(SendTo.NotServer)]
    private void SendOtherPlayerTickDataRpc(OtherPlayerTickData tickData)
    {
        if (IsOwner)
            return;

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
