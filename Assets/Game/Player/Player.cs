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
    public Vector3 InputCameraDir;
    public bool InputDownWeaponShoot;
    public bool InputHoldWeaponAim;
    public bool InputDownWeaponReload;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Tick);
        serializer.SerializeValue(ref InputRotaionY);
        serializer.SerializeValue(ref InputWalkDir);
        serializer.SerializeValue(ref InputCameraDir);
        serializer.SerializeValue(ref InputDownWeaponShoot);
        serializer.SerializeValue(ref InputHoldWeaponAim);
        serializer.SerializeValue(ref InputDownWeaponReload);
    }

    public void ResetInputDown()
    {
        InputDownWeaponShoot = false;
        InputDownWeaponReload = false;
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

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Tick);
        serializer.SerializeValue(ref HealthMax);
        serializer.SerializeValue(ref Health);
        serializer.SerializeValue(ref VelocityY);
        serializer.SerializeValue(ref Position);
        serializer.SerializeValue(ref CurrentWeaponType);
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

    private GameUser _user;

    private GameObject _visual;
    private Collider _collider;
    private CharacterController _characterController;

    private PlayerCameraTarget _cameraTarget;
    private CinemachineCamera _cmFirstPersonCamera;

    private InputAction _inputMove;
    private InputAction _inputWeaponShoot;
    private InputAction _inputWeaponAim;
    private InputAction _inputWeaponReload;

    private Dictionary<WeaponType, Weapon> _weapons = new();
    private Weapon _weapon;

    public bool IsDead { get; private set; } = false;
    [CreateProperty] public int HealthMax { get; private set; } = 100;
    [CreateProperty] public int Health { get; private set; } = 0;
    private float _velocityY = 0;

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
        _inputWeaponShoot = InputSystem.actions.FindAction("Player/WeaponShoot");
        _inputWeaponAim = InputSystem.actions.FindAction("Player/WeaponAim");
        _inputWeaponReload = InputSystem.actions.FindAction("Player/WeaponReload");

        Health = HealthMax;
    }

    public override void OnNetworkSpawn()
    {
        if (IsHost)
        {
            _user = LobbyManager.Singleton.GetUserByClientId(OwnerClientId);
        }

        // Setup camera target.
        _cameraTarget = Instantiate(PrefabPlayerCameraTarget);
        _cameraTarget.Target = transform;
        _cameraTarget.Offset = Vector3.up * 0.5f;
        _cameraTarget.TeleportToTarget();

        // Setup weapon.
        _weapons.Add(WeaponType.GunPistol, Instantiate(PrefabWeaponGunPistol).Init(this));
        _weapon = _weapons[WeaponType.GunPistol];

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
                InputWalkDir = _inputMove.ReadValue<Vector2>(),
                InputCameraDir = GetCameraDir(),
                InputDownWeaponShoot = _inputWeaponShoot.WasPressedThisFrame(),
                InputHoldWeaponAim = _inputWeaponAim.IsPressed(),
                InputDownWeaponReload = _inputWeaponReload.WasPressedThisFrame(),
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
                _weapon.PushCurrentTickData(_tick);

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
                    SendPlayerTickDataToOwnerRpc(GetTickData(tick), _weapon.GetSerializedTickData(tick));
                }
                else
                {
                    var tick = lastProcessedTick + _delayTick;
                    SendPlayerTickDataToOwnerRpc(GetTickData(tick), _weapon.GetSerializedTickData(tick));
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
            HealthMax = HealthMax,
            Health = Health,
            VelocityY = _velocityY,
            Position = transform.position,
        };
    }

    public void ApplyTickData(PlayerTickData tickData)
    {
        // Apply stats.
        HealthMax = tickData.HealthMax;
        Health = tickData.Health;

        // Apply velocity and position.
        _velocityY = tickData.VelocityY;
        _characterController.enabled = false;
        transform.position = tickData.Position;
        _characterController.enabled = true;

        // Weapon.
        _weapon = _weapons[tickData.CurrentWeaponType];
        //TODO: _weapon.ApplyTickData
    }

    public void PushTickData(PlayerInput input, PlayerTickData tickData)
    {
        if (InputBuffer.Count == InputBuffer.Capacity)
            InputBuffer.PopFirst();
        InputBuffer.Add(input);

        if (TickBuffer.Count == TickBuffer.Capacity)
            TickBuffer.PopFirst();
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
        if (!IsDead)
        {
            var rotation = transform.eulerAngles;
            rotation.y = input.InputRotaionY;
            transform.eulerAngles = rotation;

            Movement(input.InputWalkDir, deltaTime);

            _weapon.OnUpdate(input, deltaTime);
        }
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
            InputBuffer.RemoveFrontItems(i + 1);
            TickBuffer.RemoveFrontItems(i + 1);

            // Check prediction.
            bool isDesync =
                serverTickData.HealthMax != predictedTickData.HealthMax ||
                serverTickData.Health != predictedTickData.Health ||
                serverTickData.Position != predictedTickData.Position;

            if (isDesync)
            {
                Debug.Log("prediction failed");

                // TODO:
                // Rollback.
                for (var j = TickBuffer.Count - 1; j > i; --j)
                {
                    // 틱을 이용해서 상태를 돌아야함.
                    var weapon = _weapons[TickBuffer[j].CurrentWeaponType];
                    var tick = TickBuffer[j].Tick;
                    // weapon에서 tick 시점의 상태의 시작 틱을 가져옴.
                    // 만약에 시작틱이 serverTickData.Tick보다 크면 그 상태 전체를 롤백하고 그 상태의 틱들은 넘어감.
                    // stack 구조로 상태가 한 일을 저장해놓고 pop하면서 취소해야함.
                    // 아니라면 상태의 일부를 롤백함.
                }

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

    public void ApplyDamage(int value)
    {
        Health = Mathf.Clamp(Health - value, 0, HealthMax);
    }

    public void ApplyHeal(int value)
    {
        Health = Mathf.Clamp(Health + value, 0, HealthMax);
    }

    public void ChangeWeapon(WeaponType weaponType)
    {
        if (_weapon.WeaponType == weaponType)
            return;

        if (_weapons.ContainsKey(weaponType))
        {
            _weapon.SetStateToIdle();
            _weapon = _weapons[weaponType];
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
    private void SendPlayerTickDataToOwnerRpc(PlayerTickData tickData, byte[] weaponTickData)
    {
        LatestTickData = tickData;

        var reader = new FastBufferReader(weaponTickData, Unity.Collections.Allocator.Temp);
        if (!reader.TryBeginRead(weaponTickData.Length))
        {
            throw new OverflowException("Not enough space in the buffer");
        }

        using (reader)
        {
            reader.ReadValue(out WeaponTickDataHeader header);
            var weaponType = (WeaponType)header.Type;
            switch (weaponType)
            {
                case WeaponType.GunPistol:
                    _weapons[weaponType].SetLatestTickData(WeaponTickDataGunPistol.NewFromReader(header, reader));
                    break;
            }
        }
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
}
