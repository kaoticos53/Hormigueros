using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public static class ProbeLive
{
    static object Field(object o, string name)
    {
        if (o == null) return null;
        var t = o.GetType();
        while (t != null)
        {
            var f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null) return f.GetValue(o);
            t = t.BaseType;
        }
        return null;
    }

    static object Prop(object o, string name)
    {
        if (o == null) return null;
        var t = o.GetType();
        var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        return p != null ? p.GetValue(o) : null;
    }

    static Type Find(string fullName)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType(fullName);
            if (t != null) return t;
        }
        return null;
    }

    public static string Run()
    {
        var sb = new System.Text.StringBuilder();
        var pbType = Find("AntSim.Unity.Scripts.Presenter.SimPresenterBehaviour");
        var hbType = Find("AntSim.Unity.Scripts.Presenter.HudLayoutBehaviour");
        sb.Append("pbType=").Append(pbType != null).Append(" hbType=").Append(hbType != null).Append('\n');

        var pb = pbType != null ? UnityEngine.Object.FindAnyObjectByType(pbType) as Component : null;
        var hb = hbType != null ? UnityEngine.Object.FindAnyObjectByType(hbType) as Component : null;
        sb.Append("pb=").Append(pb != null).Append(" hb=").Append(hb != null).Append('\n');

        if (pb != null)
        {
            var pres = Prop(pb, "Presenter");
            sb.Append("Speed=").Append(Field(pb, "Speed"));
            sb.Append(" SeedPool=").Append(Field(pb, "SeedPoolPath"));
            sb.Append(" Replay=").Append(Field(pb, "ReplayFile")).Append('\n');
            if (pres != null)
            {
                sb.Append("FinalTick=").Append(Prop(pres, "FinalTick"));
                var cur = Prop(pres, "CurrentTick");
                if (cur != null)
                {
                    sb.Append(" curTick=").Append(Field(cur, "Tick"));
                    var ants = Field(cur, "Ants") as ICollection;
                    var evs = Field(cur, "Events") as ICollection;
                    var als = Field(cur, "Alerts") as ICollection;
                    sb.Append(" ants=").Append(ants != null ? ants.Count : -1);
                    sb.Append(" events=").Append(evs != null ? evs.Count : -1);
                    sb.Append(" alerts=").Append(als != null ? als.Count : -1);
                }
                else sb.Append(" cur=null");
                sb.Append('\n');
            }
        }

        if (hb != null)
        {
            var toasts = Prop(hb, "Toasts");
            if (toasts != null)
            {
                var active = Prop(toasts, "Active") as IEnumerable;
                int n = 0;
                sb.Append("TOASTS:");
                if (active != null)
                    foreach (var t in active)
                    {
                        n++;
                        string key = (string)Field(t, "Key");
                        object lvl = Field(t, "Level");
                        string txt = (string)Field(t, "Text");
                        sb.Append(" [").Append(key).Append("|lvl=").Append(lvl).Append("|").Append(txt).Append("]");
                    }
                if (n == 0) sb.Append(" none");
                sb.Append('\n');
            }
            else sb.Append("TOASTS: (model null)\n");

            var cards = Prop(hb, "Cards");
            if (cards != null)
            {
                var dict = Prop(cards, "Cards") as IDictionary;
                if (dict != null)
                {
                    sb.Append("CARDS:");
                    foreach (var k in dict.Keys)
                    {
                        var rendered = cards.GetType().GetMethod("Render")?.Invoke(cards, new object[] { k });
                        string one = rendered as string;
                        if (one != null) one = one.Replace('\n', '/');
                        sb.Append(" [").Append(k).Append("=").Append(one).Append("]");
                    }
                    sb.Append('\n');
                }
            }

            var cont = Field(hb, "ToastContainer");
            if (cont != null)
            {
                var rt = cont as Transform;
                var kids = new List<string>();
                foreach (Transform c in rt)
                    if (c.gameObject.name.StartsWith("Toast_")) kids.Add(c.gameObject.name);
                sb.Append("ToastContainer children=").Append(kids.Count);
                foreach (var k in kids) sb.Append(" ").Append(k);
                sb.Append('\n');
            }
            else sb.Append("ToastContainer=null\n");
        }
        return sb.ToString();
    }
}
