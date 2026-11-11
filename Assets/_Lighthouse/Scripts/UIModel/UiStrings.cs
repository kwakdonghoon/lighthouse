using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Lighthouse.UI.Model
{
    public sealed class RoleInfo { public int Stage; public string Name, Desc; }
    public sealed class TipInfo { public string Id, Icon, At, Text, InPanel; }

    /// <summary>
    /// 화면 문구 표(Resources/Data/ko/ui.json). 화면 코드는 한글 문구를 직접 쓰지 않고 키로 가져온다(다국어·ContentLint 대상).
    /// 문자열의 {0}..{9} 만 바꿔 넣는다 — 조사 표지({을/를} 등)는 건드리지 않고 FixJosa 가 나중에 처리한다.
    /// </summary>
    public sealed class UiStrings
    {
        readonly Dictionary<string, string> s = new Dictionary<string, string>();
        public readonly List<RoleInfo> Roles = new List<RoleInfo>();
        public readonly List<TipInfo> Tips = new List<TipInfo>();
        public readonly HashSet<string> Missing = new HashSet<string>();
        public readonly HashSet<string> Used = new HashSet<string>();

        public static UiStrings Parse(string json)
        {
            var u = new UiStrings();
            var o = JObject.Parse(json);
            if (o["s"] is JObject so) foreach (var p in so.Properties()) u.s[p.Name] = (string)p.Value;
            if (o["roles"] is JArray ra) foreach (JObject r in ra) u.Roles.Add(new RoleInfo { Stage = (int)r["stage"], Name = (string)r["name"], Desc = (string)r["desc"] });
            if (o["tips"] is JArray ta) foreach (JObject t in ta) u.Tips.Add(new TipInfo { Id = (string)t["id"], Icon = (string)t["icon"], At = (string)t["at"], Text = (string)t["text"], InPanel = (string)t["inPanel"] });
            return u;
        }

        public bool Has(string key) => s.ContainsKey(key);
        public int Count => s.Count;

        public string Get(string key)
        {
            Used.Add(key);
            if (s.TryGetValue(key, out var v)) return v;
            Missing.Add(key);
            return "[" + key + "]";
        }

        /// <summary>{0}..{9} 치환.</summary>
        public string F(string key, params object[] args)
        {
            string t = Get(key);
            if (args == null || args.Length == 0) return t;
            var sb = new StringBuilder();
            for (int i = 0; i < t.Length; i++)
            {
                if (t[i] == '{' && i + 2 < t.Length && t[i + 2] == '}' && t[i + 1] >= '0' && t[i + 1] <= '9')
                {
                    int n = t[i + 1] - '0';
                    sb.Append(n < args.Length ? Convert.ToString(args[n], System.Globalization.CultureInfo.InvariantCulture) : "");
                    i += 2;
                }
                else sb.Append(t[i]);
            }
            return sb.ToString();
        }

        /// <summary>글 검사(ContentLint)와 글리프 수집용: 모든 문구.</summary>
        public IEnumerable<string> AllTexts()
        {
            foreach (var v in s.Values) yield return v;
            foreach (var r in Roles) { yield return r.Name; yield return r.Desc; }
            foreach (var t in Tips) yield return t.Text;
        }
    }
}
