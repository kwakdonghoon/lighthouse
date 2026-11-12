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

        public static bool IsPackGrant(string grant) => grant.StartsWith("pack:", StringComparison.Ordinal);

        /// <summary>
        /// 상품 안에서 주는 것 하나의 차감 값. 보통은 ItemValue 그대로(원본 그대로 — 시작 꾸러미처럼 합이 가격을 넘는 상품도 비례 계산이라 문제없다).
        /// 팩 단위 주는 것(`pack:` — 장식 묶음)이 든 상품만: 합계가 가격을 넘으면 값마다 가격/합계 비율로 줄이고 가격 단위(10원)로 내림해
        /// 합계가 가격을 넘지 않게 한다(G2'. festpack 3300→2190 ≤ 2200). 어느 하나만 남아도 차감가는 가격 단위 이상으로 남는다.
        /// </summary>
        public static double ItemValue(GameTables T, ProductDef p, string grant)
        {
            double v = ItemValue(T, grant);
            if (!p.Grants.Any(IsPackGrant)) return v;
            double total = p.Grants.Sum(g => ItemValue(T, g));
            if (total <= p.Price) return v;
            double step = T.PriceStep;
            return Math.Floor(v * p.Price / total / step) * step;
        }

        /// <summary>팩 단위 주는 것(`pack:이름`)은 같은 이름의 외형 묶음(`decor:이름`)을 가진 구매 기록에서 복원한다(팩에 장식이 생기기 전에 산 사람, 영수증 복원). 바뀌었으면 true.</summary>
        public static bool NormalizeOwned(GameTables T, Newtonsoft.Json.Linq.JObject owned)
        {
            bool ch = false;
            foreach (var p in T.Products)
                foreach (var g in p.Grants)
                {
                    if (!IsPackGrant(g)) continue;
                    string name = g.Substring(5);
                    bool base_ = p.Grants.Where(x => !IsPackGrant(x) && x.EndsWith(":" + name, StringComparison.Ordinal)).Any(x => owned[x] != null && Js.Truthy(owned[x]));
                    if (base_ && !(owned[g] != null && Js.Truthy(owned[g]))) { owned[g] = true; ch = true; }
                }
            return ch;
        }

        /// <summary>지금 내야 할 값(차감 가격). 다 가졌으면 0, 하나도 없으면 원래 가격. 실제 결제는 차감가마다 차액 상품 ID 가 필요하다.</summary>
        public static double DueOf(GameTables T, ProductDef p, Func<string, bool> has)
        {
            var left = p.Grants.Where(g => !has(g)).ToList();
            if (left.Count == 0) return 0;
            if (left.Count == p.Grants.Length) return p.Price;
            double total = p.Grants.Sum(g => ItemValue(T, p, g)), rem = left.Sum(g => ItemValue(T, p, g));
            double step = T.PriceStep;
            return Math.Min(p.Price - step, JsMath.Round(p.Price * rem / total / step) * step);
        }
    }
}
