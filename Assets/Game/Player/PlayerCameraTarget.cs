using UnityEngine;

public class PlayerCameraTarget : MonoBehaviour
{
    public Transform Target;
    public Vector3 Offset;

    private void Update()
    {
        MoveToTargetLerp();
    }

    public void MoveToTarget()
    {
        if (Target != null)
        {
            transform.position = Target.position + Offset;
        }
    }

    public void MoveToTargetLerp()
    {
        if (Target != null)
        {
            transform.position = Vector3.Lerp(transform.position, Target.position + Offset, 40 * Time.deltaTime);
        }
    }
}
