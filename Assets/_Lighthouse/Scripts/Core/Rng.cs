using System;

namespace Lighthouse.Core
{
    /// <summary>
    /// 난수 한 곳. HTML 의 Math.random() 161곳은 모두 이 인터페이스로 온다 (이식 설계서 부록 B-5).
    /// 호출 순서·횟수가 결과를 정하므로, 로직 코드는 원본과 같은 순서로 NextDouble() 만 부른다.
    /// </summary>
    public interface IRng
    {
        /// <summary>[0, 1) 의 난수.</summary>
        double NextDouble();
    }

    /// <summary>
    /// HTML 의 seededRandom(mulberry32) 포트. 같은 seed → 같은 열. Math.imul·&gt;&gt;&gt; 규칙은 uint 래핑 산술과 비트가 같다.
    /// </summary>
    public sealed class Mulberry32 : IRng
    {
        uint a;
        /// <summary>호출 횟수(골든 대조용: 원본에서 센 Math.random 횟수와 비교한다).</summary>
        public long Calls;

        /// <summary>JS: let a = (seed &gt;&gt;&gt; 0) || 1</summary>
        public Mulberry32(long seed)
        {
            uint s = unchecked((uint)seed);
            a = s == 0 ? 1u : s;
        }

        public double NextDouble()
        {
            Calls++;
            unchecked
            {
                a += 0x6D2B79F5u;
                uint t = a;
                t = (t ^ (t >> 15)) * (t | 1u);
                t ^= t + (t ^ (t >> 7)) * (t | 61u);
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }
    }

    /// <summary>실제 게임용 난수(시간 씨앗). 테스트·시뮬레이터는 Mulberry32 를 주입한다.</summary>
    public sealed class SystemRng : IRng
    {
        readonly Random r;
        public SystemRng() { r = new Random(); }
        public SystemRng(int seed) { r = new Random(seed); }
        public double NextDouble() => r.NextDouble();
    }

    /// <summary>
    /// 시계 한 곳. HTML 의 Date.now()·new Date().getHours() 42곳은 이 인터페이스로 온다 (부록 B-6).
    /// 시각은 UTC epoch ms(long 범위, 4.1e12 는 int 를 넘는다), 달력 날짜·시각은 로컬 오프셋으로 계산한다.
    /// </summary>
    public interface IClock
    {
        /// <summary>UTC epoch 밀리초.</summary>
        long NowMs { get; }
        /// <summary>그 시각의 로컬 시간대 오프셋(UTC 보다 몇 분 앞서는지; 한국 +540).</summary>
        int OffsetMinutes(long utcMs);
    }

    public sealed class SystemClock : IClock
    {
        public long NowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        public int OffsetMinutes(long utcMs)
        {
            try { return (int)TimeZoneInfo.Local.GetUtcOffset(DateTimeOffset.FromUnixTimeMilliseconds(utcMs)).TotalMinutes; }
            catch (ArgumentOutOfRangeException) { return 0; }
        }
    }

    /// <summary>시험용 고정 시계(시간이 흐르지 않는다). 오프셋 고정.</summary>
    public sealed class FixedClock : IClock
    {
        public long Now;
        readonly int off;
        public FixedClock(long nowMs, int offsetMinutes) { Now = nowMs; off = offsetMinutes; }
        public long NowMs => Now;
        public int OffsetMinutes(long utcMs) => off;
    }

    /// <summary>JS Date 의 로컬 달력 필드를 흉내 낸다 (getFullYear/getMonth()+1/getDate/getHours/getMinutes/getSeconds).</summary>
    public readonly struct LocalTime
    {
        public readonly int Year, Month, Day, Hour, Minute, Second;
        public LocalTime(int y, int mo, int d, int h, int mi, int s) { Year = y; Month = mo; Day = d; Hour = h; Minute = mi; Second = s; }

        public static LocalTime Of(long utcMs, IClock clock)
        {
            long local = utcMs + (long)clock.OffsetMinutes(utcMs) * 60000L;
            var dt = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(local);
            return new LocalTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second);
        }

        /// <summary>JS dayKeyOf/todayKey: 'YYYY-MM-DD'.</summary>
        public string DayKey => Year.ToString("0000") + "-" + Month.ToString("00") + "-" + Day.ToString("00");
    }
}
