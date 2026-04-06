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
    EnterGpose = 8,
    LeaveGpose = 9,
    
    //event based
    DutyStarted = 100,
    DutyWiped = 101,
    DutyRecommenced = 102,
    DutyCompleted = 103,
    
    EnterPvP = 200,
    LeavePvP = 201
}

public enum TriggerAction
{
    None = 0,
    StartRecording = 1,
    StopRecording = 2,
    StartBuffer = 3,
    StopBuffer = 4,
    SaveReplay = 5,
    AddChapterMarker = 6
}

#endregion

#region Config

[Serializable]
public class TriggerConfig
{
    public TriggerType Type = TriggerType.PlayerDeath;
    public TriggerAction Action = TriggerAction.None;

    public float Threshold = 0.2f; // Low HP
    
    public ReplayEventPosition EventPosition = ReplayEventPosition.Middle;
}

#endregion

#region Runtime Trigger

public class RuntimeTrigger: IDisposable
{
    public Func<bool>? Condition;
    public Action Execute = null!;
    public Action? OnDispose;

    private bool _isInitialized;
    private bool _lastState;

    public void Update()
    {
        if (Condition == null) return;
        bool current = Condition();
        
        if (!_isInitialized)
        {
            _lastState = current;
            _isInitialized = true;
            return;
        }
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
            Execute = () => ExecuteAction(config) 
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
            case TriggerType.EnterGpose:
                trigger.Condition = () => Service.ClientState.IsGPosing;
                break;
            case TriggerType.LeaveGpose:
                trigger.Condition = () => !Service.ClientState.IsGPosing;
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
            case TriggerType.EnterPvP:
            {
                Action handler = () => trigger.Execute();
                Service.ClientState.EnterPvP += handler;
                trigger.OnDispose = () => Service.ClientState.EnterPvP -= handler;
                break;
            }
            case TriggerType.LeavePvP:
            {
                Action handler = () => trigger.Execute();
                Service.ClientState.LeavePvP += handler;
                trigger.OnDispose = () => Service.ClientState.LeavePvP -= handler;
                break;
            }
        }

        return trigger;
    }

    private static void ExecuteAction(TriggerConfig config)
    {
        var recorder = Service.Recorder;

        switch (config.Action)
        {
            case TriggerAction.StartRecording:
                recorder.StartRecording(null, "User defined Trigger");
                break;
            case TriggerAction.StopRecording: 
                _ = recorder.StopRecording();
                break;
            case TriggerAction.SaveReplay:
                recorder.SaveReplayBuffer(null, config.EventPosition); 
                break;
            case TriggerAction.StartBuffer: 
                recorder.StartReplayBuffer("User defined Trigger");
                break;
            case TriggerAction.StopBuffer: 
                _ = recorder.StopReplayBuffer();
                break;
            case TriggerAction.AddChapterMarker:
                recorder.AddChapterMarker(config.Type.ToString());
                break;
        }
    }
}

#endregion
