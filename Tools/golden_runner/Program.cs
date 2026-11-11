using System;
using System.IO;
using System.Linq;
using Lighthouse.Tests.M2;
using Lighthouse.Render.Verify;

// M2 렌더 골든 대조를 Unity 없이 dotnet 으로 돌린다. 사용: dotnet run --project Tools/golden_runner -- [골든 폴더] [세트 이름...]
static class Program
{
    static int Main(string[] args)
    {
        string dir = args.Length > 0 ? args[0] : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../golden/m2/out"));
        M2Checks.Dir = dir;
        var sets = args.Skip(1).ToArray();
        var res = M2Checks.RunAll(sets);
        int fail = 0;
        foreach (var r in res) if (!r.Ok) { fail++; Console.WriteLine(r); }
        foreach (var g in res.GroupBy(r => r.Set))
            Console.WriteLine("{0,-18} items={1,4} fail={2,3} diffPx={3,7} overTol={4,5} maxD={5}", g.Key, g.Count(), g.Count(r => !r.Ok), g.Sum(r => r.ExactDiff), g.Sum(r => r.OverTol), g.Max(r => r.MaxDelta));
        Console.WriteLine(fail == 0 ? "ALL OK" : fail + " FAIL");
        return fail == 0 ? 0 : 1;
    }
}
