using System;
using System.Collections.Generic;
using System.Linq;
using Lighthouse.Core;
using Lighthouse.UI.Model;

namespace Lighthouse.World
{
    /// <summary>날씨·역할·환경 (원본 scheduleWx 4070행 ~ updateRoles 4099행, updateAmbient 4513행). 2단계부터 날씨, 3단계부터 구조 요청.</summary>
    public sealed partial class GameLoop
    {
        public string RainWord => Ctx.CurSeason() == "winter" ? Str.Get("world.snowWord") : Str.Get("world.rainWord");
        /// <summary>날씨 주기를 줄이는 개발용 배율 (원본 ?fast). 시험·가속 시계에서 쓴다.</summary>
        public double WxSlow = 1;
        public double RescueEverySec = 0;           // 0 이면 원본 (240~480초), 아니면 고정(개발용 ?rescue 3초)

        void ScheduleWx(long now)
        {
            string s = Ctx.CurSeason();
            Wx.Next = now + (s == "autumn" ? 100 + R() * 100 : 150 + R() * 150) * 1000 / WxSlow;
            Wx.NextKind = Ctx.Tier() >= 3 && R() < (s == "autumn" ? 0.25 : s == "winter" ? 0.8 : 0.6) ? "rain" : "fog";
        }

        void StartWx(string kind, long now)
        {
            Wx.Kind = kind; Wx.T0 = now; Wx.Until = now + (kind == "fog" ? 70 : 80) * 1000 / WxSlow; Wx.Count = 0; sirenAt = 0; Ctx.Wx = kind;
            if (kind == "fog") SendToast(Str.Get("world.fogStart"), Str.Get("world.fogStartSub"));
            else if (Ctx.CurSeason() == "winter") SendToast(Str.Get("world.snowStart"), Str.Get("world.snowStartSub"));
            else SendToast(Str.Get("world.rainStart"), Str.Get("world.rainStartSub"));
        }

        /// <summary>bonusGift: cond 는 방금 걷힌 날씨('fog'·'rain'). 걷힌 직후에도 그 날씨의 선물이 올 수 있게 "그때"를 덮어쓰고 무게를 높인다 (QA-305).</summary>
        void BonusGift(string title, string sub, string cond)
        {
            var bak = Ctx.CondOv;
            if (cond != null) { var c = Ctx.CondNow(); Ctx.CondOv = new Cond { Night = c.Night, Dawn = c.Dawn, Rain = cond == "rain" || c.Rain, Fog = cond == "fog" || c.Fog }; }
            string g = Ctx.RollGift("trade", cond); Ctx.CondOv = bak; Ctx.AddGift(g); Ctx.NotifyChanged();
            Pops.Add(new Pop { X = W / 2.0, Y = Terrain_CY - 70, Gift = g }); Say("pick");
            SendToast(title, GameContext.FixJosa(Str.F("world.bonusSub", sub, GiftName(g))));
        }
        const double Terrain_CY = 224;

        void EndWx(long now)
        {
            string kind = Wx.Kind; Wx.Kind = "clear"; Ctx.Wx = "clear";
            if (kind == "fog") { Bump("fog"); BonusGift(Str.Get("world.fogEnd"), Wx.Count != 0 ? Str.F("world.fogEndSub", Wx.Count) : Str.Get("world.fogEndSub0"), "fog"); }
            else if (Ctx.CurSeason() == "winter") { Bump("rain"); BonusGift(Str.Get("world.snowEnd"), Str.Get("world.snowEndSub"), null); }
            else { Bump("rain"); RainbowUntilMs = Ctx.Night ? 0 : now + 45000; BonusGift(Str.Get("world.rainEnd"), Ctx.Night ? Str.Get("world.rainEndNight") : Str.Get("world.rainEndDay"), "rain"); }
            ScheduleWx(now);
        }

        /// <summary>구조 요청: 느리게 떠다니는 표류선.</summary>
        void SpawnSos()
        {
            int lane = R() < 0.5 ? 1 : 2, dir = R() < 0.5 ? 1 : -1;
            Ships.Add(new Ship { Id = shipId++, Type = "fish", Lane = lane, Dir = dir, Ph = 0, Done = false, Sos = true, Need = 2.4, Sp = 1.2, X = dir > 0 ? W * 0.28 : W * 0.72 });
            SendToast(Str.Get("world.sos"), Str.Get("world.sosSub")); Say("sos");
        }

        void UpdateRoles(double dt)
        {
            if (Ctx.Tier() < 2) return;
            long now = Ctx.Clock.NowMs;
            if (Wx.Next == 0) ScheduleWx(now);
            if (Wx.Kind == "clear") { if (now >= Wx.Next) StartWx(Wx.NextKind, now); } else if (now >= Wx.Until) EndWx(now);
            double target = Wx.Kind == "clear" ? 0 : Math.Max(0, Math.Min(1, Math.Min((now - Wx.T0) / 6000.0, (Wx.Until - now) / 6000.0)));
            Wx.Amt += (target - Wx.Amt) * Math.Min(1, dt * 2);
            if (Wx.Kind == "fog" && now - sirenAt > 3800)                        // 사이렌: 가까운 배를 부른다
            {
                sirenAt = now; Sirens.Add(new Siren()); Say("horn");
                foreach (var sh in Ships) if (!sh.Done && Math.Abs(sh.X - W / 2.0) < 120) sh.Guide = Math.Min(0.99, sh.Guide + 0.12);
            }
            for (int i = Sirens.Count - 1; i >= 0; i--) { Sirens[i].Age += dt; if (Sirens[i].Age > 2.2) Sirens.RemoveAt(i); }
            if (Ctx.Tier() >= 3 && !Ships.Any(s => s.Sos))
            {
                if (nextRescue == 0) nextRescue = now + (RescueEverySec > 0 ? RescueEverySec * 1000 : (240 + R() * 240) * 1000);
                if (now >= nextRescue) { SpawnSos(); nextRescue = 0; }
            }
        }

        // ================= 환경: 돌고래 (원본 4504~4535행). 표류물(drift)·별똥별은 이번 차수에서 옮기지 않았다 =================
        public const double Jump = 1.35;
        void SpawnDolphins()
        {
            int dir = R() < 0.5 ? 1 : -1;
            double y0 = R() < 0.5 ? 192 + R() * 18 : 278 + R() * 24, x0 = W * (dir > 0 ? 0.08 + R() * 0.2 : 0.72 + R() * 0.2);
            for (int k = 0; k < 2; k++) Dolphins.Add(new Dolphin { X0 = x0 - dir * k * 14, Y0 = y0 + k * 4, Dir = dir, T = -k * 0.35, Dur = 4.6 });
            Ctx.SeeSea("dolphin", true);
        }

        void UpdateAmbient(double dt)
        {
            nextDolphin -= dt;
            if (nextDolphin <= 0) { nextDolphin = 25 + R() * 25; SpawnDolphins(); }
            for (int i = Dolphins.Count - 1; i >= 0; i--)
            {
                var d = Dolphins[i]; double before = d.T - Math.Floor(d.T / Jump) * Jump;
                d.T += dt;
                double after = d.T - Math.Floor(d.T / Jump) * Jump;
                if (d.T > 0 && ((before < 1) != (after < 1))) Rings.Add(new Ring { X = d.X0 + d.Dir * 16 * d.T, Y = d.Y0 + 2, S = 0.55 });     // 뛰어오를 때와 들어갈 때의 물결
                if (d.T > d.Dur) Dolphins.RemoveAt(i);
            }
        }

        /// <summary>시각 tau 의 돌고래 위치: (x, y, 물 위에 있는가).</summary>
        public static (double x, double y, bool up) DolphinAt(Dolphin d, double tau)
        {
            double ph = tau - Math.Floor(tau / Jump) * Jump, x = d.X0 + d.Dir * 16 * tau;
            return ph >= 1 ? (x, d.Y0 + 2, false) : (x, d.Y0 - 60 * ph * (1 - ph), true);
        }

        /// <summary>예보 입력(HUD): 무지개 여부·다음 날씨 종류와 남은 초.</summary>
        public ForecastInput Forecast()
        {
            long now = Ctx.Clock.NowMs;
            return new ForecastInput { Rainbow = !Ctx.Night && now < RainbowUntilMs, NextKind = Wx.Kind == "clear" && Wx.Next != 0 ? Wx.NextKind : null, NextInSec = Wx.Next != 0 ? (Wx.Next - now) / 1000.0 : double.MaxValue, RainWord = RainWord };
        }
    }

}
