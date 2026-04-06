using System;

namespace EorzeaCamcorder.Trigger;

#region Enums

public enum TriggerType
{
    PlayerDeath = 0,
    LowHp = 1,
    EnterCombat = 2,
    LeaveCombat = 3,
    EnterDeepDungeon = 4,
    LeaveDeepDungeon = 5,
    EnterDuty = 6,
    LeaveDuty = 7,
    
    //event based
    DutyStarted = 100,
    DutyWiped = 101,
    DutyRecommenced = 102,
    DutyCompleted = 103
}

public enum TriggerAction
{
    None = 0,
    StartRecording = 1,
    StopRecording = 2,
    StartBuffer = 3,
    StopBuffer = 4,
    SaveReplay = 5
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

public class RuntimeTrigger: IDisposable
{
    public Func<bool>? Condition;
    public Action Execute = null!;
    public Action? OnDispose;

    private bool _lastState;

    public void Update()
    {
        if (Condition == null) return;
        bool current = Condition();

        if (current && !_lastState)
        {
            Execute();
        }

        _lastState = current;
    }
    public void Dispose()
    {
        OnDispose?.Invoke();
    }
}

#endregion

#region System

public static class TriggerSystem
{
    public static RuntimeTrigger Build(TriggerConfig config)
    {
        var trigger = new RuntimeTrigger
        {
            Execute = () => ExecuteAction(config.Action)
        };
        
        switch (config.Type)
        {
            // Condition
            case TriggerType.PlayerDeath:
                trigger.Condition = () => Service.ObjectTable.LocalPlayer?.IsDead ?? false;
                break;
            case TriggerType.LowHp:
                trigger.Condition = () =>
                {
                    var p = Service.ObjectTable.LocalPlayer;
                    if (p == null) return false;
                    return (p.CurrentHp / (float)p.MaxHp) <= config.Threshold;
                };
                break;
            case TriggerType.EnterCombat:
                trigger.Condition = () => Service.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.InCombat];
                break;
            case TriggerType.LeaveCombat:
                trigger.Condition = () => !Service.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.InCombat];
                break;
            case TriggerType.EnterDeepDungeon:
                trigger.Condition = () => Service.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.InDeepDungeon];
                break;
            case TriggerType.LeaveDeepDungeon:
                trigger.Condition = () => !Service.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.InDeepDungeon];
                break;
            case TriggerType.EnterDuty:
                trigger.Condition = () => Service.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.BoundByDuty];
                break;
            case TriggerType.LeaveDuty:
                trigger.Condition = () => !Service.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.BoundByDuty];
                break;

            // Event
            case TriggerType.DutyStarted:
            {
                EventHandler<ushort> handler = (_, _) => trigger.Execute();
                Service.DutyState.DutyStarted += handler;
                trigger.OnDispose = () => Service.DutyState.DutyStarted -= handler;
                break;
            }
            case TriggerType.DutyWiped:
            {
                EventHandler<ushort> handler = (_, _) => trigger.Execute();
                Service.DutyState.DutyWiped += handler;
                trigger.OnDispose = () => Service.DutyState.DutyWiped -= handler;
                break;
            }
            case TriggerType.DutyRecommenced:
            {
                EventHandler<ushort> handler = (_, _) => trigger.Execute();
                Service.DutyState.DutyRecommenced += handler;
                trigger.OnDispose = () => Service.DutyState.DutyRecommenced -= handler;
                break;
            }
            case TriggerType.DutyCompleted:
            {
                EventHandler<ushort> handler = (_, _) => trigger.Execute();
                Service.DutyState.DutyCompleted += handler;
                trigger.OnDispose = () => Service.DutyState.DutyCompleted -= handler;
                break;
            }
        }

        return trigger;
    }

    private static void ExecuteAction(TriggerAction action)
    {
        var recorder = Service.Recorder;

        switch (action)
        {
            case TriggerAction.StartRecording:
                if (!recorder.IsRecording) recorder.StartRecording(null, "User defined Trigger");
                break;
            case TriggerAction.StopRecording: 
                if (recorder.IsRecording) _ = recorder.StopRecording();
                break;
            case TriggerAction.SaveReplay: 
                if (recorder.IsReplayBufferRunning) recorder.SaveReplayBuffer(); 
                break;
            case TriggerAction.StartBuffer: 
                if (!recorder.IsReplayBufferRunning) recorder.StartReplayBuffer("User defined Trigger");
                break;
            case TriggerAction.StopBuffer: 
                if (recorder.IsReplayBufferRunning) _ = recorder.StopReplayBuffer();
                break;
        }
    }
}

#endregion
