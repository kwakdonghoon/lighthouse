using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Lighthouse.Core
{
    /// <summary>
    /// JS 의 암묵 규칙(형 변환·진리값·typeof·키 순서·JSON.stringify 서식)을 JToken 위에서 그대로 재현하는 헬퍼.
    /// 저장 마이그레이션·가져오기(원본 migrate/sanitizeImport/cleanLetters)가 이 규칙에 기대어 1:1 로 포팅된다 (부록 B-9).
    /// </summary>
    public static class Js
    {
        // ---------- 파싱 ----------
        /// <summary>JSON 파싱. 날짜 모양 문자열을 날짜로 바꾸지 않는다(JS JSON.parse 와 같게).</summary>
        public static JToken Parse(string text)
        {
            using (var sr = new StringReader(text))
            using (var r = new JsonTextReader(sr) { DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Double })
            {
                var t = JToken.ReadFrom(r);
                if (r.Read() && r.TokenType != JsonToken.Comment) throw new JsonReaderException("JSON 뒤에 글자가 더 있음");
                return t;
            }
        }

        // ---------- 형 판정 ----------
        public static bool IsNumber(JToken t) => t != null && (t.Type == JTokenType.Integer || t.Type == JTokenType.Float);
        public static bool IsString(JToken t) => t != null && t.Type == JTokenType.String;
        public static bool IsObject(JToken t) => t != null && t.Type == JTokenType.Object;
        public static bool IsArray(JToken t) => t != null && t.Type == JTokenType.Array;
        /// <summary>JS obj(): v &amp;&amp; typeof v === 'object' &amp;&amp; !Array.isArray(v)</summary>
        public static bool IsPlainObject(JToken t) => t != null && t.Type == JTokenType.Object;

        /// <summary>토큰의 숫자 값(숫자 형이 아니면 NaN).</summary>
        public static double Num(JToken t)
        {
            if (t == null) return double.NaN;
            if (t.Type == JTokenType.Float) return (double)((JValue)t).Value;
            if (t.Type == JTokenType.Integer)
            {
                object v = ((JValue)t).Value;
                if (v is long l) return l;
                if (v is int i) return i;
                                if (v is ulong u) return u;
                return double.Parse(((JValue)t).ToString(CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture);      // 큰 정수(BigInteger)도 문자열 경유로 받는다
            }
            return double.NaN;
        }

        /// <summary>Number.isFinite(v): 숫자 형이면서 유한.</summary>
        public static bool IsFinite(JToken t) { if (!IsNumber(t)) return false; double d = Num(t); return !double.IsNaN(d) && !double.IsInfinity(d); }
        /// <summary>Number.isInteger(v).</summary>
        public static bool IsInteger(JToken t) { if (!IsNumber(t)) return false; double d = Num(t); return !double.IsNaN(d) && !double.IsInfinity(d) && Math.Floor(d) == d; }

        /// <summary>JS 단항 + (ToNumber). undefined(null 참조) → NaN.</summary>
        public static double ToNumber(JToken t)
        {
            if (t == null) return double.NaN;
            switch (t.Type)
            {
                case JTokenType.Integer:
                case JTokenType.Float: return Num(t);
                case JTokenType.Boolean: return (bool)t ? 1 : 0;
                case JTokenType.Null: return 0;
                case JTokenType.String: return StringToNumber((string)t);
                case JTokenType.Array:
                    {
                        var a = (JArray)t;
                        if (a.Count == 0) return 0;
                        if (a.Count == 1) return ToNumber(a[0]);
                        return double.NaN;
                    }
                default: return double.NaN;
            }
        }

        public static double StringToNumber(string s)
        {
            string x = s.Trim();
            if (x.Length == 0) return 0;
            if (x == "Infinity" || x == "+Infinity") return double.PositiveInfinity;
            if (x == "-Infinity") return double.NegativeInfinity;
            if (x.Length > 2 && x[0] == '0' && (x[1] == 'x' || x[1] == 'X'))
            {
                double acc = 0; for (int i = 2; i < x.Length; i++) { int d = Uri.IsHexDigit(x[i]) ? Convert.ToInt32(x[i].ToString(), 16) : -1; if (d < 0) return double.NaN; acc = acc * 16 + d; }
                return acc;
            }
            // JS 숫자 문자열: 부호, 숫자, 소수점, 지수. 그 밖(예: "1,5", "5px", ".")은 NaN
            foreach (char c in x) if (!(char.IsDigit(c) && c < 128) && c != '.' && c != 'e' && c != 'E' && c != '+' && c != '-') return double.NaN;
            if (double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out double r)) return r;
            return double.NaN;
        }

        /// <summary>JS 진리값. undefined(null 참조)·null·false·0·NaN·'' 이 거짓.</summary>
        public static bool Truthy(JToken t)
        {
            if (t == null) return false;
            switch (t.Type)
            {
                case JTokenType.Null: return false;
                case JTokenType.Boolean: return (bool)t;
                case JTokenType.Integer:
                case JTokenType.Float: { double d = Num(t); return !(d == 0 || double.IsNaN(d)); }
                case JTokenType.String: return ((string)t).Length > 0;
                default: return true;
            }
        }

        /// <summary>JS typeof (undefined = null 참조). null 값은 'object'.</summary>
        public static string TypeOf(JToken t)
        {
            if (t == null) return "undefined";
            switch (t.Type)
            {
                case JTokenType.Integer:
                case JTokenType.Float: return "number";
                case JTokenType.String: return "string";
                case JTokenType.Boolean: return "boolean";
                default: return "object";
            }
        }

        // ---------- 객체 접근 ----------
        /// <summary>o[k] (없으면 null 참조 = undefined). 배열은 인덱스 키 지원, 그 밖의 원시값은 undefined.</summary>
        public static JToken Get(JToken o, string k)
        {
            if (o is JObject jo) return jo.TryGetValue(k, StringComparison.Ordinal, out JToken v) ? v : null;
            if (o is JArray ja && int.TryParse(k, NumberStyles.None, CultureInfo.InvariantCulture, out int i) && i >= 0 && i < ja.Count && i.ToString(CultureInfo.InvariantCulture) == k) return ja[i];
            return null;
        }

        /// <summary>hasOwn(o, k): o != null &amp;&amp; hasOwnProperty.call(o, k).</summary>
        public static bool HasOwn(JToken o, string k)
        {
            if (o is JObject jo) return k != null && jo.ContainsKey(k);
            if (o is JArray) return Get(o, k) != null;
            return false;
        }

        /// <summary>Object.keys(o). 객체는 JSON 순서, 배열은 인덱스 문자열, 그 밖은 빈 목록. (정수 모양 키가 먼저 오름차순이라는 JS 규칙은 표 키에는 해당 없음.)</summary>
        public static List<string> Keys(JToken o)
        {
            var r = new List<string>();
            if (o is JObject jo) { foreach (var p in jo.Properties()) r.Add(p.Name); }
            else if (o is JArray ja) { for (int i = 0; i < ja.Count; i++) r.Add(i.ToString(CultureInfo.InvariantCulture)); }
            return r;
        }

        static readonly HashSet<string> objectProto = new HashSet<string>(StringComparer.Ordinal)
        {
            "constructor", "__defineGetter__", "__defineSetter__", "hasOwnProperty", "__lookupGetter__", "__lookupSetter__",
            "isPrototypeOf", "propertyIsEnumerable", "toString", "valueOf", "__proto__", "toLocaleString"
        };
        /// <summary>k in Object.prototype</summary>
        public static bool InObjectPrototype(string k) => objectProto.Contains(k);

        /// <summary>정수면 정수 값, 아니면 실수 값의 JValue (JSON 으로 쓸 때 5.0 이 아니라 5 가 되게).</summary>
        public static JValue N(double d)
        {
            if (!double.IsNaN(d) && !double.IsInfinity(d) && Math.Floor(d) == d && Math.Abs(d) < 9007199254740992.0) return new JValue((long)d);
            return new JValue(d);
        }

        /// <summary>문자열 slice(0, n) (UTF-16 단위).</summary>
        public static string Slice(string s, int n) => s.Length <= n ? s : s.Substring(0, n);

        /// <summary>JS 문자열이 토큰이면 그 문자열, 아니면 null.</summary>
        public static string Str(JToken t) => t != null && t.Type == JTokenType.String ? (string)t : null;

        // ---------- 숫자 → 문자열 (JS Number.prototype.toString) ----------
        /// <summary>JS 의 Number→String. 최단 왕복 자릿수에 JS 의 지수 표기 규칙(1e21 이상·1e-7 미만)을 적용한다.</summary>
        public static string NumToString(double v)
        {
            if (double.IsNaN(v)) return "NaN";
            if (double.IsPositiveInfinity(v)) return "Infinity";
            if (double.IsNegativeInfinity(v)) return "-Infinity";
            if (v == 0) return "0";
            string sign = v < 0 ? "-" : "";
            double a = Math.Abs(v);
            // 최단 왕복 자릿수 찾기 (E 표기의 가수 자릿수 1..17)
            string digits = null; int exp10 = 0;
            for (int p = 1; p <= 17; p++)
            {
                string e = a.ToString("E" + (p - 1), CultureInfo.InvariantCulture);       // d.ddddE+xxx
                if (double.Parse(e, NumberStyles.Float, CultureInfo.InvariantCulture) == a)
                {
                    int ei = e.IndexOf('E');
                    digits = e.Substring(0, ei).Replace(".", "");
                    exp10 = int.Parse(e.Substring(ei + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                    break;
                }
            }
            if (digits == null) { string e = a.ToString("E16", CultureInfo.InvariantCulture); int ei = e.IndexOf('E'); digits = e.Substring(0, ei).Replace(".", ""); exp10 = int.Parse(e.Substring(ei + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture); }
            digits = digits.TrimEnd('0'); if (digits.Length == 0) digits = "0";
            int k = digits.Length, n = exp10 + 1;       // 값 = 0.digits × 10^n
            var sb = new StringBuilder(sign);
            if (k <= n && n <= 21) { sb.Append(digits); sb.Append('0', n - k); }
            else if (0 < n && n <= 21) { sb.Append(digits, 0, n); sb.Append('.'); sb.Append(digits, n, k - n); }
            else if (-6 < n && n <= 0) { sb.Append("0."); sb.Append('0', -n); sb.Append(digits); }
            else
            {
                int e = n - 1;
                sb.Append(digits[0]);
                if (k > 1) { sb.Append('.'); sb.Append(digits, 1, k - 1); }
                sb.Append('e'); sb.Append(e < 0 ? '-' : '+'); sb.Append(Math.Abs(e).ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        // ---------- JSON.stringify ----------
        /// <summary>JS JSON.stringify(value) 와 같은 한 줄 서식. 키 순서는 삽입 순서, 유한하지 않은 수는 null, 한글은 그대로, 제어문자는 \uXXXX(소문자).</summary>
        public static string Stringify(JToken t)
        {
            var sb = new StringBuilder();
            Write(sb, t);
            return sb.ToString();
        }

        static void Write(StringBuilder sb, JToken t)
        {
            if (t == null) { sb.Append("null"); return; }
            switch (t.Type)
            {
                case JTokenType.Object:
                    {
                        sb.Append('{'); bool first = true;
                        foreach (var p in ((JObject)t).Properties())
                        {
                            if (p.Value == null || p.Value.Type == JTokenType.Undefined) continue;
                            if (!first) sb.Append(','); first = false;
                            WriteStr(sb, p.Name); sb.Append(':'); Write(sb, p.Value);
                        }
                        sb.Append('}'); break;
                    }
                case JTokenType.Array:
                    {
                        sb.Append('['); bool first = true;
                        foreach (var x in (JArray)t) { if (!first) sb.Append(','); first = false; Write(sb, x); }
                        sb.Append(']'); break;
                    }
                case JTokenType.String: WriteStr(sb, (string)t); break;
                case JTokenType.Boolean: sb.Append((bool)t ? "true" : "false"); break;
                case JTokenType.Integer:
                    {
                        object v = ((JValue)t).Value;
                        if (v is long l && Math.Abs((double)l) < 9007199254740992.0) sb.Append(l.ToString(CultureInfo.InvariantCulture));
                        else { double d = Num(t); sb.Append(double.IsInfinity(d) || double.IsNaN(d) ? "null" : NumToString(d)); }
                        break;
                    }
                case JTokenType.Float:
                    {
                        double d = Num(t);
                        sb.Append(double.IsInfinity(d) || double.IsNaN(d) ? "null" : NumToString(d)); break;
                    }
                default: sb.Append("null"); break;
            }
        }

        static void WriteStr(StringBuilder sb, string s)
        {
            sb.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { sb.Append(c).Append(s[i + 1]); i++; }
                        else if (char.IsSurrogate(c)) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ---------- 문자열 정렬 (Array.prototype.sort() 기본: 숫자도 문자열로 비교) ----------
        /// <summary>JS arr.sort() (비교 함수 없음) 로 숫자 배열을 정렬한다: 각 수를 JS 문자열로 바꿔 UTF-16 코드 단위 순으로 비교, 안정 정렬.</summary>
        public static double[] SortAsStrings(double[] a)
        {
            var keys = new string[a.Length]; var idx = new int[a.Length];
            for (int i = 0; i < a.Length; i++) { keys[i] = NumToString(a[i]); idx[i] = i; }
            var order = new List<int>(idx);
            order.Sort((x, y) => { int c = string.CompareOrdinal(keys[x], keys[y]); return c != 0 ? c : x.CompareTo(y); });
            var r = new double[a.Length];
            for (int i = 0; i < r.Length; i++) r[i] = a[order[i]];
            return r;
        }

        // ---------- 해시 (FNV-1a 변형, 원본 hashStr) ----------
        /// <summary>원본 hashStr: FNV-1a 32비트(문자 코드 단위) → 36진 문자열.</summary>
        public static string HashStr(string s)
        {
            unchecked
            {
                uint h = 2166136261u;
                for (int i = 0; i < s.Length; i++) { h ^= s[i]; h *= 16777619u; }
                return ToBase36(h);
            }
        }

        public static string ToBase36(uint v)
        {
            if (v == 0) return "0";
            const string D = "0123456789abcdefghijklmnopqrstuvwxyz";
            var sb = new StringBuilder();
            while (v > 0) { sb.Insert(0, D[(int)(v % 36)]); v /= 36; }
            return sb.ToString();
        }
    }
}
