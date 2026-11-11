using System;
using Lighthouse.Core;

namespace Lighthouse.World
{
    /// <summary>
    /// 자동 저장 (원본 persist() + lastSeen 갱신, 2994~3001행·3069행). 순수 C#: 저장 파일은 IStorage(Core)가 임시 파일→교체로 쓰고(save.json + save.bak.json 한 벌),
    /// 이 클래스는 "언제 쓰는가"만 정한다.
    ///  - 상태가 바뀌면(GameContext.StateChanged) 표시만 해 두고, 마지막 쓰기로부터 MinGapSec 가 지난 틱에 쓴다(배 한 척마다 디스크를 두드리지 않게).
    ///  - 15초마다 lastSeen 을 갱신하며 쓴다(원본 setInterval 15000).
    ///  - Flush() 는 앱이 가려질 때·끝날 때(OnApplicationPause/Quit)와 종료 직전에 부른다: lastSeen=지금.
    /// 쓰기 실패(디스크 가득 등)는 삼키고 SaveFailed 로 알린다 — 이전 본(save.bak.json)이 남는다.
    /// </summary>
    public sealed class SaveService
    {
        readonly SaveStore store; readonly GameContext ctx; readonly IClock clock;
        public double MinGapSec = 2, LastSeenEverySec = 15;
        double sinceWrite, sinceSeen; bool dirty;
        public int Writes { get; private set; }
        public event Action<Exception> SaveFailed;

        public SaveService(SaveStore store, GameContext ctx, IClock clock)
        {
            this.store = store; this.ctx = ctx; this.clock = clock;
            ctx.StateChanged += () => dirty = true;
        }

        public void Tick(double dt)
        {
            sinceWrite += dt; sinceSeen += dt;
            if (sinceSeen >= LastSeenEverySec) { sinceSeen = 0; ctx.S.LastSeen = clock.NowMs; dirty = true; }
            if (dirty && sinceWrite >= MinGapSec) Write();
        }

        /// <summary>지금 곧바로 쓴다(lastSeen 갱신 포함).</summary>
        public void Flush() { ctx.S.LastSeen = clock.NowMs; Write(); }

        void Write()
        {
            dirty = false; sinceWrite = 0;
            try { store.Save(ctx.S); Writes++; }
            catch (Exception e) { dirty = true; SaveFailed?.Invoke(e); }
        }
    }
}
