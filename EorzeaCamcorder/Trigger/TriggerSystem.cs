using System;

namespace EorzeaCamcorder.Trigger;

#region Enums

public enum TriggerType
{
    PlayerDeath = 0,
    LowHp = 1,
    EnterCombat = 2,
    LeaveCombat = 3
}

public enum TriggerAction
{
    None = 0,
    StartRecording = 1,
    StopRecording = 2,
    SaveReplay = 3
}

#endregion

#region Config

[Serializable]
public class TriggerConfig
{
    public TriggerType Type = TriggerType.PlayerDeath;
    public TriggerAction Action = TriggerAction.None;

    public float Threshold = 0.2f; // Low HP
}

#endregion

#region Runtime Trigger

public class RuntimeTrigger
{
    public Func<bool> Condition = null!;
    public Action Execute = null!;

    private bool _lastState;

    public void Update()
    {
        bool current = Condition();

        if (current && !_lastState)
        {
            Execute();
        }

        _lastState = current;
    }
}

#endregion

#region System

public static class TriggerSystem
{
    public static RuntimeTrigger Build(TriggerConfig config)
    {
        return new RuntimeTrigger
        {
            Condition = BuildCondition(config),
            Execute = () => ExecuteAction(config.Action)
        };
    }

    private static Func<bool> BuildCondition(TriggerConfig config)
    {
        return config.Type switch
        {
            TriggerType.PlayerDeath => () =>
            {
                var p = Service.ObjectTable.LocalPlayer;
                return p?.IsDead ?? false;
            },

            TriggerType.LowHp => () =>
            {
                var p = Service.ObjectTable.LocalPlayer;
                if (p == null) return false;

                float hp = p.CurrentHp / (float)p.MaxHp;
                return hp <= config.Threshold;
            },

            TriggerType.EnterCombat => () =>
                Service.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.InCombat],

            TriggerType.LeaveCombat => () =>
                !Service.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.InCombat],

            _ => () => false
        };
    }

    private static void ExecuteAction(TriggerAction action)
    {
        var recorder = Service.Recorder;

        switch (action)
        {
            case TriggerAction.StartRecording:
                if (!recorder.IsRecording)
                    recorder.StartRecording(null, "Trigger");
                break;

            case TriggerAction.StopRecording:
                if (recorder.IsRecording)
                    _ = recorder.StopRecording();
                break;

            case TriggerAction.SaveReplay:
                if (recorder.IsReplayBufferRunning)
                    recorder.SaveReplayBuffer();
                break;
        }
    }
}

#endregion
