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

    /// <summary>
    /// 폴더에 저장(순수 System.IO). 쓰기는 "임시 파일에 끝까지 쓰고(디스크까지 밀어 넣고) → 한 번에 교체"다.
    /// 교체는 덮어쓰며 옮기기(윈도우 MoveFileEx REPLACE_EXISTING·POSIX rename)라서 본 파일이 "없는 순간"이 없다 — 어느 시점에 끊겨도 본 파일은 옛 본 아니면 새 본이다.
    /// (이전 방식은 지우고 옮기는 두 단계라 그 사이에 끊기면 본 파일이 사라졌다.)
    /// </summary>
    public sealed class FileStorage : IStorage
    {
        readonly string dir;
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        /// <summary>시험용: 임시 파일을 다 쓴 뒤·교체 직전에 불린다. 여기서 예외를 던지면 "그 순간 강제 종료"와 같다(본 파일은 그대로, 임시 파일만 남는다).</summary>
        public Action<string> BeforeCommit;

        public FileStorage(string dir)
        {
            this.dir = dir; Directory.CreateDirectory(dir);
            CleanStaleTemp();
        }
        string P(string n) => Path.Combine(dir, n);
        public string Dir => dir;
        public bool Exists(string name) => File.Exists(P(name));
        public string ReadText(string name) => File.ReadAllText(P(name), Utf8);

        public void WriteTextAtomic(string name, string text)
        {
            string tmp = P(name + ".tmp"), dst = P(name);
            byte[] bytes = Utf8.GetBytes(text);
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true);                                  // OS 캐시가 아니라 디스크까지 (전원이 나가도 임시 파일은 온전하게)
            }
            BeforeCommit?.Invoke(name);
            if (File.Exists(dst))
            {
                // 1순위: 덮어쓰며 옮기기(File.Move(원본, 대상, true) = 윈도우 MoveFileEx REPLACE_EXISTING · POSIX rename 한 번 → 대상이 "없는 순간"이 없다).
                //        실제 강제 종료 시험에서 File.Replace(윈도우 ReplaceFile)는 본을 ~RF*.TMP 로 옮긴 뒤 새 파일을 옮기는 두 단계라 그 사이에 끊기면 본 파일이 없었다(백업으로는 복원됨).
                // 2순위: 이 런타임에 위 오버로드가 없으면(Unity 의 Mono 판에 따라) File.Replace.  3순위: 덮어쓰기 복사.
                if (MoveOverwrite != null)
                {
                    try { MoveOverwrite.Invoke(null, new object[] { tmp, dst, true }); return; }
                    catch (System.Reflection.TargetInvocationException e) when (e.InnerException is IOException || e.InnerException is UnauthorizedAccessException || e.InnerException is PlatformNotSupportedException) { }
                }
                try { File.Replace(tmp, dst, null); }
                catch (Exception e) when (e is PlatformNotSupportedException || e is IOException || e is UnauthorizedAccessException)
                {
                    // 교체가 안 되는 파일 시스템: 덮어쓰기 복사로 대신한다(원자적이지 않지만 호출하는 쪽이 직전 본을 백업으로 갖고 있다)
                    File.Copy(tmp, dst, true); File.Delete(tmp);
                }
            }
            else File.Move(tmp, dst);
        }

        static readonly System.Reflection.MethodInfo MoveOverwrite = typeof(File).GetMethod("Move", new[] { typeof(string), typeof(string), typeof(bool) });

        public void Delete(string name) { if (File.Exists(P(name))) File.Delete(P(name)); }

        /// <summary>끊긴 쓰기가 남긴 임시 파일(*.tmp)을 치운다. 임시 파일은 어떤 경우에도 불러오기 후보가 아니다(끝까지 쓰였는지 알 수 없다).</summary>
        void CleanStaleTemp()
        {
            try { foreach (var f in Directory.GetFiles(dir, "*.tmp")) { try { File.Delete(f); } catch (Exception) { } } }
            catch (Exception) { }
        }
    }

    /// <summary>불러오기가 어떻게 끝났는지(플레이어에게 알릴 일이 있는지).</summary>
    public enum LoadNotice
    {
        None,               // 본 파일을 그대로 읽었거나 처음 시작
        RestoredBackup,     // 본 파일을 못 읽어 직전 본(save.bak.json)으로 이어서 함
        StartedFresh,       // 파일은 있었지만 둘 다 못 읽어 새로 시작함(손상 파일은 save.corrupt.json 에 남김)
    }

    /// <summary>
    /// 저장 불러오기·쓰기 (설계서 4-2): save.json(JSON, 원본과 같은 필드 이름·구조) + 직전 본 save.bak.json 한 벌.
    /// 불러올 때는 본 파일 → 백업 → 새 저장 순으로 시도하고, 읽은 JSON 은 항상 SaveMigrator.Migrate 를 거친다.
    /// "읽을 수 있다"는 파싱이 되고, 최상위가 객체이며, 저장이 늘 쓰는 칸(ver·stage)이 있는 것을 뜻한다(빈 파일·빈 객체·배열은 손상).
    /// 읽지 못한 파일은 지우지 않고 save.corrupt.json 으로 한 벌 남긴다(나중에 사람이 살릴 수 있게).
    /// 시작 시각(startAt)이 0 이면 지금으로 채운다(원본 1620행, 기념일 편지 기준).
    /// </summary>
    public sealed class SaveStore
    {
        public const string MainName = "save.json", BackupName = "save.bak.json", CorruptName = "save.corrupt.json", PreImportName = "save.pre-import.json";
        readonly IStorage io; readonly SaveMigrator mig; readonly IClock clock;

        /// <summary>마지막 Load 가 어디서 읽었는지: "main" | "backup" | "new".</summary>
        public string LoadedFrom { get; private set; } = "new";
        /// <summary>마지막 Load 의 결과 알림(복원·새로 시작). 화면은 이것으로 안내한다.</summary>
        public LoadNotice Notice { get; private set; } = LoadNotice.None;

        public SaveStore(IStorage storage, SaveMigrator migrator, IClock clock) { io = storage; mig = migrator; this.clock = clock; }

        /// <summary>저장 파일 내용으로 쓸 수 있는지(파싱 + 최상위 객체 + 저장이 늘 쓰는 칸). 아니면 예외.</summary>
        static JObject ParseSave(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) throw new InvalidDataException("empty");
            var o = Js.Parse(text) as JObject;
            if (o == null || (o["ver"] == null && o["stage"] == null && o["stageV"] == null)) throw new InvalidDataException("not a save");
            return o;
        }

        public GameState Load()
        {
            GameState st = null; bool anyFile = false, mainBad = false, kept = false; Notice = LoadNotice.None;
            foreach (var pair in new[] { new[] { MainName, "main" }, new[] { BackupName, "backup" } })
            {
                string raw = null;
                try
                {
                    if (!io.Exists(pair[0])) { if (pair[1] == "main") mainBad = true; continue; }       // 본 파일이 없는데 백업이 있으면 끊긴 흔적이니 복원으로 친다
                    anyFile = true;
                    raw = io.ReadText(pair[0]);
                    st = GameState.FromJson(mig.Migrate(ParseSave(raw)));
                    LoadedFrom = pair[1]; break;
                }
                catch (Exception)
                {
                    st = null; if (pair[1] == "main") mainBad = true;
                    if (raw != null && !kept) { kept = true; try { io.WriteTextAtomic(CorruptName, raw); } catch (Exception) { } }      // 깨진 파일은 건너뛰고 다음 후보로 — 지우지는 않고 한 벌 남긴다
                }
            }
            if (st == null) { st = GameState.FromJson(mig.Defaults()); LoadedFrom = "new"; if (anyFile) Notice = LoadNotice.StartedFresh; }
            else if (LoadedFrom == "backup" && mainBad) Notice = LoadNotice.RestoredBackup;
            if (!(st.StartAt > 0)) st.StartAt = clock.NowMs;
            return st;
        }

        public void Save(GameState s)
        {
            string text = Js.Stringify(s.ToJson());
            if (io.Exists(MainName))
            {
                try { string old = io.ReadText(MainName); ParseSave(old); io.WriteTextAtomic(BackupName, old); }      // 읽을 수 있는 직전 본만 백업으로
                catch (Exception) { }
            }
            io.WriteTextAtomic(MainName, text);
        }

        /// <summary>가져오기로 덮어쓰기 직전: 지금 저장(읽을 수 있을 때만)을 save.pre-import.json 으로 한 벌 남긴다. 실패해도 가져오기는 막지 않는다.</summary>
        public void KeepBeforeImport()
        {
            try { if (io.Exists(MainName)) { string old = io.ReadText(MainName); ParseSave(old); io.WriteTextAtomic(PreImportName, old); } }
            catch (Exception) { }
        }
    }
}
