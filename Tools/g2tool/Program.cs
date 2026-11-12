using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Lighthouse.Core;
using Lighthouse.Data;
using Lighthouse.Render;

namespace Lighthouse.G2Tool
{
    public static partial class Program
    {
        static string Root()
        {
            for (var d = new DirectoryInfo(Directory.GetCurrentDirectory()); d != null; d = d.Parent)
                if (Directory.Exists(Path.Combine(d.FullName, "Assets", "_Lighthouse", "Resources", "Data"))) return d.FullName;
            throw new DirectoryNotFoundException();
        }
        public static GameTables Tables() => DataLoader.Load(new FolderDataSource(Path.Combine(Root(), "Assets", "_Lighthouse", "Resources", "Data")));

        public static int Main(string[] a)
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            string cmd = a.Length > 0 ? a[0] : "";
            ApplyPatch();
            if (cmd == "lv") return Lv();
            return Other(cmd, a);
        }

        static readonly (string name, double ships, int days, int ve)[] Profiles =
            { ("A", 468, 120, 1), ("B", 192, 120, 1), ("C", 48, 120, 1), ("Z", 0, 365, 1), ("D", 48, 365, 3) };

        static int Lv()
        {
            var T = Tables(); var clock = new FixedClock(1762831800000L, -540);
            foreach (var p in Profiles)
            {
                var first = new Dictionary<string, int>(); int days = p.days;
                var r = Simulator.Run(T, clock, p.ships, days, 1, p.ve, (d, ctx) =>
                {
                    foreach (var tr in T.Tracks) { int lv = ctx.LvlOf(tr.Id); for (int L = 1; L <= lv; L++) { string k = tr.Id + L; if (!first.ContainsKey(k)) first[k] = d; } }
                    foreach (var g in new[] { 400, 1000, 1500, 2500, 5000, 8000, 15000, 20000, 25000, 30000, 40000, 50000, 60000, 80000, 100000 }) if (ctx.S.Guided >= g) { string k = "guided" + g; if (!first.ContainsKey(k)) first[k] = d; }
                    for (int st = 1; st <= 6; st++) if (ctx.Stage >= st) { string k = "stage" + st; if (!first.ContainsKey(k)) first[k] = d; }
                });
                Console.WriteLine("== " + p.name + " ships/day " + p.ships + " days " + days + " ve " + p.ve + " finalStage " + r.FinalStage + " guided " + r.FinalGuided);
                foreach (var g in new[] { "garden", "port", "lens" })
                {
                    var sb = new StringBuilder(g + ": ");
                    for (int L = 5; L <= 30; L++) { string k = g + L; if (first.TryGetValue(k, out int d)) sb.Append("L" + L + "=" + d + " "); }
                    Console.WriteLine(sb);
                }
                var sg = new StringBuilder("guided: "); foreach (var g in new[] { 400, 1000, 1500, 2500, 5000, 8000, 15000, 20000, 25000, 30000, 40000, 50000, 60000, 80000, 100000 }) if (first.TryGetValue("guided" + g, out int d)) sg.Append(g + "=" + d + " "); Console.WriteLine(sg);
                var ss = new StringBuilder("stage: "); for (int st = 2; st <= 6; st++) if (first.TryGetValue("stage" + st, out int d)) ss.Append(st + "=" + d + " "); Console.WriteLine(ss);
            }
            return 0;
        }

        static partial void OtherCmd(string cmd, string[] a, ref int rc, ref bool handled);
        static int Other(string cmd, string[] a)
        {
            int rc = 1; bool handled = false; OtherCmd(cmd, a, ref rc, ref handled);
            if (!handled) Console.WriteLine("commands: lv");
            return rc;
        }
    }
}
