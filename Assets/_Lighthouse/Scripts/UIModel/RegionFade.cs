using System;

namespace Lighthouse.UI.Model
{
    /// <summary>
    /// 지역(바다) 전환 물안개의 시간표(원본 switchRegion 1903~1918행): ms = 700(움직임 줄이기면 0).
    /// 0 → ms 동안 투명도 0→1(ease), ms+250 에 바다를 바꾸고 팝업을 닫고, 그 뒤 500ms 머문 뒤 0 으로 ms 동안 걷히고, ms+50 뒤 안개를 치우고 도착을 알린다.
    /// </summary>
    public readonly struct RegionFadeTiming
    {
        public readonly float Ms;
        public RegionFadeTiming(bool calm) { Ms = calm ? 0f : 0.7f; }
        /// <summary>바다를 바꾸는 시각(초).</summary>
        public float CommitAt => Ms + 0.25f;
        /// <summary>걷히기 시작하는 시각.</summary>
        public float FadeOutAt => CommitAt + 0.5f;
        /// <summary>안개를 치우고 도착을 알리는 시각.</summary>
        public float EndAt => FadeOutAt + Ms + 0.05f;
        public float Alpha(float t)
        {
            float a;
            if (t < FadeOutAt) a = Ms <= 0f ? 1f : Math.Min(1f, Math.Max(0f, t / Ms));
            else a = Ms <= 0f ? 0f : 1f - Math.Min(1f, (t - FadeOutAt) / Ms);
            return a * a * (3f - 2f * a);                                                       // CSS ease 에 가까운 부드러운 곡선
        }
    }

    /// <summary>바다 전환을 화면에 부탁하는 호스트(안개 연출은 화면이 한다). 선택 사항이라 IUiHost 와 따로 둔다. 없으면 컨트롤러가 즉시 바꾼다.</summary>
    public interface IUiHostRegion { void SwitchRegion(string id, string name, Action commit, Action arrive); }

    public sealed partial class PanelController
    {
        bool regionBusy;
        /// <summary>설정의 바다 칸(원본 switchRegion): 연출이 있으면 안개가 덮인 때(commit) 바꾸고 걷힌 뒤(arrive) 도착 토스트, 없으면 즉시.</summary>
        void SwitchRegionAct(string id)
        {
            if (regionBusy || id == Ctx.CurRegion() || !RegionUsable(id)) return;
            string name = T.RegionById[id].Name; regionBusy = true;
            Action commit = () => CommitRegion(id), arrive = () => { regionBusy = false; Host.Toast(F_("toast.arrive", name), S_("toast.arriveSub")); };
            if (Host is IUiHostRegion h) h.SwitchRegion(id, name, commit, arrive); else { commit(); arrive(); }
        }

        /// <summary>안개가 덮인 순간: 바다를 바꾸고(섬·하늘 다시 굽기), 도감의 바다도 맞추고, 팝업이 열려 있으면 닫는다(원본 1912행).</summary>
        public void CommitRegion(string id)
        {
            S.Region = id; dexRg = id; Ctx.NotifyChanged(); Host.WorldDirty();
            if (Mode != PanelMode.None) Close();
        }
        public bool RegionBusy => regionBusy;
    }
}
