using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace EorzeaCamcorder.Trigger;

public class TriggerWindow : Window, IDisposable
{
    private Configuration config => Service.Config;

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

            int type = (int)t.Type;
            if (ImGui.Combo("Type", ref type, Enum.GetNames<TriggerType>(), Enum.GetNames<TriggerType>().Length))
            {
                t.Type = (TriggerType)type;
                save = true;
            }

            if (t.Type == TriggerType.LowHp)
            {
                float th = t.Threshold;
                if (ImGui.SliderFloat("HP Threshold", ref th, 0.01f, 1.0f))
                {
                    t.Threshold = th;
                    save = true;
                }
            }

            int action = (int)t.Action;
            if (ImGui.Combo("Action", ref action, Enum.GetNames<TriggerAction>(), Enum.GetNames<TriggerAction>().Length))
            {
                t.Action = (TriggerAction)action;
                save = true;
            }

            if (ImGui.Button("Delete"))
            {
                config.Triggers.RemoveAt(i);
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
}
