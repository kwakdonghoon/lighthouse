using System;
using System.Collections.Generic;
using System.Linq;

namespace Lighthouse.Core
{
    // 결제·광고 SDK 는 범위 밖(M8). 여기에는 순수 계산(차감 가격·하루 한도)과 어댑터 인터페이스만 둔다 (설계서 5-2).

    /// <summary>구매 결과.</summary>
    public sealed class PurchaseResult { public bool Ok; public string Reason; }
    /// <summary>광고 결과: Ok(끝까지 봄) / Reason: closed|error.</summary>
    public sealed class AdResult { public bool Ok; public string Reason; }

    /// <summary>결제 어댑터(Unity IAP 등은 Platform 어셈블리가 구현한다). 빌드 심볼로 선택(LIGHTHOUSE_DEV: 테스트 어댑터, LIGHTHOUSE_STORE: 실 SDK).</summary>
    public interface IPaymentAdapter
    {
        System.Threading.Tasks.Task<PurchaseResult> Purchase(ProductDef product, int dueWon);
        System.Threading.Tasks.Task<IReadOnlyList<string>> Restore();
    }

    /// <summary>보상형 광고 어댑터. 완료일 때만 지급, 닫음·오류는 손실 없음(원본 finishAd).</summary>
    public interface IAdAdapter
    {
        System.Threading.Tasks.Task<AdResult> Show(string kindId);
        void Abort();
    }

    /// <summary>상점 차감 가격 계산 (원본 itemValue/dueOf 1678~1688행).</summary>
    public static class ShopMath
    {
        public static double ItemValue(GameTables T, string grant)
            => grant == "noads" ? T.Bal.ItemValueNoAds : grant.StartsWith("beam:", StringComparison.Ordinal) ? T.Bal.ItemValueBeam : T.Bal.ItemValueSkin;

        /// <summary>지금 내야 할 값(차감 가격). 다 가졌으면 0, 하나도 없으면 원래 가격. 실제 결제는 차감가마다 차액 상품 ID 가 필요하다.</summary>
        public static double DueOf(GameTables T, ProductDef p, Func<string, bool> has)
        {
            var left = p.Grants.Where(g => !has(g)).ToList();
            if (left.Count == 0) return 0;
            if (left.Count == p.Grants.Length) return p.Price;
            double total = p.Grants.Sum(g => ItemValue(T, g)), rem = left.Sum(g => ItemValue(T, g));
            double step = T.PriceStep;
            return Math.Min(p.Price - step, JsMath.Round(p.Price * rem / total / step) * step);
        }
    }
}
