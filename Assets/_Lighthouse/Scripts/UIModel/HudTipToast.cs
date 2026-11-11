using System;
using System.Collections.Generic;
using System.Linq;
using Lighthouse.Core;

namespace Lighthouse.UI.Model
{
    /// <summary>위쪽 가운데 표시줄(원본 updateHud)의 값.</summary>
    public sealed class HudInfo
    {
        public string StageText, Label, Forecast, ForecastKind;     // ForecastKind: "" | fog | rain | rainbow
        public double Count;
        public float Pct;
        public string[] LabelParts = new string[0];                 // " · " 로 나눈 덩어리(덩어리 안에서는 줄이 바뀌지 않는다)
    }

    /// <summary>날씨 예보에 필요한 런타임 값(날씨 연출은 M4 가 채운다).</summary>
    public sealed class ForecastInput { public bool Rainbow; public string NextKind; public double NextInSec = double.MaxValue; public string RainWord = "비"; }

    public static class Hud
    {
        public static HudInfo Compute(GameContext c, UiStrings s, ForecastInput fc = null)
        {
            var T = c.T; var S = c.S; int stage = c.Stage; double goal = T.Goal[stage]; int need = T.DexNeed[stage]; int dex = c.Discovered();
            var h = new HudInfo { Count = S.Guided };
            double pct; string lab;
            var b = S.Build;
            if (b != null)
            {
                long now = c.Clock.NowMs; double left = Math.Max(0, Math.Ceiling((b.EndAt - now) / 1000.0));
                pct = Math.Min(100, (now - b.StartAt) / (b.EndAt - b.StartAt) * 100);
                lab = s.F("hud.building", T.StageName[(int)b.To], Math.Floor(left / 60), ((int)left % 60).ToString("00"));
            }
            else if (goal <= 0) { pct = 100; lab = ""; }
            else if (S.Guided >= goal && dex < need) { pct = 100; lab = s.F("hud.moreDex", dex, need); }
            else { pct = Math.Min(100, S.Guided / goal * 100); lab = s.F("hud.next", Math.Min(S.Guided, goal), goal) + (need > 0 ? " · " + s.F("hud.dex", Math.Min(dex, need), need) : ""); }
            h.Pct = (float)pct; h.Label = lab;
            h.LabelParts = lab == "" ? new string[0] : lab.Split(new[] { " · " }, StringSplitOptions.None);
            h.StageText = T.StageName[stage] + (b != null ? s.Get("hud.buildingSuffix") : "");
            h.Forecast = Forecast(c, s, fc ?? new ForecastInput(), out h.ForecastKind);
            return h;
        }

        static string Forecast(GameContext c, UiStrings s, ForecastInput fc, out string kind)
        {
            kind = "";
            if (c.Tier() < 2) return "";
            if (c.Wx == "fog") { kind = "fog"; return s.Get("hud.fogNow"); }
            if (c.Wx == "rain") { kind = "rain"; return GameContext.FixJosa(s.F("hud.rainNow", fc.RainWord)); }
            if (fc.Rainbow) { kind = "rainbow"; return s.Get("hud.rainbow"); }
            if (c.Tier() >= 3 && fc.NextKind != null && fc.NextInSec < 120)
            {
                int sec = (int)Math.Max(0, Math.Ceiling(fc.NextInSec));
                return s.F("hud.forecast", fc.NextKind == "rain" ? fc.RainWord : s.Get("hud.fogWord"), sec / 60, (sec % 60).ToString("00"));
            }
            return "";
        }

        /// <summary>타이틀 아래 줄(원본 titleDay).</summary>
        public static string TitleDay(GameContext c, UiStrings s, bool first) => first ? s.Get("title.start") : s.F("title.day", c.DaysSinceStart() + 1);
    }

    /// <summary>알림 토스트(원본 showToast): 5초 뒤 사라진다. 순수 로직.</summary>
    public sealed class ToastModel
    {
        public string Title, Sub; public long ShownAt; public bool Visible;
        public const long LifeMs = 5000;
        public void Show(string title, string sub, long nowMs) { Title = GameContext.FixJosa(title); Sub = sub != null ? GameContext.FixJosa(sub) : null; ShownAt = nowMs; Visible = true; }
        public bool Tick(long nowMs) { if (Visible && nowMs - ShownAt >= LifeMs) { Visible = false; return true; } return false; }
        /// <summary>0~1 투명도(원본 keyframes: 8% 까지 나타나고 82% 부터 사라짐).</summary>
        public float Alpha(long nowMs)
        {
            if (!Visible) return 0f; float t = Math.Min(1f, (nowMs - ShownAt) / (float)LifeMs);
            return t < 0.08f ? t / 0.08f : t > 0.82f ? Math.Max(0f, 1f - (t - 0.82f) / 0.18f) : 1f;
        }
    }

    /// <summary>안내 카드가 뜰 수 있는 때를 알려 주는 바깥 상태.</summary>
    public sealed class TipEnv
    {
        public bool TitleOpen, PanelOpen, Building, CanUpgrade, ToastVisible, Sim, MenuVisible = true;
        public PanelMode PanelMode; public string DecoTab = "place";
    }

    /// <summary>온보딩 안내 카드(원본 TIPS·updateTips): 조건이 맞을 때 한 번씩, 4.5초 뒤 사라지고, 모두 끌 수 있다.</summary>
    public sealed class TipEngine
    {
        readonly GameContext c; readonly UiStrings s;
        public TipInfo Current; long endAt, holdUntil, nextAt;
        public const long LifeMs = 4500;
        readonly HashSet<string> deferred = new HashSet<string>();
        public TipEngine(GameContext ctx, UiStrings str) { c = ctx; s = str; }

        bool When(TipInfo t, TipEnv env)
        {
            var S = c.S;
            switch (t.Id)
            {
                case "h1": return S.Guided >= 1;
                case "h2": return S.Found.Count > 0;
                case "h3": return c.UnreadCount() > 0 && env.MenuVisible;
                case "h4": return c.Stage == 1 && S.Guided >= c.T.Goal[1] - 4;
                case "h5": return c.Stage >= 2;
                case "h6": return S.Guided >= 6;
                case "h8": return (env.PanelOpen && env.PanelMode == PanelMode.Deco && env.DecoTab == "place") || deferred.Contains("h8");
            }
            return false;
        }
        bool Skip(TipInfo t) => t.Id == "h3" && c.S.Letters.Any(l => l.Read);

        /// <summary>한 프레임: 새로 보여 줄 카드를 돌려준다(없으면 null). 사라진 카드는 Current 가 null 이 된다.</summary>
        public TipInfo Update(long now, TipEnv env, out bool hid)
        {
            hid = false;
            if (env.TitleOpen) { holdUntil = now + 800; return null; }
            if (env.Sim || c.S.TipsOff) { if (Current != null) { Hide(now); hid = true; } return null; }
            bool InOk(TipInfo t) => env.PanelOpen && t.InPanel != null && t.InPanel == env.PanelMode.ToString().ToLowerInvariant();
            if (Current != null)
            {
                if (now >= endAt || (env.PanelOpen && !InOk(Current)) || env.Building) { Hide(now); hid = true; }
                return null;
            }
            if (env.PanelOpen && env.PanelMode != PanelMode.Deco) { holdUntil = now + 1000; return null; }
            if (env.Building || env.CanUpgrade) { holdUntil = now + 1500; return null; }
            if (env.ToastVisible) { holdUntil = now + 1000; return null; }
            if (now < holdUntil || now < nextAt) return null;
            foreach (var t in s.Tips)
            {
                if (env.PanelOpen && !InOk(t)) continue;
                if (c.S.Tips.TryGetValue(t.Id, out double seen) && seen != 0) continue;
                if (!When(t, env)) continue;
                if (Skip(t)) { c.S.Tips[t.Id] = 1; c.NotifyChanged(); continue; }
                Current = t; c.S.Tips[t.Id] = 1; c.NotifyChanged(); endAt = now + LifeMs; deferred.Remove(t.Id);
                return t;
            }
            return null;
        }

        /// <summary>자리가 없어 놓지 못한 카드는 창이 닫힌 뒤로 미룬다(D31).</summary>
        public void Defer(TipInfo t, long now) { Current = null; c.S.Tips.Remove(t.Id); deferred.Add(t.Id); holdUntil = now + 1500; }

        public void Hide(long now) { Current = null; nextAt = now + 2000; }
        public void TurnOff() { c.S.TipsOff = true; c.NotifyChanged(); Current = null; }
    }
}
