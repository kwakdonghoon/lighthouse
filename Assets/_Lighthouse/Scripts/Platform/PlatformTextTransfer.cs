using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Lighthouse.Core;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace Lighthouse.Platform
{
    /// <summary>
    /// 저장 코드 복사·파일 내보내기·파일 열기의 플랫폼별 구현 (HTML 의 navigator.clipboard / Blob 다운로드 / input[type=file] 대응).
    ///  - 에디터: 저장·열기 대화상자(EditorUtility).
    ///  - Android: 파일은 앱 폴더(exports)에 남기고 공유 시트(Intent.ACTION_SEND, text/plain)로 보낸다. 열기는 선택 창이 없으므로(네이티브 플러그인 필요) "exports 의 가장 새 .txt → 클립보드의 LHS1 코드".
    ///  - iOS: 공유 시트(UIActivityViewController, Plugins/iOS/LhPlatform.mm). 열기는 Android 와 같다.
    ///  - 데스크톱: 문서 폴더\Lighthouse 에 쓰고 폴더를 연다. 열기는 위와 같은 폴더·exports·클립보드.
    /// 실기기에서 확인하지 않았다(출시 체크리스트). 파일 크기는 열 때 2 MB 로 제한한다.
    /// </summary>
    public sealed class PlatformTextTransfer : ITextTransfer
    {
        public const int MaxReadBytes = 2 * 1024 * 1024;
        static readonly Regex SafeName = new Regex("^[A-Za-z0-9._-]{1,64}$", RegexOptions.CultureInvariant);
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        readonly string exportDir;

        public PlatformTextTransfer(string exportDir) { this.exportDir = exportDir; }

        public void CopyText(string text) { GUIUtility.systemCopyBuffer = text ?? ""; }

        // ---------- 내보내기 ----------
        public string SaveTextFile(string fileName, string text)
        {
            try
            {
                string name = SafeName.IsMatch(fileName ?? "") ? fileName : "lighthouse-save.txt";
                text = text ?? "";
#if UNITY_EDITOR
                string chosen = EditorUtility.SaveFilePanel("저장 코드 파일", "", name, "txt");
                if (string.IsNullOrEmpty(chosen)) return null;
                File.WriteAllText(chosen, text, Utf8); return chosen;
#else
                Directory.CreateDirectory(exportDir);
                string path = Path.Combine(exportDir, name);
                File.WriteAllText(path, text, Utf8);
  #if UNITY_ANDROID
                ShareAndroid(name, text);
  #elif UNITY_IOS
                LhShareText(text, name);
  #else
                string docs = DesktopFolder();
                if (docs != null) { Directory.CreateDirectory(docs); string p2 = Path.Combine(docs, name); File.WriteAllText(p2, text, Utf8); path = p2; try { Application.OpenURL(new Uri(docs + Path.DirectorySeparatorChar).AbsoluteUri); } catch (Exception) { } }
  #endif
                return path;
#endif
            }
            catch (Exception e) { Debug.LogWarning("파일 내보내기 실패: " + e.Message); return null; }
        }

        // ---------- 가져오기(파일 열기) ----------
        public void PickTextFile(Action<string> onText)
        {
            string text = null;
            try
            {
#if UNITY_EDITOR
                string chosen = EditorUtility.OpenFilePanel("저장 코드 파일", "", "txt");
                if (!string.IsNullOrEmpty(chosen)) text = ReadCapped(chosen);
#else
                text = FindNewestCode();
#endif
            }
            catch (Exception e) { Debug.LogWarning("파일 열기 실패: " + e.Message); }
            onText?.Invoke(text);
        }

        /// <summary>내보낸 폴더들의 가장 새 .txt 중 LHS1 코드로 시작하는 것, 없으면 클립보드의 LHS1 코드. 없으면 null.</summary>
        public string FindNewestCode()
        {
            foreach (var dir in new[] { exportDir, DesktopFolder() })
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
                foreach (var f in new DirectoryInfo(dir).GetFiles("*.txt").OrderByDescending(x => x.LastWriteTimeUtc))
                {
                    string t = ReadCapped(f.FullName);
                    if (LooksLikeCode(t)) return t;
                }
            }
            string clip = GUIUtility.systemCopyBuffer;
            return LooksLikeCode(clip) ? clip.Trim() : null;
        }

        public static bool LooksLikeCode(string t) => t != null && t.TrimStart().StartsWith("LHS1.", StringComparison.Ordinal);

        static string ReadCapped(string path)
        {
            var fi = new FileInfo(path);
            if (!fi.Exists || fi.Length > MaxReadBytes) return null;
            return File.ReadAllText(path, Utf8).Trim();
        }

        static string DesktopFolder()
        {
#if UNITY_ANDROID || UNITY_IOS
            return null;
#else
            try { string d = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments); return string.IsNullOrEmpty(d) ? null : Path.Combine(d, "Lighthouse"); }
            catch (Exception) { return null; }
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        static void ShareAndroid(string subject, string text)
        {
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                {
                    try
                    {
                        using (var intentClass = new AndroidJavaClass("android.content.Intent"))
                        using (var intent = new AndroidJavaObject("android.content.Intent"))
                        {
                            intent.Call<AndroidJavaObject>("setAction", intentClass.GetStatic<string>("ACTION_SEND"));
                            intent.Call<AndroidJavaObject>("setType", "text/plain");
                            intent.Call<AndroidJavaObject>("putExtra", intentClass.GetStatic<string>("EXTRA_SUBJECT"), subject);
                            intent.Call<AndroidJavaObject>("putExtra", intentClass.GetStatic<string>("EXTRA_TEXT"), text);
                            using (var chooser = intentClass.CallStatic<AndroidJavaObject>("createChooser", intent, subject))
                                activity.Call("startActivity", chooser);
                        }
                    }
                    catch (Exception e) { Debug.LogWarning("공유 시트 실패: " + e.Message); }
                }));
            }
        }
#endif

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void LhShareText(string text, string name);
#endif
    }
}
