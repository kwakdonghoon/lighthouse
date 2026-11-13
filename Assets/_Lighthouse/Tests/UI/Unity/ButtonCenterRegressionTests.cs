using System.Collections.Generic;
using System.Linq;
using Lighthouse.Core;
using Lighthouse.EditorTools;
using Lighthouse.UI;
using Lighthouse.UI.Model;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Lighthouse.Tests
{
    /// <summary>
    /// 단추 안 글·아이콘 가운데 정렬 회귀 시험. 수동 캡처 감사(M3Tools.CenterAudit)와 같은 검사를 EditMode 에서 돌린다.
    /// 면 있는 단추: 내용 중심 = 면 중심(위·아래 테두리 보정) ±(가로 1칸·세로 2.2칸), 면 안에 들어올 것.
    /// 면 없는 단추(편지 줄·링크·카드 머리줄·칸): 단추 사각형 안에 들어올 것, 글자 링크는 가운데.
    /// </summary>
    public class ButtonCenterRegressionTests
    {
        static UiRoot Make(int dpW, int dpH, float dpr, int stage, out UiFixture.Rig rig)
        {
            rig = UiFixture.Make(stage, stage == 6 ? 260 : stage == 1 ? 0 : 100, stage >= 4 ? 3 : 0);
            UiRoot.BootStrings = rig.Str;
            var ui = UiRoot.Create(null, false);
            ui.ManualScreen = true; ui.DevW = Mathf.RoundToInt(dpW * dpr); ui.DevH = Mathf.RoundToInt(dpH * dpr); ui.Density = dpr;
            ui.Safe = new Insets(0, dpW <= 800 ? 24f : 0f, 0, dpW <= 800 ? 16f : 0f);
            ui.Setup(rig.Ctx, rig.Str, rig.Pay, rig.Ads, null, null);
            return ui;
        }

        static readonly (string name, System.Action<UiRoot> open)[] Screens =
        {
            ("mail", ui => { ui.Ctl.Open(PanelMode.Mail); }),
            ("dex", ui => ui.Ctl.Open(PanelMode.Dex)),
            ("dexsea", ui => { ui.Ctl.Open(PanelMode.Dex); ui.Ctl.Act("dex:sea"); }),
            ("growth", ui => { ui.Ctl.Open(PanelMode.Growth); ui.Ctl.Act("rolebox"); }),
            ("offer", ui => { ui.Ctl.Open(PanelMode.Growth); ui.Ctl.Act("grow:offer"); }),
            ("deco", ui => { ui.Ctl.Open(PanelMode.Deco); ui.Ctl.Act("zone:front"); }),
            ("skin", ui => { ui.Ctl.Open(PanelMode.Deco); ui.Ctl.Act("dtab:skin"); }),
            ("shop", ui => { ui.Ctl.Open(PanelMode.Shop); ui.Ctl.Act("adtoggle"); }),
            ("settings", ui => { ui.Ctl.Open(PanelMode.Growth); ui.Ctl.Act("gear"); ui.Ctl.Act("setmore"); }),
            ("title", ui => ui.ShowTitle(false)),
            ("photo", ui => ui.SetPhoto(true)),
            ("tip", ui => ui.Tip.Show(new TipInfo { Id = "t", Icon = "star", Text = "등대를 눌러 오늘의 첫 불빛을 켜 보세요" }, ui.Str, UiRoot.CanvasW / 2f, 220f, true, UiRoot.CanvasW)),
        };

        [TestCase(320, 568, 2f), TestCase(393, 852, 2f), TestCase(393, 852, 2.75f), TestCase(768, 1024, 2f)]
        public void ButtonContentStaysCenteredAndInsideTheFace(int dpW, int dpH, float dpr)
        {
            var ui = Make(dpW, dpH, dpr, 6, out _);
            try
            {
                var bad = new List<string>(); int total = 0;
                foreach (var (name, open) in Screens)
                {
                    ui.Ctl.Close(); ui.SetPhoto(false); ui.Tip.Hide(); ui.Title.gameObject.SetActive(false);
                    open(ui); ui.Panel.Refresh(false); Canvas.ForceUpdateCanvases(); LayoutRebuilder.ForceRebuildLayoutImmediate(ui.CanvasRect); Canvas.ForceUpdateCanvases();
                    total += M3Tools.CenterAuditPublic(ui, $"{dpW}x{dpH}@{dpr} {name}", bad);
                }
                Assert.That(total, Is.GreaterThan(60), "검사한 단추가 너무 적다 — 검사가 헛돌고 있을 수 있다");
                Assert.That(bad, Is.Empty, string.Join("\n", bad.Take(12)));
            }
            finally { Object.DestroyImmediate(ui.gameObject); }
        }

        [Test]
        public void FaceFramesKeepTheSameBottomMinusTopBorder()
        {
            // FrameButton 안쪽 여백은 위 3·아래 5 칸으로 고정. 틀 그림의 (아래-위) 테두리 차이가 달라지면 내용이 틀 안에서 치우치므로 여기서 잡는다.
            foreach (var f in new[] { "main", "gold", "btn", "hbtn", "hbtn_on", "ib" })
            {
                var sp = UiTheme.Frame(f); Assert.IsNotNull(sp, f);
                Assert.That(sp.border.y - sp.border.w, Is.EqualTo(2f).Within(0.01f), $"틀 {f}: 아래({sp.border.y})-위({sp.border.w}) 테두리 차이가 2칸이어야 FrameButton 여백(3/5)과 맞는다");
            }
        }
    }
}
