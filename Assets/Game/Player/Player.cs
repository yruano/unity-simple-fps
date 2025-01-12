using Unity.Netcode;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

public class Player : NetworkBehaviour
{
    [SerializeField] private float WalkSpeed = 4.0f;
    [SerializeField] private Transform CameraTarget;

    private CinemachineCamera _cmFpsCamera;
    private Rigidbody _rb;

    private InputAction _move;

    private void Awake()
    {
        _cmFpsCamera = GameObject.Find("Cm FPS Camera").GetComponent<CinemachineCamera>();

        _rb = GetComponent<Rigidbody>();

        _move = InputSystem.actions.FindAction("Player/Move");
    }

    private void Start()
    {
        if (IsOwner)
        {
            _cmFpsCamera.Target.TrackingTarget = CameraTarget;
        }
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
        var cameraRotation = _cmFpsCamera.transform.eulerAngles;
        var rotation = transform.eulerAngles;
        rotation.y = cameraRotation.y;
        transform.eulerAngles = rotation;
    }

    private void Movement()
    {
        var inputDir = _move.ReadValue<Vector2>();

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
}