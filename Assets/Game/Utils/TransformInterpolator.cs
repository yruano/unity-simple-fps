using UnityEngine;

public static class TransformUtils // Convenience functions:
{
    /// <summary>
    /// A SetParent() wrapper that works in a safe way and doesn't expose interpolation glitches.
    /// </summary>
    /// <param name="t"></param>
    /// <param name="parent"></param>
    /// <param name="transformLocalSpace"></param>
    public static void SetParentInterpolated(this Transform t, Transform parent, bool transformLocalSpace = true)
    {
        if (parent == t.parent) return;

        TransformInterpolator transformInterpolator = t.GetComponent<TransformInterpolator>();

        if (transformInterpolator != null && transformInterpolator.enabled)
        {
            // Skip interpolation for this frame:
            transformInterpolator.enabled = false;
            t.SetParent(parent, transformLocalSpace);
            transformInterpolator.enabled = true;
        }
        else
            t.SetParent(parent, transformLocalSpace);
    }

    /// <summary>
    /// A SetLocalPositionAndRotation()/SetPositionAndRotation() wrapper that suppresses interpolation.
    /// </summary>
    /// <param name="t"></param>
    /// <param name="position"></param>
    /// <param name="rotation"></param>
    /// <param name="transformLocalSpace"></param>
    public static void Teleport(this Transform t, Vector3 position, Quaternion rotation, bool transformLocalSpace = true)
    {
        TransformInterpolator transformInterpolator = t.GetComponent<TransformInterpolator>();

        if (transformInterpolator != null && transformInterpolator.enabled)
        {
            // Skip interpolation for this frame:
            transformInterpolator.enabled = false;

            if (transformLocalSpace)
                t.SetLocalPositionAndRotation(position, rotation);
            else
                t.SetPositionAndRotation(position, rotation);

            transformInterpolator.enabled = true;
        }
        else
        {
            if (transformLocalSpace)
                t.SetLocalPositionAndRotation(position, rotation);
            else
                t.SetPositionAndRotation(position, rotation);
        }
    }

}

/// <summary>
/// How to use TransformInterpolator properly:
/// 0. Make sure the gameobject executes its mechanics (transform-manipulations)
/// in FixedUpdate().
/// 1. Set the execution order for this script BEFORE all the other scripts
/// that execute mechanics.
/// 2. Attach (and enable) this component to every gameobject that you want to interpolate
/// (including the camera).
/// </summary>
public class TransformInterpolator : MonoBehaviour
{
    void OnDisable() // Restore current transform state:
    {
        if (isTransformInterpolated_)
        {
            transform.localPosition = transformData_.Position;
            transform.localRotation = transformData_.Rotation;
            transform.localScale = transformData_.Scale;

            isTransformInterpolated_ = false;
        }

        isEnabled_ = false;
    }

    void FixedUpdate()
    {
        // Restore current transform state in the first FixedUpdate() call of the frame.
        if (isTransformInterpolated_)
        {
            transform.localPosition = transformData_.Position;
            transform.localRotation = transformData_.Rotation;
            transform.localScale = transformData_.Scale;

            isTransformInterpolated_ = false;
        }
        // Cache current transform as the starting point for interpolation.
        prevTransformData_.Position = transform.localPosition;
        prevTransformData_.Rotation = transform.localRotation;
        prevTransformData_.Scale = transform.localScale;
    }

    void LateUpdate()   // Interpolate in Update() or LateUpdate().
    {
        // The TransformInterpolator could get enabled and then modified in this frame.
        // So we postpone the enabling procedure to refresh the starting point of interpolation.
        // And we just skip interpolation for the first frame.
        if (!isEnabled_)
        {
            OnEnabledProcedure();
            return;
        }

        // Cache the final transform state as the end point of interpolation.
        if (!isTransformInterpolated_)
        {
            transformData_.Position = transform.localPosition;
            transformData_.Rotation = transform.localRotation;
            transformData_.Scale = transform.localScale;

            // This promise matches the execution that follows after that.
            isTransformInterpolated_ = true;
        }

        // (Time.time - Time.fixedTime) is the "unprocessed" time according to documentation.
        float interpolationAlpha = (Time.time - Time.fixedTime) / Time.fixedDeltaTime;


        // Interpolate transform:
        transform.localPosition = Vector3.Lerp(prevTransformData_.Position,
        transformData_.Position, interpolationAlpha);
        transform.localRotation = Quaternion.Slerp(prevTransformData_.Rotation,
        transformData_.Rotation, interpolationAlpha);
        transform.localScale = Vector3.Lerp(prevTransformData_.Scale,
        transformData_.Scale, interpolationAlpha);
    }

    private void OnEnabledProcedure() // Captures initial transform state.
    {
        prevTransformData_.Position = transform.localPosition;
        prevTransformData_.Rotation = transform.localRotation;
        prevTransformData_.Scale = transform.localScale;

        isEnabled_ = true;
    }

    private struct TransformData
    {
        public Vector3 Position;
        public Vector3 Scale;
        public Quaternion Rotation;
    }

    private TransformData transformData_;
    private TransformData prevTransformData_;
    private bool isTransformInterpolated_ = false;
    private bool isEnabled_ = false;
}
