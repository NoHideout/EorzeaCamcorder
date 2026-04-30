using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using EorzeaCamcorder.Trigger;

namespace EorzeaCamcorder.Windows;

public class TriggerWindow : Window, IDisposable
{
    private Configuration config => Service.Config;
    private readonly Dictionary<string, string> _searchStrings = new();

    private static readonly Dictionary<TriggerType, (string Name, string Description)> TriggerInfo = new()
    {
        { TriggerType.PlayerDeath, ("Death", "Fires when your character dies.") },
        { TriggerType.LowHp, ("Low HP", "Fires when your character's HP falls below the set percentage.") },
        { TriggerType.EnterCombat, ("Enter Combat", "Fires when you engage an enemy.") },
        { TriggerType.LeaveCombat, ("Leave Combat", "Fires when you exit combat.") },
        { TriggerType.EnterDeepDungeon, ("Enter Deep Dungeon", "Fires when you zone into a Deep Dungeon.") },
        { TriggerType.LeaveDeepDungeon, ("Leave Deep Dungeon", "Fires when you exit a Deep Dungeon.") },
        { TriggerType.EnterDuty, ("Enter Duty", "Fires when you enter an instanced duty.") },
        { TriggerType.LeaveDuty, ("Leave Duty", "Fires when you exit an instanced duty.") },
        { TriggerType.PartyMemberDeath, ("Party Death", "Fires when anyone in your Party dies excluding yourself.") },
        { TriggerType.StartPerforming, ("Start Performing", "Fires when you start performing with a musical instrument.") },
        { TriggerType.StopPerforming, ("Stop Performing", "Fires when you stop performing with a musical instrument.") },

        { TriggerType.DutyStarted, ("Duty Started", "Fires after 'Duty Commenced' appears.") },
        { TriggerType.DutyWiped, ("Duty Wiped", "Fires when the entire party is defeated and the screen fades to black.") },
        { TriggerType.DutyRecommenced, ("Duty Recommenced", "Fires when you respawn and the barrier drops.") },
        { TriggerType.DutyCompleted, ("Duty Completed", "Fires only when the duty boss is defeated and 'Duty Complete' appears.") },
        { TriggerType.EnterPvP, ("Enter PvP", "Fires when you enter PvP.") },
        { TriggerType.LeavePvP, ("Leave PvP", "Fires when you leave PvP.") },
        { TriggerType.EnterGpose, ("Enter Gpose", "Fires when you enter Group pose mode.") },
        { TriggerType.LeaveGpose, ("Leave Gpose", "Fires when you leave Group pose mode.") },
    };

    private static readonly Dictionary<TriggerAction, (string Name, string Description)> ActionInfo = new()
    {
        { TriggerAction.None, ("None", "Does nothing.") },
        { TriggerAction.StartRecording, ("Start Recording", "Starts a normal recording if one isn't already running.") },
        { TriggerAction.StopRecording, ("Stop Recording", "Stops the current normal recording.") },
        { TriggerAction.StartBuffer, ("Start Replay Buffer", "Starts the replay buffer if it isn't already running.") },
        { TriggerAction.StopBuffer, ("Stop Replay Buffer", "Stops the running replay buffer.") },
        { TriggerAction.SaveReplay, ("Save Replay", "Saves the last X seconds from the replay buffer to a file.") },
        { TriggerAction.AddChapterMarker, ("Add Chapter Marker", "Adds a chapter marker to the video. Useful for video editing and playback.") }
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
            
            using var id = ImRaii.PushId(i);
            
            using (var group = ImRaii.Group())
            {
                ImGui.Text($"Trigger #{i + 1}");

                if (DrawSearchableCombo("Type", ref t.Type, TriggerInfo, $"{i}_type")) save = true;

                if (t.Type == TriggerType.LowHp)
                {
                    float th = t.Threshold;
                    if (ImGui.SliderFloat("HP Threshold", ref th, 0.01f, 1.0f, "%.2f"))
                    {
                        t.Threshold = th;
                        save = true;
                    }
                }

                if (DrawSearchableCombo("Action", ref t.Action, ActionInfo, $"{i}_action")) save = true;
                
                if (t.Action == TriggerAction.SaveReplay)
                {
                    string[] posNames = { "At the End", "In the Middle", "At the Start" };
                    int currentPos = (int)t.EventPosition;
                    
                    if (ImGui.Combo("Event Position", ref currentPos, posNames, posNames.Length))
                    {
                        t.EventPosition = (ReplayEventPosition)currentPos;
                        save = true;
                    }
                    DrawTooltipIfHovered("Where the triggering event should appear in the saved clip.");
                }
                
                if (ImGui.Button("Delete"))
                {
                    config.Triggers.RemoveAt(i);
                    _searchStrings.Remove($"{i}_type"); 
                    _searchStrings.Remove($"{i}_action"); 
                    save = true;
                    break;
                }
            }

            ImGui.Separator();
        }

        if (save)
        {
            config.Save();
            Service.TriggerManager.Reload();
        }
    }

    private bool DrawSearchableCombo<T>(string label, ref T currentValue, Dictionary<T, (string Name, string Description)> infoDict, string searchKey) where T : struct, Enum
    {
        bool changed = false;
        string currentName = infoDict.TryGetValue(currentValue, out var info) ? info.Name : currentValue.ToString();

        using (var combo = ImRaii.Combo(label, currentName))
        {
            if (combo)
            {
                if (!_searchStrings.TryGetValue(searchKey, out string? search))
                {
                    search = "";
                }
                
                ImGui.InputTextWithHint("##search", "Search...", ref search, 100);
                _searchStrings[searchKey] = search; 
                
                ImGui.Separator();

                foreach (var item in Enum.GetValues<T>())
                {
                    var itemInfo = infoDict.TryGetValue(item, out var iInfo) ? iInfo : (Name: item.ToString(), Description: "");
                    
                    if (!string.IsNullOrEmpty(search) && !itemInfo.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
                        continue;
                    
                    bool isSelected = EqualityComparer<T>.Default.Equals(currentValue, item);
                    
                    if (ImGui.Selectable(itemInfo.Name, isSelected))
                    {
                        currentValue = item;
                        changed = true;
                    }

                    DrawTooltipIfHovered(itemInfo.Description);
                }
            }
        }
        
        DrawTooltipIfHovered(info.Description);

        return changed;
    }

    private void DrawTooltipIfHovered(string description)
    {
        if (string.IsNullOrEmpty(description) || !ImGui.IsItemHovered()) return;

        using (ImRaii.Tooltip())
        {
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 35f); 
            ImGui.TextUnformatted(description);
            ImGui.PopTextWrapPos();
        }
    }
}
