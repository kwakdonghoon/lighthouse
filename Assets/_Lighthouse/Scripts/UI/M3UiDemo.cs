using System;
using Lighthouse.Core;
using Lighthouse.Data;
using Lighthouse.UI.Model;
using UnityEngine;

namespace Lighthouse.UI
{
    /// <summary>
    /// M3 데모 씬(M3UI.unity)의 호스트: 표를 읽고 샘플 진행 상태로 UI 를 띄운다. 게임 루프 연결(저장·월드·시간 흐름)은 M4 소관이라 여기서는 하지 않는다.
    /// 플레이 모드 확인용 — 타이틀을 누르면 시작, 메뉴 단추로 팝업, Esc 로 닫기.
    /// </summary>
    public sealed class M3UiDemo : MonoBehaviour
    {
        public int Stage = 4, Ships = 150;
        public UiRoot Ui { get; private set; }

        void Start()
        {
            var src = new ResourcesDataSource();
            var T = DataLoader.Load(src);
            var str = UiStrings.Parse(src.ReadText("ko/ui.json"));
            var ctx = SampleState.Make(T, new SystemClock(), 11, Stage, Ships, Stage >= 4 ? 4 : 1);
            var code = new SaveCode(T, new SaveMigrator(T, new SystemClock()));
            UiRoot.BootStrings = str;
            Ui = UiRoot.Create(null, false);
            Ui.Setup(ctx, str, null, null, code, null);
            Ui.ShowTitle(false);
        }
    }
}
