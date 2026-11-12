using System;
using System.Collections.Generic;
using System.Linq;
using Lighthouse.Core;
using Lighthouse.UI.Model;
using Lighthouse.World;
using NUnit.Framework;

namespace Lighthouse.Tests
{
    /// <summary>U1: 꾸미기 자리 번호 배지가 10번 이상에서도 두 자리로 그려지는지(원본은 0~9 표뿐이라 전부 0 모양). 10 미만은 원본과 같은 자리·모양. 순수 C#(dotnet·EditMode 공용).</summary>
    public sealed class U1BadgeTests
    {
        const int W = 216, H = 468, OY = 148; const double X = 100, Y = 100;
        static GameTables T => M1Fixture.Tables;

        // 한 자리 숫자 표의 독립 사본(시험 기준): WorldPainter.DecoDigits 와 따로 적었다
        static readonly string[][] Dg =
        {
            new[] { "###", "#.#", "#.#", "#.#", "###" }, new[] { ".#.", "##.", ".#.", ".#.", "###" }, new[] { "##.", "..#", ".#.", "#..", "###" }, new[] { "##.", "..#", ".#.", "..#", "##." }, new[] { "#.#", "#.#", "###", "..#", "..#" },
            new[] { "###", "#..", "##.", "..#", "##." }, new[] { ".##", "#..", "###", "#.#", "###" }, new[] { "###", "..#", ".#.", ".#.", ".#." }, new[] { "###", "#.#", "###", "#.#", "###" }, new[] { "###", "#.#", "###", "..#", "##." }
        };

        /// <summary>자리 번호 num 배지를 실제로 그려 (바탕 상자 격자, 글자 격자)를 읽는다. 상자: 'o', 글자: '#', 없음: '.'</summary>
        static string[] Render(int num, bool sel, out int x0, out int y0)
        {
            var clock = new ManualClock(M1Fixture.FixMs, M1Fixture.TzMinutes);
            var st = GameState.FromJson(new SaveMigrator(T, clock).Defaults());
            var ctx = new GameContext(T, st, new Mulberry32(1), clock) { Stage = 1 };
            var loop = new GameLoop(ctx, UiStrings.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(M1Fixture.DataDir, "ko", "ui.json"), new System.Text.UTF8Encoding(false))), new Mulberry32(2));
            loop.Markers.Add(new DecoMarker { X = X, Y = Y, Rx = 9, Sel = sel, Full = true, Num = num });
            var p = new WorldPainter(W, H, OY); p.PaintDecoMarkers(loop, 2.6);
            uint[] px = p.Ov.P; int minx = W, miny = H, maxx = -1, maxy = -1;
            for (int j = 0; j < H; j++) for (int i = 0; i < W; i++) if ((px[j * W + i] >> 24) != 0) { minx = Math.Min(minx, i); maxx = Math.Max(maxx, i); miny = Math.Min(miny, j); maxy = Math.Max(maxy, j); }
            x0 = minx; y0 = miny - OY; var rows = new string[maxy - miny + 1];
            for (int j = miny; j <= maxy; j++)
            {
                var sb = new System.Text.StringBuilder();
                for (int i = minx; i <= maxx; i++)
                {
                    uint v = px[j * W + i]; if ((v >> 24) == 0) { sb.Append('.'); continue; }
                    sb.Append((v & 255) > 200 ? '#' : 'o');      // 글자 흰색/노랑(R>200), 바탕 남색·갈색 테두리(R<130)
                }
                rows[j - miny] = sb.ToString();
            }
            return rows;
        }

        static string[] Expected(int num)
        {
            string s = num.ToString(); int n = s.Length, w = n * 4 - 1; var rows = new List<string>();
            rows.Add(new string('o', w + 2));
            for (int j = 0; j < 5; j++)
            {
                var sb = new System.Text.StringBuilder("o");
                for (int k = 0; k < n; k++) { if (k > 0) sb.Append('o'); sb.Append(Dg[s[k] - '0'][j].Replace('.', 'o')); }
                sb.Append('o'); rows.Add(sb.ToString());
            }
            rows.Add(new string('o', w + 2));
            return rows.ToArray();
        }

        [Test]
        public void SingleDigitBadgesKeepOriginalPositionAndShape()
        {
            for (int n = 0; n <= 9; n++)
            {
                var got = Render(n, false, out int x0, out int y0);
                CollectionAssert.AreEqual(Expected(n), got, "한 자리 " + n);
                Assert.AreEqual((int)X - 4, x0, "상자 왼쪽 끝(원본 bx-1 = X-4) " + n); Assert.AreEqual((int)Y + 2, y0, "상자 위쪽(원본 by-1 = Y+2) " + n);
            }
        }

        [Test]
        public void TwoDigitBadgesDrawBothDigitsAndStayCentred()
        {
            var seen = new HashSet<string>();
            for (int n = 0; n <= 99; n++)
            {
                var got = Render(n, false, out int x0, out int y0);
                CollectionAssert.AreEqual(Expected(n), got, "배지 " + n);
                if (n >= 10) { Assert.AreEqual((int)X - 6, x0, "두 자리는 중심을 한 자리와 같게(상자 X-6~X+2) " + n); Assert.AreEqual(9, got[0].Length); }
                Assert.IsTrue(seen.Add(string.Join("/", got)), "번호마다 모양이 달라야 한다 " + n);
            }
        }

        [Test]
        public void AllSlotNumbersOfTheTablesRenderTheirOwnDigits()
        {
            Assert.AreEqual(T.DecorSlots.Count, T.DecorSlots.Select(s => s.Id).Distinct().Count());
            foreach (var sl in T.DecorSlots)
            {
                int num = WorldPainter.SlotNum(sl.Id); Assert.AreEqual(sl.Num, num, sl.Id);
                CollectionAssert.AreEqual(Expected(num), Render(num, false, out _, out _), "자리 " + sl.Id);
            }
        }

        [Test]
        public void SlotNumParsesAllDigitsAndBadgeDotsMatchRenderer()
        {
            Assert.AreEqual(12, WorldPainter.SlotNum("P12")); Assert.AreEqual(5, WorldPainter.SlotNum("SH5")); Assert.AreEqual(0, WorldPainter.SlotNum(null));
            Assert.AreEqual(5, WorldPainter.BadgeDots(10).Length);
            Assert.AreEqual(".#.", WorldPainter.BadgeDots(10)[0].Substring(0, 3)); Assert.AreEqual("###", WorldPainter.BadgeDots(10)[0].Substring(4));
            Assert.AreEqual("##..###", WorldPainter.BadgeDots(28)[0]); Assert.AreEqual(3, WorldPainter.BadgeDots(8)[0].Length);
        }
    }
}
