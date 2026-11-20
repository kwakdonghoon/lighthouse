using System;
using System.Collections.Generic;
using System.Linq;
using Lighthouse.Core;

namespace Lighthouse.World
{
    /// <summary>배: 추첨·항로·이동·암초 비켜 가기·정박·안내 쌓기·도착 보상 (원본 spawnShip 1979행, updateShips 1992행).</summary>
    public sealed partial class GameLoop
    {
        string PickShipType() => Ctx.ShipTypeAt(Ctx.Rng.NextDouble());        // 규칙 난수

        int BigCount() => Ships.Count(q => Lanes.IsBig(q.Type));

        public Ship SpawnShip(bool mid)
        {
            string type = PickShipType();
            for (int i = 0; i < 6 && Lanes.IsBig(type) && BigCount() >= 2; i++) type = PickShipType();      // 큰 배는 동시에 두 척까지만
            if (Lanes.IsBig(type) && BigCount() >= 2) type = "trade";
            bool big = Lanes.IsBig(type);
            int lane = big ? new[] { 0, 0, 1, 1 }[(int)(R() * 4)] : new[] { 0, 0, 1, 1, 2 }[(int)(R() * 5)];
            int dir = R() < 0.5 ? 1 : -1;
            var sh = new Ship
            {
                Id = shipId++, Lane = lane, Dir = dir, Ph = R() * 6.28, Type = type,
                WillRest = Ctx.Tier() >= 3 && lane == 2 && R() < 0.4,
                Sp = new[] { 3, 5, 7 }[lane] * (0.8 + R() * 0.4) * (big ? 0.7 : 1),
                X = mid ? 20 + R() * (W - 40) : (dir > 0 ? -45 : W + 45)
            };
            Ships.Add(sh);
            var reg = Ctx.PickRegular();                                    // 가끔 이름과 사연이 있는 단골 손님이 온다 (규칙 난수)
            if (reg != null) { sh.Reg = reg.Id; sh.Type = reg.Type; sh.Sp *= reg.Sp; }
            else if (!mid)                                                  // 드문 손님: 조건(밤·비·안개·겨울 밤 …)이 맞을 때 아주 가끔
            {
                var rare = Ctx.PickRareGuest(rareRnd, id => Ships.Any(s => s.Rare == id));
                if (rare != null) { sh.Rare = rare.Id; sh.Type = rare.Type; }
            }
            return sh;
        }

        void UpdateShips(double dt, double th0)
        {
            spawnIn -= dt;
            double load = Ships.Sum(q => Lanes.IsBig(q.Type) ? 0.5 : 1);
            int port = Ctx.EffLv("port");
            if (spawnIn <= 0 && load < 5 + (port >= 2 ? 1 : 0) + (port >= 5 ? 1 : 0))
            {
                SpawnShip(false);
                spawnIn = (Ctx.Tier() == 1 ? 6 + R() * 6 : 9 + R() * 9) * (1 - 0.07 * port);     // 항구가 자랄수록 배가 자주 들른다
            }
            bool night = Ctx.Night;
            for (int i = Ships.Count - 1; i >= 0; i--)
            {
                var sh = Ships[i]; double ly = Lanes.Y[sh.Lane], ls = Lanes.S[sh.Lane];
                sh.X += sh.Dir * sh.Sp * dt;
                if (sh.Wave > 0) sh.Wave -= dt;
                if (sh.Done) sh.DoneAge += dt;
                if (sh.DodgeT > 0) { sh.DodgeT -= dt; sh.Yo = sh.DodgeT > 0 ? sh.DodgeDir * 7 * Math.Sin(Math.PI * (1 - sh.DodgeT / 2)) : 0; }
                if (Ctx.Tier() >= 2 && !sh.Reefed)
                    foreach (var reef in Lanes.Reefs)                       // 암초 경고: 붉은 불빛에 배가 암초를 비켜 간다
                    {
                        double rx = W * reef[0], ry = reef[1];
                        if (Math.Abs(sh.X - rx) < 18 && Math.Abs(ly - ry) < 24)
                        {
                            sh.Reefed = true; sh.DodgeT = 2; sh.DodgeDir = ly <= ry ? -1 : 1; WarnUntil = Perf + 1700; WarnX = rx; WarnY = ry; Say("warn");
                            Bump("reef");
                            if (Perf > reefGiftAt)                         // 암초 선물은 45초에 한 번만 (경고 연출은 매번)
                            {
                                reefGiftAt = Perf + 45000;
                                string g = Ctx.RollGift(sh.Type); Ctx.AddGift(g); Ctx.NotifyChanged();
                                Pops.Add(new Pop { X = sh.X, Y = ly - 24 * ls, Gift = g });
                            }
                        }
                    }
                if (sh.WillRest && !sh.Rested && Math.Abs(sh.X - W / 2.0) < 4) { sh.Rested = true; sh.RestT = 9; sh.Sp0 = sh.Sp; sh.Sp = 0; }      // 항구: 섬 앞에 잠시 정박한다
                if (sh.RestT > 0)
                {
                    sh.RestT -= dt;
                    if (sh.RestT <= 0) { sh.Sp = sh.Sp0; string g = Ctx.RollGift("trade"); Ctx.AddGift(g); Bump("dock"); Ctx.NotifyChanged(); Pops.Add(new Pop { X = sh.X, Y = ly - 24 * ls, Gift = g }); }
                }
                if (sh.X < -70 || sh.X > W + 70) { Ships.RemoveAt(i); if (Bubble != null && Bubble.Ship == sh) { Bubble = null; BubbleChanged?.Invoke(null); } continue; }
                if (sh.Done) continue;
                // 밤에는 빛줄기가 닿는 만큼, 낮에는 등대 가까이 있는 동안 안내가 쌓인다
                double hit = BeamHits(th0, sh.X, ly);
                double rate = ((night ? hit * 0.4 : (Math.Abs(sh.X - W / 2.0) < 90 ? 0.09 : 0)) + (Aiming ? hit * 0.9 : 0)) * Ctx.LensMul() * (Ctx.Wx == "fog" ? 1 - 0.5 * Wx.Amt : 1);
                if (Aiming && hit > 0.3 && R() < dt * 8) Fx.Add(new Twinkle { X = sh.X + (R() - 0.5) * 10, Y = ly - 14 * ls, Vx = (R() - 0.5) * 14, Vy = -6 - R() * 8, Life = 0.8 });
                sh.Guide += rate * dt;
                if (sh.Guide >= (sh.Need != 0 ? sh.Need : 1)) CompleteShip(sh, ly, ls);
            }
        }

        /// <summary>안내 완료: 선물과 (가끔) 편지를 우체통에 남기고 간다 (원본 2024~2041행).</summary>
        void CompleteShip(Ship sh, double ly, double ls)
        {
            sh.Done = true;
            var rw = Ctx.RewardShip(sh.Type, false, false);
            Pops.Add(new Pop { X = sh.X, Y = ly - 24 * ls, Gift = rw.Gift });
            if (rw.Letter != null) MailAlertUntil = Perf + 3200;
            Say(rw.Letter != null ? "mail" : "ding");
            if (sh.Reg != null) VisitRegularAnnounce(sh);
            if (sh.Rare != null) RareAnnounce(sh, ly, ls);
            if (Ctx.Wx == "fog") Wx.Count++;
            if (sh.Sos)                                                     // 구조 성공: 선물이 더 도착한다
            {
                sh.Sos = false; sh.Sp = 6; Bump("rescued");
                for (int k = 0; k < 2; k++) { string g = Ctx.RollGift("trade"); Ctx.AddGift(g); }
                Ctx.NotifyChanged(); Say("level"); SendToast(Str.Get("world.rescued"), Str.Get("world.rescuedSub"));
            }
            for (int k = 0; k < 14; k++) Fx.Add(new Twinkle { X = sh.X, Y = ly - 12 * ls, Vx = (R() - 0.5) * 22, Vy = -8 - R() * 16, Life = 1 + R() * 0.8 });
            Rings.Add(new Ring { X = sh.X, Y = ly + 1, S = ls });
        }

        /// <summary>단골 방문 기록과 이야기 편지 알림 (원본 visitRegular 4973행: 편지가 있으면 우체통 알림·소리·토스트).</summary>
        void VisitRegularAnnounce(Ship sh)
        {
            var r = Ctx.RegById(sh.Reg); if (r == null) return;
            var l = Ctx.VisitRegular(r.Id, false);
            double v = Ctx.RegVisits(r.Id);
            if (l != null)
            {
                MailAlertUntil = Perf + 4000; Say("mail");
                var vb = Ctx.T.Bal.VisitAt;
                if (v > vb[vb.Length - 1]) SendToast(Str.F("world.regGreet", r.Cap), Str.F("world.regVisit", r.Ship, v));      // 이야기를 다 들은 단골의 안부 편지
                else SendToast(Str.F("world.regLetter", r.Cap), v == Ctx.T.Bal.GiftAt ? Str.Get("world.regSpecial") : Str.F("world.regVisit", r.Ship, v));
            }
            Ctx.NotifyChanged();
        }

        /// <summary>드문 손님 안내 완료: 첫 만남은 편지와 빛나는 선물, 다시 만나면 선물 하나.</summary>
        void RareAnnounce(Ship sh, double ly, double ls)
        {
            if (!Ctx.T.RareById.TryGetValue(sh.Rare, out var d)) return;
            bool first = !Ctx.RareMet(d.Id);
            var l = Ctx.MeetRare(d.Id, out string g);
            if (g != null) Pops.Add(new Pop { X = sh.X, Y = ly - 30 * ls, Gift = g });
            if (l != null) MailAlertUntil = Perf + 4000;
            Say(first ? "level" : "ding");
            SendToast(Str.Get(first ? "world.rareFirst" : "world.rareAgain"), Str.F(first ? "world.rareFirstSub" : "world.rareAgainSub", d.Ship));
            Ctx.NotifyChanged();
        }

        // ================= 탭 (원본 tapAt 5123행) =================
        static string PickOf(string[] a, double r) => a[Math.Min(a.Length - 1, (int)(r * a.Length))];
        string[] Lines(string key) => Str.Get(key).Split('|');

        void ShowBubble(string text, Ship ship, bool keeper)
        {
            Bubble = new BubbleInfo { Text = text, Ship = ship, Keeper = keeper, Until = Perf + 2600 };
            BubbleChanged?.Invoke(Bubble);
        }

        /// <summary>설계 좌표 (x, y) 를 탭했다. 배 → 관리인 → 우체통 → 등대 → 바다 순으로 판정한다.</summary>
        public void TapAt(double x, double y)
        {
            double cx = W / 2.0;
            if (TryWish(x, y) || TryPickDrift(x, y)) return;                      // 흐르는 별똥별 → 떠다니는 표류물 (원본 tapAt 5125행)
            foreach (var sh in Ships.OrderByDescending(s => Lanes.Y[s.Lane]))      // 앞쪽 배부터 판정
            {
                double ly = Lanes.Y[sh.Lane], ls = Lanes.S[sh.Lane];
                if (Math.Abs(x - sh.X) < Lanes.HalfLen(sh.Type) * ls + 5 && y > ly - 38 * ls && y < ly + 8 * ls)
                {
                    sh.Wave = 2.4; Say("hello"); if (!sh.Done) sh.Guide = Math.Min(0.98, sh.Guide + 0.2);
                    for (int k = 0; k < 6; k++) Fx.Add(new Twinkle { X = sh.X, Y = ly - 16 * ls, Vx = (R() - 0.5) * 18, Vy = -6 - R() * 10, Life = 0.9 });
                    var reg = sh.Reg != null ? Ctx.RegById(sh.Reg) : null;
                    var say = sh.Done ? Lines("world.sayDone") : Ctx.Night ? Lines("world.sayNight") : Lines("world.sayDay");
                    var rare = sh.Rare != null && Ctx.T.RareById.TryGetValue(sh.Rare, out var rd) && rd.Lines.Length > 0 ? rd : null;
                    ShowBubble(reg != null && reg.Lines.Length > 0 ? reg.Ship + Str.Get("world.shipSuffix") + " · " + PickOf(reg.Lines, R()) : rare != null ? rare.Ship + Str.Get("world.shipSuffix") + " · " + PickOf(rare.Lines, R()) : PickOf(say, R()), sh, false);
                    return;
                }
            }
            if (Ctx.Tier() >= 3)                                                  // 관리인을 누르면 한마디 한다
            {
                var kp = KeeperPos();
                if (Hyp(x - kp.x, y - (kp.y - 5)) < 8)
                {
                    string act = Keeper.WaveT > 0 ? "wave" : Keeper.Walking ? null : Keeper.Act;
                    string[] pool = act != null && Str.Has("world.act." + act) && R() < 0.7 ? Lines("world.act." + act) : Lines("world.keeperSay");
                    ShowBubble(PickOf(pool, R()), null, true);
                    return;
                }
            }
            var mp = MailboxPos();
            if (Hyp(x - mp.x, y - (mp.y - 7)) < 10) { OpenMail?.Invoke(); return; }       // 우체통을 누르면 편지함이 열린다
            int stage = Ctx.Stage; var P = Lighthouse.Render.TowerParams.ForStage(stage);
            double top = Lighthouse.Render.Terrain.CY - (P.Zt * Lighthouse.Render.Terrain.VZ + 6 + 6);
            if (Math.Abs(x - cx) < 14 + 5 && y > top && y < Lighthouse.Render.Terrain.CY + 4)      // 등대: 램프가 번쩍하고 빛이 한 바퀴 빠르게 돌고 갈매기가 날아오른다 (탑 폭 14·높이는 근사: 원본 P.gr/h/lh/rh 미이식)
            {
                Flash = 1; Spin = 1; Say("bell");
                if (Ctx.Tier() >= 2) { SignalUntil = Perf + 2600; foreach (var s2 in Ships) s2.Wave = 2 + R(); }      // 신호: 지나는 배들이 인사를 보내온다
                Ctx.SeeSea("gull", true);
                double ly = LampY(stage);
                for (int k = 0; k < 4; k++) Birds.Add(new Bird { X = cx + (R() - 0.5) * 12, Y = ly + 10, Vx = (R() < 0.5 ? -1 : 1) * (14 + R() * 18), Vy = -(10 + R() * 12), Life = 3.6, Ph = R() * 6 });
                return;
            }
            if (y > Lighthouse.Render.BgBaker.Horizon + 2) Rings.Add(new Ring { X = x, Y = y, S = 0.8 });      // 바다를 누르면 물결
        }

        public (int x, int y) MailboxPos()
        {
            var (u, v) = Lighthouse.Render.Terrain.Blk2Uv(3.5, 1.5);
            return ((int)JsMath.Round(W / 2.0 + u), (int)JsMath.Round(Lighthouse.Render.Terrain.CY + Lighthouse.Render.Terrain.IsleY + v * Lighthouse.Render.Terrain.KY - Lighthouse.Render.Terrain.TerrH(Ctx.Stage, u, v)));
        }
    }
}
