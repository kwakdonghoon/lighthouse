#if LIGHTHOUSE_DEV
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Lighthouse.Core;
using Lighthouse.Render;
using Lighthouse.World;
using Newtonsoft.Json.Linq;

namespace Lighthouse.Dev
{
    public sealed class DevCheckResult
    {
        public string Name, Detail; public bool Ok, Skipped;
        public override string ToString() => (Skipped ? "[건너뜀] " : Ok ? "[통과] " : "[실패] ") + Name + " — " + Detail;
    }

    /// <summary>
    /// 에디터 모드의 "자체 점검 14가지"(원본 5934~5951행)를 Unity 로 옮긴 것. 순수 C#이라 dotnet·EditMode·인게임 패널·에디터 메뉴가 같은 목록을 쓴다.
    /// 원본 14개와의 대응:
    ///  1 내용 점검 → ContentLint          2 바다 친구 도감 기록/가져오기 걸러내기 → 저장 코드 왕복(걸러내기 시험은 Tests/Core)
    ///  3 사진 모드 켜기/끄기 → (UI 쪽) PlayMode·UI 시험        4 굽기 6단계×4계절×낮밤 → "섬 굽기"   5 등대 스킨×단계 → "스킨 전부 굽기"(섬 층)
    ///  6 스킨 모든 층 미리 입기 → (Render 골든)               7 배 스프라이트 → (Render 골든)         8 배 추첨 합 100 → "배 추첨 비율"
    ///  9 관리인 6단계×16000걸음 → "관리인 걸음"               10 팝업 가로 넘침·터치 44 → (UI 시험 UiModel/UiUnity)
    ///  11 저장 코드 왕복 → 같음                                12 상점 상품 연결 → "상점 상품 연결"    13 이정표 정렬 → 같음
    ///  14 효과음 29종 → (Audio 시험: B 영역)
    /// "(…)"로 적은 것은 이 목록에 넣지 않고 그 시험으로 대신한다(Skipped 로 표시).
    /// </summary>
    public static class DevChecks
    {
        sealed class Mem : IStorage
        {
            public readonly Dictionary<string, string> F = new Dictionary<string, string>();
            public bool Exists(string n) => F.ContainsKey(n); public string ReadText(string n) => F[n];
            public void WriteTextAtomic(string n, string t) { F[n] = t; } public void Delete(string n) { F.Remove(n); }
        }

        static DevCheckResult R(string name, bool ok, string detail) => new DevCheckResult { Name = name, Ok = ok, Detail = detail };
        static DevCheckResult Skip(string name, string why) => new DevCheckResult { Name = name, Ok = true, Skipped = true, Detail = why };
        static DevCheckResult Try(string name, Func<(bool ok, string detail)> f)
        {
            try { var r = f(); return R(name, r.ok, r.detail); }
            catch (Exception e) { return R(name, false, "예외 " + e.GetType().Name + ": " + e.Message); }
        }

        public static List<DevCheckResult> RunAll(GameTables T, IClock clock, Lighthouse.UI.Model.UiStrings str, Action<string> progress = null)
        {
            var list = new List<DevCheckResult>();
            void Add(DevCheckResult r) { list.Add(r); progress?.Invoke(r.ToString()); }
            var mig = new SaveMigrator(T, clock); var code = new SaveCode(T, mig);

            Add(Try("내용 점검 ContentLint.CheckContent()", () => { var x = new ContentLint(T).CheckContent(); return (x.Count == 0, x.Count == 0 ? "문제 없음" : string.Join(" / ", x.Take(3))); }));

            Add(Try("저장 코드 → 불러오기 왕복", () =>
            {
                var d = mig.Defaults(); string c = code.Export(d); var p = code.Parse(c);
                return (p.Err == null && p.Data != null, p.Err ?? ("코드 " + c.Length + "자"));
            }));

            Add(Try("저장 마이그레이션이 한 번 더 돌려도 같다(멱등)", () =>
            {
                var a = mig.Migrate(mig.Defaults()); var b = mig.Migrate((JObject)a.DeepClone());
                return (JToken.DeepEquals(a, b), JToken.DeepEquals(a, b) ? "같음" : "두 번째가 다름");
            }));

            Add(Try("저장 파일 왕복(메모리): 쓰기 → 읽기 → 같은 상태", () =>
            {
                var io = new Mem(); var store = new SaveStore(io, mig, clock);
                var st = store.Load(); st.Guided = 123; st.Gifts["shell"] = 4; st.Found["shell"] = 1; store.Save(st); st.Guided = 124; store.Save(st);
                var back = new SaveStore(io, mig, clock).Load();
                bool ok = Js.Stringify(back.ToJson()) == Js.Stringify(st.ToJson()) && io.Exists(SaveStore.BackupName);
                return (ok, ok ? "본 + 백업 생김" : "달라짐");
            }));

            Add(Try("저장 복원: 본 파일이 깨지면 백업으로 이어서(알림 RestoredBackup)", () =>
            {
                var io = new Mem(); var store = new SaveStore(io, mig, clock); var st = store.Load(); st.Guided = 7; store.Save(st); st.Guided = 8; store.Save(st);
                io.F[SaveStore.MainName] = "{ 깨진";
                var s2 = new SaveStore(io, mig, clock); var got = s2.Load();
                bool ok = s2.LoadedFrom == "backup" && s2.Notice == LoadNotice.RestoredBackup && (int)got.Guided == 7 && io.F.ContainsKey(SaveStore.CorruptName);
                return (ok, ok ? "백업 7 로 복원, 깨진 본은 save.corrupt.json 에 보관" : s2.LoadedFrom + "/" + s2.Notice + "/" + got.Guided);
            }));

            Add(Try("배 추첨: 단계별 비율 합 100, 표의 배가 모두 있다", () =>
            {
                var bad = new List<string>();
                for (int s = 1; s < T.ShipMix.Count; s++)
                {
                    double sum = T.ShipMix[s].Sum(m => m.W); if (Math.Abs(sum - 100) > 1e-9) bad.Add(s + "단계 합 " + sum);
                }
                return (bad.Count == 0, bad.Count == 0 ? (T.ShipMix.Count - 1) + "단계 모두 100" : string.Join(", ", bad));
            }));

            Add(Try("성장 이정표 정렬 · 스킨 보상 연결", () =>
            {
                int bad = 0;
                foreach (var tr in T.Tracks)
                {
                    var ms = T.Milestones.Where(m => m.K == tr.Id).ToList();
                    for (int i = 0; i < ms.Count; i++)
                    {
                        if (i > 0 && ms[i - 1].Lv > ms[i].Lv) bad++;
                        if (ms[i].SkinLayer != null && !(T.SkinLayerById.TryGetValue(ms[i].SkinLayer, out var lay) && lay.Skins.ContainsKey(ms[i].SkinId ?? ""))) bad++;
                    }
                }
                return (bad == 0, bad + "건 이상");
            }));

            Add(Try("상점: 구매 스킨이 상품에 모두 연결", () =>
            {
                var miss = new List<string>();
                foreach (var lay in T.SkinLayers) foreach (var kv in lay.Skins)
                        if (kv.Value.Src == "iap" && !T.Products.Any(p => p.Grants != null && p.Grants.Contains(lay.Id + ":" + kv.Key))) miss.Add(lay.Id + ":" + kv.Key);
                return (miss.Count == 0, miss.Count == 0 ? "모두 연결" : string.Join(", ", miss));
            }));

            Add(Try("섬 굽기: 6단계 × 겨울/아닌 × 정원 레벨 0·5·10 (오류 없이, 시간)", () =>
            {
                var sw = Stopwatch.StartNew(); int n = 0; double worst = 0;
                for (int st = 1; st <= T.MaxStage; st++) foreach (bool winter in new[] { false, true }) foreach (int gl in new[] { 0, 5, 10 })
                        { var t0 = sw.Elapsed.TotalMilliseconds; new IslandBaker(new IslandEnv { Stage = st, Winter = winter, GardenLv = gl }).Bake(); worst = Math.Max(worst, sw.Elapsed.TotalMilliseconds - t0); n++; }
                return (true, n + "조합, 최대 " + worst.ToString("F0") + " ms");
            }));

            Add(Try("관리인: 6단계 × 16000걸음 (섬 밖으로 나가지 않는다)", () =>
            {
                int bad = 0;
                for (int stage = 1; stage <= T.MaxStage; stage++)
                {
                    var ck = new ManualClock(clock.NowMs, clock.OffsetMinutes(clock.NowMs));
                    var st = GameState.FromJson(new SaveMigrator(T, ck).Defaults()); st.Seen = true; st.Stage = stage; st.StartAt = ck.Now;
                    var ctx = new GameContext(T, st, new Mulberry32(stage), ck) { Stage = stage };
                    var loop = new GameLoop(ctx, str, new Mulberry32(stage + 1000)); loop.SetView(216, 468, 468 - 320);
                    loop.NightForced = true; loop.Dusk = 0;
                    for (int k = 0; k < 16000; k++) { ck.AdvanceSec(0.1); loop.Tick(0.1); if (Terrain.TerrH(stage, loop.Keeper.U, loop.Keeper.V) < 0) bad++; }
                }
                return (bad == 0, bad + "걸음 섬 밖");
            }));

            Add(Skip("사진 모드 켜기/끄기", "UI 쪽: Tests/UI·PlayModeApp"));
            Add(Skip("스킨 모든 층 미리 입기 · 배 스프라이트 굽기", "Render 골든 시험(Tests/EditMode GoldenTests, Tests/World FxGolden)"));
            Add(Skip("팝업: 가로 넘침 0 · 터치 44px 미만 단추 0", "UI 레이아웃 시험(Tests/UI UiModelTests, Tests/UI/Unity)"));
            Add(Skip("효과음 29종 렌더", "Audio(B) 시험"));
            return list;
        }

        /// <summary>시뮬레이터(원본 simulate) 결과를 사람이 읽을 표로. 같은 seed 면 같은 결과.</summary>
        public static string SimulateText(GameTables T, IClock clock, double shipsPerDay, int days, long seed, int visitEvery)
        {
            var sw = Stopwatch.StartNew();
            var r = Simulator.Run(T, clock, shipsPerDay, days, seed, visitEvery);
            var j = r.ToJson();
            return "simulate(배/일 " + shipsPerDay + ", " + days + "일, seed " + seed + ", 방문 " + visitEvery + "일마다) " + sw.ElapsedMilliseconds + " ms\n"
                 + "최종 단계 " + r.FinalStage + ", 안내 " + r.FinalGuided + "척, 도감 " + r.FinalDiscovered + ", 편지 " + r.FinalLetters + ", 난수 호출 " + r.RngCalls + "\n"
                 + "단계 도달일: " + string.Join(", ", r.Stage.OrderBy(k => k.Key).Select(k => k.Key + "단계 " + k.Value + "일")) + "\n"
                 + "최종 레벨: " + string.Join(", ", r.FinalLv.Select(k => k.Key + " " + k.Value)) + "\n"
                 + j.ToString(Newtonsoft.Json.Formatting.None);
        }
    }
}
#endif
