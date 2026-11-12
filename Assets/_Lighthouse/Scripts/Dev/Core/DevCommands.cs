#if LIGHTHOUSE_DEV
using System;
using System.Linq;
using Lighthouse.Core;
using Lighthouse.World;

namespace Lighthouse.Dev
{
    /// <summary>
    /// 개발용 조작(원본 ?dev 주소 매개변수와 개발 막대·에디터 모드의 단추들, HTML 5555~5576행·5771~5964행). 순수 C#(GameContext·GameLoop 만 건드린다)이라 dotnet 으로 시험한다.
    /// 인게임 패널(DevPanel)과 에디터 메뉴가 같은 함수를 부른다. 이 어셈블리는 LIGHTHOUSE_DEV 가 없으면 컴파일에서 통째로 빠진다.
    ///  ?s=      → JumpStage        ?n= ?dusk= → SetNight / SetDusk   ?season= → SetSeason   ?wx= → StartWeather
    ///  ?gift=   → GiveGifts        ?lv=       → SetLevel             ?guided=  → SetGuided   ?startdays= → SetStartDays
    ///  ?away=   → Away             ?reset     → FreshState           (배속·시간 건너뛰기는 원본에 ?fast/?cycle 정도뿐이라 SkipTime 과 DevTimeMul 을 새로 둠)
    /// </summary>
    public static class DevCommands
    {
        /// <summary>?s= : 화면의 단계만 바꾼다. persist 가 true 면 저장의 단계도 바꾸고 진행 중 공사를 지운다(저장을 바꾸는 단추).</summary>
        public static void JumpStage(GameContext ctx, int stage, bool persist)
        {
            stage = Math.Max(1, Math.Min(ctx.T.MaxStage, stage));
            ctx.Stage = stage; ctx.NeedRebuild = true;
            if (persist) { ctx.S.Stage = stage; ctx.S.Build = null; ctx.NotifyChanged(); }
        }

        /// <summary>?n=0|1 : 밤낮 고정(null 이면 시계를 따르게 되돌린다).</summary>
        public static void SetNight(GameLoop loop, bool? night)
        {
            if (night == null) { loop.NightForced = false; return; }
            loop.NightForced = true; loop.Dusk = night.Value ? 1 : 0; loop.Ctx.Night = night.Value;
        }

        /// <summary>?dusk=0..1 : 해질녘 정도를 고정.</summary>
        public static void SetDusk(GameLoop loop, double dusk)
        {
            dusk = Math.Max(0, Math.Min(1, dusk));
            loop.NightForced = true; loop.Dusk = dusk; loop.Ctx.Night = dusk >= 0.5;
        }

        /// <summary>?season= : spring|summer|autumn|winter, null 이면 자동(저장하지 않는다).</summary>
        public static void SetSeason(GameContext ctx, string season)
        {
            ctx.SeasonForce = season != null && GameTables.SeasonIds.Contains(season) ? season : null; ctx.NeedRebuild = true;
        }

        /// <summary>?fast : 날씨·구조 요청 주기를 1/6 로(원본 4069행 slow). 공사 시간 단축(원본 3183행의 8배)은 배속(DevTimeMul)으로 대신한다.</summary>
        public static void SetFast(GameLoop loop, bool on) { loop.WxSlow = on ? 6 : 1; }

        /// <summary>?wx=fog|rain : 날씨를 곧 시작(0.8초 뒤, 원본 5565행). 이미 날씨가 있으면 끝난 뒤.</summary>
        public static void StartWeather(GameLoop loop, string kind)
        {
            loop.Wx.Next = loop.Ctx.Clock.NowMs + 800; loop.Wx.NextKind = kind == "rain" ? "rain" : "fog";
        }

        /// <summary>?gift=N : 모든 선물을 N개씩(원본 5561행: gifts·totals 에 더하고 found=1).</summary>
        public static void GiveGifts(GameContext ctx, int n)
        {
            n = n <= 0 ? 10 : n;
            foreach (var g in ctx.T.Gifts)
            {
                ctx.S.Gifts[g.Id] = GameContext.Get(ctx.S.Gifts, g.Id) + n;
                ctx.S.Totals[g.Id] = GameContext.Get(ctx.S.Totals, g.Id) + n;
                ctx.S.Found[g.Id] = 1;
            }
            ctx.NotifyChanged();
        }

        /// <summary>?lv=N : 세 갈래를 레벨 N 으로(0 이면 0). 원본 5562행 lvAt(v).</summary>
        public static void SetLevel(GameContext ctx, int lv)
        {
            foreach (var tr in ctx.T.Tracks) ctx.S.Growth[tr.Id] = lv > 0 ? Progress.LvAt(ctx.T, lv) : 0;
            ctx.NotifyChanged();
        }

        /// <summary>?guided=N : 안내한 배 수.</summary>
        public static void SetGuided(GameContext ctx, double n) { ctx.S.Guided = Math.Max(0, n); ctx.NotifyChanged(); }

        /// <summary>?startdays=N : 시작한 지 N일 지난 것으로(기념일 편지 확인, 원본 5560행).</summary>
        public static void SetStartDays(GameContext ctx, double days) { ctx.S.StartAt = ctx.Clock.NowMs - (long)(Math.Max(0, days) * 86400000.0); ctx.NotifyChanged(); }

        /// <summary>?away=초 : 자리를 비운 시간만큼의 보상을 지금 받는다(원본 5576~5577행). 90초 미만이면 null.</summary>
        public static OfflineResult Away(GameContext ctx, GameLoop loop, double sec)
        {
            ctx.S.LastSeen = ctx.Clock.NowMs - (long)(sec * 1000);
            var news = loop.ReturnFromAway(sec);
            ctx.S.LastSeen = ctx.Clock.NowMs; ctx.NotifyChanged();
            return news;
        }

        /// <summary>시계를 sec 만큼 건너뛰고(DevClock) 그 시간 동안 접속하지 않은 것처럼 보상을 정산한다.</summary>
        public static OfflineResult SkipTime(DevClock clock, GameContext ctx, GameLoop loop, double sec)
        {
            clock.AddExtraSec(sec);
            var news = loop.ReturnFromAway(sec);
            ctx.S.LastSeen = clock.NowMs; ctx.NotifyChanged();
            return news;
        }

        /// <summary>?reset : 새 저장(구매·광고 기록도 비운다 — 원본 5558행). nowMs 가 startAt.</summary>
        public static GameState FreshState(SaveMigrator mig, long nowMs)
        {
            var st = GameState.FromJson(mig.Defaults()); st.StartAt = nowMs; return st;
        }
    }
}
#endif
