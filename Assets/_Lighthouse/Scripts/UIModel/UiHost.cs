using System.Collections.Generic;
using System.Threading.Tasks;
using Lighthouse.Core;
using Newtonsoft.Json.Linq;

namespace Lighthouse.UI.Model
{
    /// <summary>화면이 Core 밖(씬·플랫폼)에 부탁하는 일. Unity 쪽(UiRoot)이 구현하고, 시험은 기록용 구현을 쓴다.</summary>
    public interface IUiHost
    {
        void Sfx(string name);                  // tick | pick
        void WorldDirty();                      // 외형·계절·장식이 바뀜 → 섬을 다시 굽는다 (원본 needRebuild)
        void StartPhoto();
        void ApplyView(string view);            // near | normal | wide
        void ApplyWake(bool on);
        bool WakeSupported { get; }
        void Toast(string title, string sub);
        void CopyText(string text);
        void SaveTextFile(string name, string text);
        void ImportSave(JObject data);          // 가져오기 확정 (저장 교체는 앱이 한다)
        void ClosePanelRequested();
    }

    public sealed class UiFeatures
    {
        public bool Shop = true, Ads = true;    // 원본 FEATURES (결제·광고 테스트 단계)
        public bool PaymentTest = true;         // 원본 PAYMENT.mode === 'test'
    }

    /// <summary>시험·미리보기용 호스트: 부른 기록만 남긴다.</summary>
    public sealed class RecordingHost : IUiHost
    {
        public readonly List<string> Log = new List<string>();
        public bool Wake;
        public string View = "normal", LastCopy;
        public JObject Imported;
        public void Sfx(string name) { Log.Add("sfx:" + name); }
        public void WorldDirty() { Log.Add("dirty"); }
        public void StartPhoto() { Log.Add("photo"); }
        public void ApplyView(string view) { View = view; Log.Add("view:" + view); }
        public void ApplyWake(bool on) { Wake = on; Log.Add("wake:" + on); }
        public bool WakeSupported => true;
        public void Toast(string title, string sub) { Log.Add("toast:" + title); }
        public void CopyText(string text) { LastCopy = text; Log.Add("copy"); }
        public void SaveTextFile(string name, string text) { Log.Add("file:" + name); }
        public void ImportSave(JObject data) { Imported = data; Log.Add("import"); }
        public void ClosePanelRequested() { Log.Add("close"); }
    }

    /// <summary>시험 결제 어댑터(원본 PAYMENT mode 'test'): 돈이 나가지 않고 바로 성공. 실 SDK 는 M8.</summary>
    public sealed class TestPaymentAdapter : IPaymentAdapter
    {
        public bool Fail;
        public Task<PurchaseResult> Purchase(ProductDef product, int dueWon) => Task.FromResult(new PurchaseResult { Ok = !Fail, Reason = Fail ? "error" : null });
        public Task<IReadOnlyList<string>> Restore() => Task.FromResult<IReadOnlyList<string>>(new List<string>());
    }

    /// <summary>시험 광고 어댑터: 화면(AdLayer)이 Finish 를 불러 끝낸다. 실 SDK 는 M8.</summary>
    public sealed class TestAdAdapter : IAdAdapter
    {
        TaskCompletionSource<AdResult> tcs;
        public bool Showing => tcs != null && !tcs.Task.IsCompleted;
        public string Kind { get; private set; }
        public Task<AdResult> Show(string kindId) { Kind = kindId; tcs = new TaskCompletionSource<AdResult>(); return tcs.Task; }
        public void Finish(bool ok, string reason = null) { tcs?.TrySetResult(new AdResult { Ok = ok, Reason = reason }); }
        public void Abort() { Finish(false, "error"); }
    }
}
