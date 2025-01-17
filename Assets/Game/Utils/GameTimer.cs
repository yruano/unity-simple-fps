using System;
using System.Collections.Generic;
using System.Linq;

public class GameTimerCallback
{
    public float TriggerTime;
    public bool Called;
    public Action<float> Callback;
}

public class GameTimer
{
    public float Duration;
    public float Time;
    public bool IsEnded;
    public List<GameTimerCallback> Callbacks = new();

    public GameTimer(float duration)
    {
        Duration = duration;
    }

    public void Tick(float delteTime)
    {
        if (!IsEnded)
        {
            Time += delteTime;

            if (Time >= Duration)
            {
                Time = Duration;
                IsEnded = true;
            }

            foreach (var callback in Callbacks)
            {
                if (!callback.Called)
                {
                    if (callback.TriggerTime <= Time)
                    {
                        callback.Called = true;
                        callback.Callback(Time);
                    }
                }
            }
        }
    }

    public void Reset()
    {
        Time = 0;
        IsEnded = false;

        foreach (var callback in Callbacks)
        {
            callback.Called = false;
        }
    }

    public void RegisterCallback(float triggerTime, Action<float> callback)
    {
        Callbacks.Add(new GameTimerCallback { TriggerTime = triggerTime, Callback = callback });
        Callbacks = Callbacks.OrderBy(t => t.TriggerTime).ToList();
    }
}