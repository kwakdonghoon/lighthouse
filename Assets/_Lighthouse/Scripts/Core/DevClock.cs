#if LIGHTHOUSE_DEV
using System;

namespace Lighthouse.Core
{
    /// <summary>
    /// 개발용 시계: 실제 시계 + 더해 준 시간. 배속(GameApp.DevTimeMul)과 "N시간 건너뛰기"가 이것을 앞으로 보낸다.
    /// 출시 빌드(LIGHTHOUSE_DEV 없음)에는 이 파일이 통째로 컴파일에서 빠진다.
    /// 주의: 더해 준 시간은 저장되지 않는다 — 배속으로 논 저장은 다음 실행 때 lastSeen 이 "미래"가 되어 비운 시간 보상이 0 이 된다(기기 시계가 뒤로 간 것과 같은 규칙).
    /// </summary>
    public sealed class DevClock : IClock
    {
        readonly IClock real; long extraMs;
        public DevClock(IClock real, long startExtraMs = 0) { this.real = real; extraMs = startExtraMs; }
        public long ExtraMs => extraMs;
        public long NowMs => real.NowMs + extraMs;
        public int OffsetMinutes(long utcMs) => real.OffsetMinutes(utcMs);
        public void AddExtraSec(double sec) { extraMs += (long)Math.Round(sec * 1000.0); }
    }
}
#endif
