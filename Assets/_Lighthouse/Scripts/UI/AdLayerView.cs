using System;
using Lighthouse.UI.Model;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Lighthouse.UI
{
    /// <summary>테스트 광고 화면(원본 #adlayer): 실제 광고가 아니라 개발 확인용. 실 SDK 는 M8.</summary>
    public sealed class AdLayerView : MonoBehaviour
    {
        public TMP_Text Wait; public Button Ok, Cancel; public Image Ok_;
        public static AdLayerView Create(Transform parent, UiStrings s)
        {
            var root = Kit.Rect("AdLayer", parent); Kit.Stretch(root); var a = root.gameObject.AddComponent<AdLayerView>();
            var dim = Kit.Solid(root, new Color(0.03f, 0.05f, 0.12f, 0.82f), "dim"); Kit.Stretch(dim.rectTransform); dim.raycastTarget = true;
            var card = Kit.Frame(root, "card", null, "card"); card.raycastTarget = false; var cr = card.rectTransform; cr.anchorMin = cr.anchorMax = new Vector2(0.5f, 0.5f); cr.pivot = new Vector2(0.5f, 0.5f); cr.sizeDelta = new Vector2(300, 10);
            var v = card.gameObject.AddComponent<VerticalLayoutGroup>(); v.childAlignment = TextAnchor.UpperCenter; v.childControlWidth = v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false; v.spacing = 6;
            var dl = card.gameObject.AddComponent<DotLayout>(); dl.Group = v; dl.PadDots = new Vector4(9, 9, 9, 9); dl.ApplyDot(UiRoot.CurrentDot);
            card.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            Kit.Text(card.transform, s.Get("ad.title"), 18f, UiTheme.Ink, true, TextAlignmentOptions.Center);
            Kit.Text(card.transform, s.Get("ad.note"), 14f, UiTheme.Soft, false, TextAlignmentOptions.Center);
            a.Wait = Kit.Text(card.transform, "", 15f, UiTheme.Teal2, true, TextAlignmentOptions.Center, "wait");
            a.Ok = Kit.FrameButton(card.transform, "main", "main_p", s.Get("ad.ok"), 15f, Color.white, null, "ok", true, 48f); a.Ok_ = a.Ok.GetComponent<Image>();
            a.Cancel = Kit.FrameButton(card.transform, "btn", "btn_p", s.Get("ad.cancel"), 15f, UiTheme.Ink, null, "cancel", true, 48f);
            root.gameObject.SetActive(false);
            return a;
        }
        public void SetReady(bool ready, int secLeft, UiStrings s)
        {
            Wait.text = ready ? UiText.Display(s.Get("ad.done")) : UiText.Display(s.F("ad.wait", secLeft));
            Ok_.sprite = UiTheme.Frame(ready ? "main" : "btn_d"); Kit.LabelOf(Ok).color = ready ? Color.white : UiTheme.Soft; Ok.interactable = ready;
        }
    }
}
