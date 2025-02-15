using UnityEngine;
using Unity.Netcode;

public class Grenade : NetworkBehaviour
{
    private NetworkObject _networkObject;
    private GameTimer _despawnTimer = new(3.0f);
    private Rigidbody _rigidbody;
    private GameObject _visual;
    private Vector3 _startPos;

    private void Awake()
    {
        _networkObject = GetComponent<NetworkObject>();
        _rigidbody = GetComponent<Rigidbody>();
        _rigidbody.isKinematic = true;
        _rigidbody.interpolation = RigidbodyInterpolation.None;
        _visual = transform.GetChild(0).gameObject;
        _visual.SetActive(false);
    }

    public override void OnNetworkSpawn()
    {
        if (IsHost)
        {
            _rigidbody.isKinematic = false;
            _rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            _rigidbody.linearVelocity = transform.forward * 10f;
            _visual.SetActive(true);
        }

        _startPos = transform.position;

        base.OnNetworkSpawn();
    }

    private void Update()
    {
        if (!IsSpawned)
            return;

        if (!IsHost)
        {
            if (!_visual.activeSelf && _startPos != transform.position)
                _visual.SetActive(true);
        }

        if (IsHost)
        {
            _despawnTimer.Tick(Time.deltaTime);
            if (_despawnTimer.IsEnded)
            {
                // TODO: damage player in area
                _networkObject.Despawn();
            }
        }
    }
}
