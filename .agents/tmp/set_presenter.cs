using System;
using System.Reflection;
using UnityEngine;

public static class SetPresenter
{
    static Type Find(string fullName)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType(fullName);
            if (t != null) return t;
        }
        return null;
    }

    static void Set(object o, string name, object v)
    {
        var t = o.GetType();
        while (t != null)
        {
            var f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null) { f.SetValue(o, v); return; }
            t = t.BaseType;
        }
    }

    public static string Run()
    {
        var pbType = Find("AntSim.Unity.Scripts.Presenter.SimPresenterBehaviour");
        var pb = pbType != null ? UnityEngine.Object.FindAnyObjectByType(pbType) as Component : null;
        if (pb == null) return "presenter not found";
        Set(pb, "Seed", (ulong)42);
        Set(pb, "Ticks", 7200);
        Set(pb, "Grid", 96);
        Set(pb, "Colonies", 2);
        Set(pb, "Speed", 1f);
        Set(pb, "SeedPoolPath", "artifacts/pretrain-warm-v2.antgenome");
        Set(pb, "ReplayFile", null);
        return "ok Seed=42 Ticks=7200 Grid=96 Speed=2 pool=warm-v2";
    }
}
