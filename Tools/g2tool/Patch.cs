using System;
using System.IO;
using System.Linq;
using Lighthouse.Render;
using Newtonsoft.Json.Linq;

namespace Lighthouse.G2Tool
{
    public static partial class Program
    {
        /// <summary>Tools/g2_slots.json(생성기와 같은 파일)의 자리 수정을 메모리의 DecorBaker.Slots 에 먼저 적용한다 — 파일 생성 전에도 같은 값으로 검사·탐색.</summary>
        static void ApplyPatch()
        {
            string path = Path.Combine(Root(), "Tools", "g2_slots.json");
            if (!File.Exists(path) || Environment.GetEnvironmentVariable("G2NOPATCH") == "1") return;
            var j = JObject.Parse(File.ReadAllText(path));
            foreach (var p in j.Properties())
            {
                var sl = DecorBaker.Slot(p.Name); var o = (JObject)p.Value;
                if (o["ab"] is JArray ab) { sl.Ka = 0; sl.Ca = (double)ab[0]; sl.Kb = 0; sl.Cb = (double)ab[1]; }
                if (o["abLin"] is JArray al) { sl.Ka = (double)al[0]["k"]; sl.Ca = (double)al[0]["c"]; sl.Kb = (double)al[1]["k"]; sl.Cb = (double)al[1]["c"]; }
                if (o["blocked"] != null) sl.Blocked = (int)o["blocked"] != 0;
                if (o["minStage"] != null) sl.MinStage = (int)o["minStage"];
                if (o["open"] is JObject op)
                {
                    if (op["base"] != null) { sl.OpenKind = "base"; sl.OpenVal = 1; }
                    else if (op["stage"] != null) { sl.OpenKind = "stage"; sl.OpenVal = (int)op["stage"]; }
                    else if (op["lv"] != null) { sl.OpenKind = "lv"; sl.OpenVal = (int)op["lv"]; }
                }
            }
        }
    }
}
