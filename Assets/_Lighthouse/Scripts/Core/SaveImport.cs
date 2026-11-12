using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Lighthouse.Core
{
    /// <summary>
    /// 가져오기 확정 뒤 새 저장 상태를 만든다 (원본 importAct 의 Object.assign(save, r.data, { owned, ads, lastSeen: Date.now() }), HTML 3753~3756행).
    ///  - 구매 기록(owned)과 광고 횟수(ads)는 코드에 담기지 않으므로 이 기기 것을 그대로 가져간다(가져오기로 광고 한도를 되돌리거나 구매를 지울 수 없게).
    ///  - lastSeen = 지금: 가져온 순간부터 다시 센다(비운 시간 보상 없음).
    /// 순수 C# — GameApp 이 부르고, dotnet 시험이 같은 코드를 시험한다.
    /// </summary>
    public static class SaveImport
    {
        public static GameState Apply(GameState device, JObject data, SaveMigrator mig, long nowMs)
        {
            var st = GameState.FromJson(mig.Migrate(data));
            st.Owned = device.Owned != null ? (JObject)device.Owned.DeepClone() : new JObject();
            st.Ads = new AdsState { Day = device.Ads.Day, HomeDay = device.Ads.HomeDay, Used = new Dictionary<string, double>(device.Ads.Used) };
            st.LastSeen = nowMs;
            return st;
        }
    }

    /// <summary>
    /// 저장 코드·파일·클립보드 같은 "플랫폼에 부탁하는 일" (구현은 Lighthouse.Platform). UI 는 이 인터페이스만 안다.
    /// 모바일은 선택 창이 없으므로 PickTextFile 은 "내보낸 폴더의 가장 새 .txt → 클립보드의 LHS1 코드" 순으로 찾는다.
    /// </summary>
    public interface ITextTransfer
    {
        void CopyText(string text);
        /// <summary>파일로 내보낸다(모바일은 공유 시트, 데스크톱은 문서 폴더). 돌려주는 값은 만든 파일 경로(실패하면 null).</summary>
        string SaveTextFile(string fileName, string text);
        /// <summary>가져올 텍스트를 찾아 콜백으로 준다. 못 찾거나 취소하면 null.</summary>
        void PickTextFile(Action<string> onText);
    }
}
