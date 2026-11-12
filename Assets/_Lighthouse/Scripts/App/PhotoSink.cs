using System;
using System.IO;
using UnityEngine;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace Lighthouse.App
{
    /// <summary>사진 저장 결과(원본 savePhoto 3165행의 네 갈래): Saved = 사진을 저장했어요 · Shared = 사진을 보냈어요 · Cancelled = 저장하지 않았어요 · Failed = 저장하지 못했어요.</summary>
    public enum PhotoOutcome { Saved, Shared, Cancelled, Failed }

    /// <summary>
    /// 사진 모드의 저장 경로(원본: 폰은 공유 시트, 아니면 파일 내려받기). 어디로도 보내지 않고 기기 안에서만 처리하며, 공유 시트는 사용자가 직접 고를 때만 뜬다.
    ///  - 에디터·데스크톱: 사진 폴더\Lighthouse(없으면 앱 폴더 photos)에 PNG 로 쓴다 → Saved.
    ///  - Android: Android 10 이상은 MediaStore 로 Pictures/Lighthouse 에 넣는다(권한 불필요) → Saved. 그 아래 버전·실패는 앱 폴더 photos 에 쓴다 → Saved(갤러리에는 안 보임).
    ///  - iOS: 공유 시트(UIActivityViewController, 이미지 저장 포함) → Shared. 취소를 알 수 없어 보낸 것으로 본다.
    /// 실기기에서 확인하지 않았다(출시 체크리스트).
    /// </summary>
    public static class PhotoSink
    {
        public static string LastPath;

        public static PhotoOutcome Save(byte[] png, string fileName, string appDir)
        {
            try
            {
                if (png == null || png.Length == 0) return PhotoOutcome.Failed;
                string dir = Path.Combine(appDir, "photos"); Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, fileName);
#if UNITY_ANDROID && !UNITY_EDITOR
                if (SaveAndroidGallery(png, fileName)) { LastPath = "MediaStore:Pictures/Lighthouse/" + fileName; return PhotoOutcome.Saved; }
                File.WriteAllBytes(path, png); LastPath = path; return PhotoOutcome.Saved;
#elif UNITY_IOS && !UNITY_EDITOR
                File.WriteAllBytes(path, png); LastPath = path; LhSharePhoto(path); return PhotoOutcome.Shared;
#else
                string pics = null;
                try { string d = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures); if (!string.IsNullOrEmpty(d) && !Application.isEditor) pics = Path.Combine(d, "Lighthouse"); } catch (Exception) { }
                if (pics != null) { try { Directory.CreateDirectory(pics); string p2 = Path.Combine(pics, fileName); File.WriteAllBytes(p2, png); LastPath = p2; return PhotoOutcome.Saved; } catch (Exception) { } }
                File.WriteAllBytes(path, png); LastPath = path; return PhotoOutcome.Saved;
#endif
            }
            catch (Exception e) { Debug.LogWarning("사진 저장 실패: " + e.Message); return PhotoOutcome.Failed; }
        }

        /// <summary>원본 파일 이름: lighthouse-YYYYMMDD-HHMM.png (3166행).</summary>
        public static string FileName(DateTime t) => "lighthouse-" + t.ToString("yyyyMMdd-HHmm") + ".png";

        /// <summary>원본 정수 배율: round(1080 / 화면 도트 폭), 1~8 (3168행).</summary>
        public static int Factor(int worldW) => Mathf.Clamp(Mathf.RoundToInt(1080f / Mathf.Max(1, worldW)), 1, 8);

#if UNITY_ANDROID && !UNITY_EDITOR
        static bool SaveAndroidGallery(byte[] png, string name)
        {
            try
            {
                using (var ver = new AndroidJavaClass("android.os.Build$VERSION"))
                {
                    if (ver.GetStatic<int>("SDK_INT") < 29) return false;           // 그 아래는 저장 권한이 필요해 앱 폴더로 대신한다
                }
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var resolver = activity.Call<AndroidJavaObject>("getContentResolver"))
                using (var values = new AndroidJavaObject("android.content.ContentValues"))
                using (var media = new AndroidJavaClass("android.provider.MediaStore$Images$Media"))
                {
                    values.Call("put", "_display_name", name); values.Call("put", "mime_type", "image/png"); values.Call("put", "relative_path", "Pictures/Lighthouse");
                    using (var tableUri = media.GetStatic<AndroidJavaObject>("EXTERNAL_CONTENT_URI"))
                    using (var item = resolver.Call<AndroidJavaObject>("insert", tableUri, values))
                    {
                        if (item == null) return false;
                        using (var os = resolver.Call<AndroidJavaObject>("openOutputStream", item)) { os.Call("write", (sbyte[])(object)png); os.Call("close"); }
                        return true;
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning("MediaStore save failed: " + e.Message); return false; }
        }
#endif

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void LhSharePhoto(string path);
#endif
    }
}
