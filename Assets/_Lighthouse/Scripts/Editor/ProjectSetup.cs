using System.IO;
using Lighthouse.Game;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Lighthouse.EditorTools
{
    /// <summary>
    /// 프로젝트 설정을 코드로 고정한다 (재현 가능). 배치 모드: -executeMethod Lighthouse.EditorTools.ProjectSetup.RunAll
    /// 안드로이드 우선·iOS 동시, 세로 고정, Gamma, LIGHTHOUSE_DEV 심볼, URP 2D.
    /// </summary>
    public static class ProjectSetup
    {
        public const string ScenePath = "Assets/_Lighthouse/Scenes/M0Preview.unity";
        const string SettingsDir = "Assets/Settings";
        const string MatPath = "Assets/_Lighthouse/Art/Materials/SpriteUnlit.mat";

        [MenuItem("Lighthouse/Setup/Run All")]
        public static void RunAll()
        {
            ApplyPlayerSettings();
            SetupUrp();
            CreateMaterial();
            CreateScene();
            AssetDatabase.SaveAssets();
            Debug.Log("[Lighthouse] ProjectSetup.RunAll 완료");
        }

        static void ApplyPlayerSettings()
        {
            PlayerSettings.companyName = "EK Games";
            PlayerSettings.productName = "Lighthouse";
            PlayerSettings.colorSpace = ColorSpace.Gamma;                       // HTML 이 sRGB 값에 직접 알파 합성 (설계서 7-2)
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "kr.ekgames.lighthouse");
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, "kr.ekgames.lighthouse");
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel25;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
            PlayerSettings.iOS.targetOSVersionString = "15.0";
            foreach (var t in new[] { NamedBuildTarget.Android, NamedBuildTarget.iOS, NamedBuildTarget.Standalone })
                PlayerSettings.SetScriptingDefineSymbols(t, "LIGHTHOUSE_DEV");  // 출시 빌드에서는 빌드 스크립트가 뺀다 (M9)
            QualitySettings.vSyncCount = 0;
        }

        static void SetupUrp()
        {
            Directory.CreateDirectory(SettingsDir);
            var rendererPath = SettingsDir + "/Renderer2D.asset";
            var rpPath = SettingsDir + "/UniversalRP.asset";
            var rd = AssetDatabase.LoadAssetAtPath<Renderer2DData>(rendererPath);
            if (rd == null) { rd = ScriptableObject.CreateInstance<Renderer2DData>(); AssetDatabase.CreateAsset(rd, rendererPath); }
            var rp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(rpPath);
            if (rp == null) { rp = UniversalRenderPipelineAsset.Create(rd); AssetDatabase.CreateAsset(rp, rpPath); }
            GraphicsSettings.defaultRenderPipeline = rp;
            for (int i = 0; i < QualitySettings.names.Length; i++) { QualitySettings.SetQualityLevel(i, false); QualitySettings.renderPipeline = rp; }
            QualitySettings.SetQualityLevel(QualitySettings.names.Length > 1 ? 1 : 0, false);
            EditorUtility.SetDirty(rp); EditorUtility.SetDirty(rd);
        }

        static void CreateMaterial()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MatPath));
            if (AssetDatabase.LoadAssetAtPath<Material>(MatPath) != null) return;
            var sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            var m = new Material(sh) { name = "SpriteUnlit" };
            AssetDatabase.CreateAsset(m, MatPath);
        }

        static void CreateScene()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black; cam.orthographic = true; cam.cullingMask = 0;
            camGo.transform.position = new Vector3(0, 0, -10);
            var go = new GameObject("M0Preview");
            var pv = go.AddComponent<M0Preview>();
            pv.SpriteMaterial = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }
    }
}
