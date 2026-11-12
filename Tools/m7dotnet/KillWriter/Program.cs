using System;
using System.IO;
using Lighthouse.Core;
using Lighthouse.Data;

// 사용: KillWriter <저장 폴더> <Data 폴더> — 저장을 끝없이 쓴다. 한 번 쓸 때마다 표준 출력에 "w <번호>" 한 줄(번호 = guided 값).
static class Program
{
    static int Main(string[] a)
    {
        var T = DataLoader.Load(new FolderDataSource(a[1]));
        var clock = new SystemClock(); var mig = new SaveMigrator(T, clock);
        var store = new SaveStore(new FileStorage(a[0]), mig, clock);
        var st = store.Load(); long i = (long)st.Guided;
        while (true)
        {
            i++; st.Guided = i; st.Gifts["shell"] = i;
            store.Save(st);
            Console.Out.WriteLine("w " + i); Console.Out.Flush();
        }
    }
}
