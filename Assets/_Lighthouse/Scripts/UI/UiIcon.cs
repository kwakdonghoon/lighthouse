using UnityEngine;
using UnityEngine.UI;

namespace Lighthouse.UI
{
    /// <summary>
    /// 아이콘 이름을 기억해 두었다가 실행 중에 스프라이트를 올린다. 아이콘은 Render 의 굽기로 만든 런타임 텍스처라 프리팹에 직접 저장할 수 없다
    /// (저장하면 참조가 끊겨 흰 사각형이 된다 — 첫 캡처에서 실측).
    /// </summary>
    public sealed class UiIcon : MonoBehaviour
    {
        public string Id;
        public void Apply(bool force = false)
        {
            var img = GetComponent<Image>(); if (img == null || string.IsNullOrEmpty(Id)) return;
            if (force || img.sprite == null) img.sprite = UiTheme.Icon(Id);
        }
        public void Set(string id) { Id = id; Apply(true); }
    }
}
