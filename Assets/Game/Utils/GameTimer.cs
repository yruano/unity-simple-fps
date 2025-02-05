using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Netcode;

public struct GameTimer : INetworkSerializable
{
    public class Callback
    {
        public float TriggerTime;
        public Action OnCallback;
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

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Duration);
        serializer.SerializeValue(ref Time);
        serializer.SerializeValue(ref CallbackIndex);
    }

    public void CopyWithoutCallbacks(GameTimer other)
    {
        Duration = other.Duration;
        Time = other.Time;
        CallbackIndex = other.CallbackIndex;
    }

    public void Reset()
    {
        Time = 0;
        CallbackIndex = 0;
    }

    public Callback AddCallback(float triggerTime, Action onCallback, Action onCancel = null)
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

            callback.OnCallback();
            CallbackIndex = i + 1;
        }
    }
}
