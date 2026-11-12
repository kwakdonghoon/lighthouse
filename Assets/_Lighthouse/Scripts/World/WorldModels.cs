using System;
using System.Collections.Generic;
using Lighthouse.Core;

namespace Lighthouse.World
{
    /// <summary>바다 위의 배 한 척 (원본 ships[] 항목). 좌표는 설계 좌표(논리 폭 W, 항로 y 는 SHIP_LANES).</summary>
    public sealed class Ship
    {
        public int Id, Lane, Dir;
        public string Type, Reg;
        public double Ph, Guide, Need = 1, X, Sp, Sp0, Wave, DoneAge, DodgeT, DodgeDir, Yo, RestT, Lit;
        public bool Done, WillRest, Rested, Reefed, Sos;
    }

    public sealed class Ring { public double X, Y, S, Age; }
    public sealed class Pop { public double X, Y, Age; public string Gift; }
    public sealed class Spark { public double X, Y, Vx, Vy, Life, Delay; public int Col; }
    public sealed class Twinkle { public double X, Y, Vx, Vy, Life; }
    public sealed class Bird { public double X, Y, Vx, Vy, Life, Ph; }
    public sealed class Dolphin { public double X0, Y0, T, Dur = 4.6; public int Dir; }
    public sealed class Siren { public double Age; }
    /// <summary>꾸미기 놓기가 열려 있을 때 지금 구역의 열린 자리 표시(원본 drawDecorMarkers). Rx: 크기별 반지름(S 6·M 9·L 12).</summary>
    public sealed class DecoMarker { public double X, Y, Rx; public bool Sel, Full; public int Num; }

    // ----- A1: 월드 연출 상태 (원본 motes·lanterns·meteors·drift·gulls·crumbs) -----
    public sealed class Mote { public double X, Y, Vx, Vy, Ph; public string S; }
    public sealed class Lantern { public double X, Y, Ph; }
    public sealed class Meteor { public double X, Y, Vx, Vy, Age, Life = 1.8; public bool Wished; }
    public sealed class DriftItem { public double X, Y, Vx, Ph, Take = -1; public string Gift; public bool Taking => Take >= 0; }
    public sealed class Gull { public double X, Y, Tx, Ty, Ph, Peck, Pk, Delay, Vx, Vy, Life; public int Face; public string St = "in"; }
    public sealed class Crumb { public double X, Y, Vx, Vy, Gy, Life; public bool Land; }

    /// <summary>말풍선 한 개: 대상(배 또는 관리인)을 따라간다. 위치는 화면 쪽이 Anchor 로 묻는다.</summary>
    public sealed class BubbleInfo { public string Text; public Ship Ship; public bool Keeper; public double Until; }

    /// <summary>관리인 상태 (원본 keeper). u·v 는 섬 평면 좌표.</summary>
    public sealed class KeeperState
    {
        public double StepD, WorkT, U = -21, V = 17, Wait = 3, ActT, WaveT, NextCast = 3, NextToss, StarT;
        public int Dsign = 1, I, Ni, Dir = -1;
        public bool LampPending, LampVia, Walking, WasNight, SawMeteor;
        public string Act = "sweep";
    }

    /// <summary>날씨 (원본 wx): 맑음 / 안개 / 비(겨울엔 눈).</summary>
    public sealed class Weather
    {
        public string Kind = "clear", NextKind = "fog";
        public double T0, Until, Next, Amt, Count;
    }

    public static class Lanes
    {
        public static readonly double[] Y = { 176, 282, 308 };
        public static readonly double[] S = { 0.55, 0.85, 1.15 };
        public static readonly double[][] Reefs = { new[] { 0.2, 280 }, new[] { 0.8, 290 } };      // [화면 폭 비율, y]
        /// <summary>SHIPS[type].L 배 반 길이.</summary>
        public static double HalfLen(string type)
        {
            switch (type)
            {
                case "fish": return 15; case "trade": return 22; case "ferry": return 21; case "explorer": return 24; case "rowboat": return 11;
                case "schooner": return 18; case "steamer": return 23; case "galleon": return 30; case "cruise": return 33; default: return 15;
            }
        }
        public static bool IsBig(string type) => type == "galleon" || type == "cruise";
        /// <summary>SHIPS[type].top 갑판 높이(선원 위치).</summary>
        public static double Top(string type)
        {
            switch (type)
            {
                case "fish": return 5; case "trade": return 6; case "ferry": return 7; case "explorer": return 8; case "rowboat": return 4;
                case "schooner": return 7; case "steamer": return 8; case "galleon": return 10; case "cruise": return 9; default: return 5;
            }
        }
    }

    /// <summary>시험용 손으로 돌리는 시계: 가속 시험에서 시간을 일 단위로 건너뛴다.</summary>
    public sealed class ManualClock : IClock
    {
        public long Now; readonly int off;
        public ManualClock(long nowMs, int offsetMinutes) { Now = nowMs; off = offsetMinutes; }
        public long NowMs => Now;
        public int OffsetMinutes(long utcMs) => off;
        public void AdvanceSec(double sec) { Now += (long)(sec * 1000); }
    }
}
