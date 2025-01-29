using System;
using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;

public struct GameTimer
{
    public class Callback
    {
        public float TriggerTime;
        public Action<float> OnCallback;
        public Action OnCancel;
    }

    public float Duration;
    public float Time;
    public int CallbackIndex;
    public List<Callback> Callbacks;

    public bool IsEnded => Time >= Duration;

    public GameTimer(float duration)
    {
        Duration = duration;
        Time = 0;
        CallbackIndex = 0;
        Callbacks = new();
    }

    public void Reset()
    {
        Time = 0;
        CallbackIndex = 0;
    }

    public void RollbackTo(float time)
    {
        for (var i = Callbacks.Count; i >= 0; --i)
        {
            var callback = Callbacks[i];
            if (callback.TriggerTime < time)
                break;

            callback.OnCancel?.Invoke();
        }
    }

    public Callback AddCallback(float triggerTime, Action<float> onCallback, Action onCancel = null)
    {
        var callback = new Callback { TriggerTime = triggerTime, OnCallback = onCallback, OnCancel = onCancel };
        Callbacks.Add(callback);
        Callbacks = Callbacks.OrderBy(t => t.TriggerTime).ToList();
        return callback;
    }

    public void RemoveCallback(Callback callback)
    {
        Callbacks.Remove(callback);
    }

    public void Tick(float deltaTime)
    {
        if (IsEnded)
            return;

        Time = Mathf.Min(Time + deltaTime, Duration);

        for (var i = CallbackIndex; i < Callbacks.Count; ++i)
        {
            var callback = Callbacks[i];
            if (callback.TriggerTime > Time)
                break;

            callback.OnCallback(Time);
            CallbackIndex = i + 1;
        }
    }
}
