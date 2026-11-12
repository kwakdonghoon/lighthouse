namespace Lighthouse.Audio
{
    /// <summary>
    /// 효과음 재생 규칙(원본 sfx() 가드 + 전역 클릭 소리 + 소리 켤 때 tick). 순수 C# 이라 시험할 수 있다.
    ///  - 소리가 꺼져 있거나 광고 중이면 어떤 소리도 내지 않는다(원본: save.sound === false || adOpen).
    ///  - 'click'(단추 탭 소리)은 직전에 다른 소리가 80ms 안에 났으면 내지 않는다(그 단추가 이미 tick/pick 을 냈다는 뜻).
    ///  - 소리를 끈 상태에서 켜는 순간에만 50ms 뒤 tick 한 번(끌 때는 소리·안내 없음).
    /// </summary>
    public sealed class SfxGate
    {
        public const double ClickGapSec = 0.080, EnableTickDelaySec = 0.050;
        double lastNonClick = -1e9, enableTickAt = -1; bool prevOn = true, prevInit;

        public bool Allow(string name, double now, bool soundOn, bool adOpen)
        {
            if (!soundOn || adOpen) return false;
            if (name == "click") return now - lastNonClick > ClickGapSec;
            lastNonClick = now; return true;
        }

        /// <summary>매 프레임 부른다. 꺼짐→켜짐 전환을 보고 50ms 뒤 true 를 한 번 돌려준다.</summary>
        public bool PollEnableTick(bool soundOn, double now)
        {
            if (!prevInit) { prevInit = true; prevOn = soundOn; return false; }
            if (soundOn && !prevOn) enableTickAt = now + EnableTickDelaySec;
            if (!soundOn) enableTickAt = -1;
            prevOn = soundOn;
            if (enableTickAt >= 0 && now >= enableTickAt) { enableTickAt = -1; return true; }
            return false;
        }
    }
}
