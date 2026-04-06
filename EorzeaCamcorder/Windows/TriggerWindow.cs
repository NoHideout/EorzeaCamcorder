using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace EorzeaCamcorder.Trigger;

public class TriggerWindow : Window, IDisposable
{
    private Configuration config => Service.Config;

    private readonly Dictionary<int, string> _searchStrings = new();

    private static readonly Dictionary<TriggerType, (string Name, string Description)> TriggerInfo = new()
    {
        { TriggerType.PlayerDeath, ("Player Death", "Fires when your character reaches 0 HP.") },
        { TriggerType.LowHp, ("Low HP", "Fires when your character's HP falls below the set percentage.") },
        { TriggerType.EnterCombat, ("Enter Combat", "Fires when you engage an enemy.") },
        { TriggerType.LeaveCombat, ("Leave Combat", "Fires when you exit combat.") },
        { TriggerType.EnterDeepDungeon, ("Enter Deep Dungeon", "Fires when you zone into a Deep Dungeon.") },
        { TriggerType.LeaveDeepDungeon, ("Leave Deep Dungeon", "Fires when you exit a Deep Dungeon.") },
        { TriggerType.EnterDuty, ("Enter Duty", "Fires when you enter an instanced duty.") },
        { TriggerType.LeaveDuty, ("Leave Duty", "Fires when you exit an instanced duty.") },
        
        { TriggerType.DutyStarted, ("Duty Started", "Fires after 'Duty Commenced' appears.") },
        { TriggerType.DutyWiped, ("Duty Wiped", "Fires when the entire party is defeated and the screen fades to black.") },
        { TriggerType.DutyRecommenced, ("Duty Recommenced", "Fires when you respawn and the barrier drops.") },
        { TriggerType.DutyCompleted, ("Duty Completed", "Fires only when the duty boss is defeated and 'Duty Complete' appears.") }
    };

    private static readonly Dictionary<TriggerAction, (string Name, string Description)> ActionInfo = new()
    {
        { TriggerAction.None, ("None", "Does nothing.") },
        { TriggerAction.StartRecording, ("Start Recording", "Starts a normal recording if one isn't already running.") },
        { TriggerAction.StopRecording, ("Stop Recording", "Stops the current normal recording.") },
        { TriggerAction.StartBuffer, ("Start Replay Buffer", "Starts the replay buffer if it isn't already running.") },
        { TriggerAction.StopBuffer, ("Stop Replay Buffer", "Stops the running replay buffer.") },
        { TriggerAction.SaveReplay, ("Save Replay", "Saves the last X seconds from the replay buffer to a file.") }
    };

    public TriggerWindow() : base("Trigger Configuration")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(400, 300)
        };
    }

    public void Dispose() { }

    public override void Draw()
    {
        bool save = false;

        if (ImGui.Button("Add Trigger"))
        {
            config.Triggers.Add(new TriggerConfig());
            save = true;
        }

        ImGui.Separator();

        for (int i = 0; i < config.Triggers.Count; i++)
        {
            var t = config.Triggers[i];
            ImGui.PushID(i);

            ImGui.BeginGroup();
            ImGui.Text($"Trigger #{i + 1}");

            string currentTypeName = TriggerInfo.TryGetValue(t.Type, out var tInfo) ? tInfo.Name : t.Type.ToString();
            
            if (ImGui.BeginCombo("Type", currentTypeName))
            {
                if (!_searchStrings.ContainsKey(i)) _searchStrings[i] = "";
                string search = _searchStrings[i];

                ImGui.InputTextWithHint("##search", "Search...", ref search, 100);
                _searchStrings[i] = search;
                
                ImGui.Separator();

                foreach (var type in Enum.GetValues<TriggerType>())
                {
                    var info = TriggerInfo.TryGetValue(type, out var iInfo) ? iInfo : (Name: type.ToString(), Description: "");                    
                    if (!string.IsNullOrEmpty(search) && !info.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
                        continue;

                    bool isSelected = t.Type == type;
                    if (ImGui.Selectable(info.Name, isSelected))
                    {
                        t.Type = type;
                        save = true;
                    }

                    DrawTooltipIfHovered(info.Description);

                    if (isSelected) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }

            DrawTooltipIfHovered(tInfo.Description);

            if (t.Type == TriggerType.LowHp)
            {
                float th = t.Threshold;
                if (ImGui.SliderFloat("HP Threshold", ref th, 0.01f, 1.0f, "%.2f"))
                {
                    t.Threshold = th;
                    save = true;
                }
            }

            string currentActionName = ActionInfo.TryGetValue(t.Action, out var aInfo) ? aInfo.Name : t.Action.ToString();
            
            if (ImGui.BeginCombo("Action", currentActionName))
            {
                foreach (var action in Enum.GetValues<TriggerAction>())
                {
                    var info = ActionInfo.TryGetValue(action, out var iInfo) ? iInfo : (Name: action.ToString(), Description: "");                    
                    bool isSelected = t.Action == action;
                    if (ImGui.Selectable(info.Name, isSelected))
                    {
                        t.Action = action;
                        save = true;
                    }

                    DrawTooltipIfHovered(info.Description);

                    if (isSelected) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
            DrawTooltipIfHovered(aInfo.Description);

            if (ImGui.Button("Delete"))
            {
                config.Triggers.RemoveAt(i);
                _searchStrings.Remove(i);
                save = true;
                ImGui.EndGroup();
                ImGui.PopID();
                break;
            }

            ImGui.EndGroup();
            ImGui.Separator();

            ImGui.PopID();
        }

        if (save)
        {
            config.Save();
            Service.TriggerManager.Reload();
        }
    }

    private void DrawTooltipIfHovered(string description)
    {
        if (string.IsNullOrEmpty(description) || !ImGui.IsItemHovered()) return;

        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 35f); 
        ImGui.TextUnformatted(description);
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
    }
}
