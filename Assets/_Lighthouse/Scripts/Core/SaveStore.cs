using System;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Lighthouse.Core
{
    /// <summary>저장 파일 한 곳. 실제 위치(persistentDataPath)는 Game/Platform 이 FileStorage 에 넘긴다. 시험은 메모리 구현을 쓴다.</summary>
    public interface IStorage
    {
        bool Exists(string name);
        string ReadText(string name);
        /// <summary>임시 파일에 쓰고 교체한다(쓰다가 끊겨도 이전 본이 남는다).</summary>
        void WriteTextAtomic(string name, string text);
        void Delete(string name);
    }

    /// <summary>폴더에 저장(순수 System.IO). 쓰기는 임시 파일 → 교체.</summary>
    public sealed class FileStorage : IStorage
    {
        readonly string dir;
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        public FileStorage(string dir) { this.dir = dir; Directory.CreateDirectory(dir); }
        string P(string n) => Path.Combine(dir, n);
        public bool Exists(string name) => File.Exists(P(name));
        public string ReadText(string name) => File.ReadAllText(P(name), Utf8);
        public void WriteTextAtomic(string name, string text)
        {
            string tmp = P(name + ".tmp"), dst = P(name);
            File.WriteAllText(tmp, text, Utf8);
            if (File.Exists(dst)) File.Delete(dst);
            File.Move(tmp, dst);
        }
        public void Delete(string name) { if (File.Exists(P(name))) File.Delete(P(name)); }
    }

    /// <summary>
    /// 저장 불러오기·쓰기 (설계서 4-2): save.json(JSON, 원본과 같은 필드 이름·구조) + 직전 본 save.bak.json 한 벌.
    /// 불러올 때는 본 파일 → 백업 → 새 저장 순으로 시도하고, 읽은 JSON 은 항상 SaveMigrator.Migrate 를 거친다.
    /// 시작 시각(startAt)이 0 이면 지금으로 채운다(원본 1620행, 기념일 편지 기준).
    /// </summary>
    public sealed class SaveStore
    {
        public const string MainName = "save.json", BackupName = "save.bak.json";
        readonly IStorage io; readonly SaveMigrator mig; readonly IClock clock;

        /// <summary>마지막 Load 가 어디서 읽었는지: "main" | "backup" | "new".</summary>
        public string LoadedFrom { get; private set; } = "new";

        public SaveStore(IStorage storage, SaveMigrator migrator, IClock clock) { io = storage; mig = migrator; this.clock = clock; }

        public GameState Load()
        {
            GameState st = null;
            foreach (var pair in new[] { new[] { MainName, "main" }, new[] { BackupName, "backup" } })
            {
                try
                {
                    if (!io.Exists(pair[0])) continue;
                    st = GameState.FromJson(mig.Migrate(Js.Parse(io.ReadText(pair[0]))));
                    LoadedFrom = pair[1]; break;
                }
                catch (Exception) { st = null; }      // 깨진 파일은 건너뛰고 다음 후보로
            }
            if (st == null) { st = GameState.FromJson(mig.Defaults()); LoadedFrom = "new"; }
            if (!(st.StartAt > 0)) st.StartAt = clock.NowMs;
            return st;
        }

        public void Save(GameState s)
        {
            string text = Js.Stringify(s.ToJson());
            if (io.Exists(MainName))
            {
                try { string old = io.ReadText(MainName); Js.Parse(old); io.WriteTextAtomic(BackupName, old); }      // 읽을 수 있는 직전 본만 백업으로
                catch (Exception) { }
            }
            io.WriteTextAtomic(MainName, text);
        }
    }
}
