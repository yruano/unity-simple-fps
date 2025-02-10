using UnityEngine;

public class BulletTrail : MonoBehaviour
{
    private LineRenderer _lineRenderer;
    private float _duration;

    private void Awake()
    {
        _lineRenderer = GetComponent<LineRenderer>();
    }

    public void Init(float duration, Vector3 start, Vector3 end)
    {
        _lineRenderer.SetPosition(0, start);
        _lineRenderer.SetPosition(1, end);
        _duration = duration;
        Destroy(gameObject, _duration);
    }
}
