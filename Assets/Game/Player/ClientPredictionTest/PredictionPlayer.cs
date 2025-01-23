using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

public struct BufferedPlayerState : INetworkSerializeByMemcpy
{
    public UInt64 Tick;
    public float DeltaTime;
    public Vector3 Pos;
    public bool IsStunned;
}

public struct BufferedPlayerInput : INetworkSerializeByMemcpy
{
    public UInt64 Tick;
    public Vector2 InputMove;
}

public class PredictionPlayer : NetworkBehaviour
{
    public float MoveSpeed = 3;

    private UInt64 _tick = 0;
    private List<BufferedPlayerState> _stateBuffer = new();
    private List<BufferedPlayerInput> _inputBuffer = new();
    private Queue<BufferedPlayerState> _recivedStates = new();
    private Queue<BufferedPlayerInput> _recivedInputs = new();

    private InputAction _inputMove;

    private bool _isStunned = false;
    private float _stunTime = 0;

    private void Awake()
    {
        _inputMove = InputSystem.actions.FindAction("Player/Move");
    }

    private void Update()
    {
        if (!IsSpawned)
        {
            return;
        }

        if (IsOwner)
        {
            _tick += 1;

            // Read input.
            var inputMove = _inputMove.ReadValue<Vector2>();

            // Send input to server.
            SendInputToServerRpc(new BufferedPlayerInput { Tick = _tick, InputMove = inputMove });

            // Self stun.
            if (Input.GetKeyDown(KeyCode.Space))
            {
                SendStunToServerRpc();
            }

            // Client-side prediction and reconciliation
            if (!IsHost)
            {
                ProcessTick(Time.deltaTime, inputMove);
                ClientSaveStates(inputMove);
                ClientRollback();
            }
        }
    }

    private void FixedUpdate()
    {
        if (!IsSpawned)
        {
            return;
        }

        if (IsHost)
        {
            if (_isStunned)
            {
                _stunTime += Time.fixedDeltaTime;
                if (_stunTime >= 2)
                {
                    _isStunned = false;
                    _stunTime = 0;
                    SendStunToOwnerRpc(false);
                }
            }

            UInt64 lastTick = 0;
            while (_recivedInputs.Count > 0)
            {
                // Recive input.
                var input = _recivedInputs.Dequeue();
                lastTick = input.Tick;

                // Run game logic.
                ProcessTick(Time.fixedDeltaTime, input.InputMove);
            }

            var state = new BufferedPlayerState
            {
                Tick = lastTick,
                Pos = transform.position,
                IsStunned = _isStunned,
            };

            // Send state to clients
            SendStateToOwnerRpc(state);
            SendStateToNonOwnerRpc(state);
        }
    }

    private void ClientSaveStates(Vector2 input)
    {
        // save state to buffer
        _stateBuffer.Add(new BufferedPlayerState
        {
            Tick = _tick,
            DeltaTime = Time.deltaTime,
            Pos = transform.position,
            IsStunned = _isStunned,
        });
        if (_stateBuffer.Count >= 30)
            _stateBuffer.RemoveAt(0);

        // save input to buffer
        _inputBuffer.Add(new BufferedPlayerInput
        {
            Tick = _tick,
            InputMove = input,
        });
        if (_inputBuffer.Count >= 30)
            _inputBuffer.RemoveAt(0);
    }

    private void ClientRollback()
    {
        while (_recivedStates.Count > 0)
        {
            var serverState = _recivedStates.Dequeue();
            var stateIndex = _stateBuffer.FindIndex(0, _stateBuffer.Count, (item) => item.Tick == serverState.Tick);
            var clientState = _stateBuffer[stateIndex];

            // Remove old state.
            _stateBuffer.RemoveRange(0, stateIndex + 1);
            _inputBuffer.RemoveRange(0, stateIndex + 1);

            if (clientState.Equals(serverState))
            {
                Debug.Log("Prediction success");
            }
            else
            {
                Debug.Log("Prediction failed");

                // Apply server state.
                transform.position = serverState.Pos;
                _isStunned = serverState.IsStunned;

                // Repredict
                for (int i = 0; i < _stateBuffer.Count; ++i)
                {
                    var deltaTime = _stateBuffer[i].DeltaTime;
                    var input = _inputBuffer[i];
                    ProcessTick(deltaTime, input.InputMove);

                    _stateBuffer[i] = new BufferedPlayerState
                    {
                        Tick = _stateBuffer[i].Tick,
                        DeltaTime = deltaTime,
                        Pos = transform.position,
                        IsStunned = _isStunned,
                    };
                }
            }
        }
    }

    private void ProcessTick(float deltaTime, Vector2 input)
    {
        if (!_isStunned)
        {
            var pos = Move(deltaTime, input);
            transform.position = pos;
        }
    }

    private Vector3 Move(float deltaTime, Vector2 input)
    {
        var pos = transform.position;
        var moveDelta = input * (MoveSpeed * deltaTime);
        pos.x += moveDelta.x;
        pos.z += moveDelta.y;
        return pos;
    }

    [Rpc(SendTo.Server)]
    private void SendInputToServerRpc(BufferedPlayerInput input)
    {
        _recivedInputs.Enqueue(input);
    }

    [Rpc(SendTo.Owner)]
    private void SendStateToOwnerRpc(BufferedPlayerState state)
    {
        _recivedStates.Enqueue(state);
    }

    [Rpc(SendTo.NotOwner)]
    private void SendStateToNonOwnerRpc(BufferedPlayerState state)
    {
        if (!IsHost)
        {
            transform.position = state.Pos;
        }
    }

    [Rpc(SendTo.Server)]
    private void SendStunToServerRpc()
    {
        _isStunned = true;
        SendStunToOwnerRpc(true);
    }

    [Rpc(SendTo.Owner)]
    private void SendStunToOwnerRpc(bool value)
    {
        Debug.Log("Stunned");
        _isStunned = value;
    }
}
