using UnityEngine;

public class PlayerCameraTarget : MonoBehaviour
{
    [HideInInspector] public Transform Target;
    [HideInInspector] public Vector3 Offset;
    [HideInInspector] public TransformInterpolator Interpolator;

    private void Awake()
    {
        Interpolator = GetComponent<TransformInterpolator>();
    }

    private void FixedUpdate()
    {
        if (Target != null)
        {
            transform.position = Target.position + Offset;
        }
    }

    public void TeleportToTarget()
    {
        if (Target != null)
        {
            transform.Teleport(Target.position + Offset, transform.rotation);
        }
    }
}
