using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;

namespace EorzeaCamcorder.Trigger;

public class TriggerManager : IDisposable
{
    private readonly List<RuntimeTrigger> _triggers = new();

    public TriggerManager()
    {
        Reload();
        Service.Framework.Update += OnUpdate;
    }

    public void Reload()
    {
        _triggers.Clear();

        foreach (var cfg in Service.Config.Triggers)
        {
            _triggers.Add(TriggerSystem.Build(cfg));
        }
    }

    private void OnUpdate(IFramework framework)
    {
        foreach (var trigger in _triggers)
        {
            trigger.Update();
        }
    }

    public void Dispose()
    {
        Service.Framework.Update -= OnUpdate;
    }
}
