using System;
using System.IO;
using Lighthouse.App;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Lighthouse.EditorTools
{
    /// <summary>
    /// M4 도구(에디터 배치): 머티리얼(Resources/Materials: SpriteUnlit 복사본·LhPremul)과 메인 씬(Main.unity)을 만들고 빌드 설정에 넣는다.
    /// 실행: Unity -batchmode -executeMethod Lighthouse.EditorTools.M4Tools.Setup (Tools/unity_run_m4.ps1 -Steps setup)
    /// </summary>
    public static class M4Tools
    {
        public const string MainScene = "Assets/_Lighthouse/Scenes/Main.unity";
        const string MatDir = "Assets/_Lighthouse/Resources/Materials";

        public static void Setup()
        {
            try
            {
                Directory.CreateDirectory(MatDir);
                if (AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/SpriteUnlit.mat") == null)
                    if (!AssetDatabase.CopyAsset("Assets/_Lighthouse/Art/Materials/SpriteUnlit.mat", MatDir + "/SpriteUnlit.mat")) throw new Exception("SpriteUnlit 복사 실패");
                if (AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/LhPremul.mat") == null)
                {
                    var sh = Shader.Find("Lighthouse/SpritePremul"); if (sh == null) throw new Exception("셰이더 Lighthouse/SpritePremul 을 찾지 못함(컴파일 오류?)");
                    AssetDatabase.CreateAsset(new Material(sh) { name = "LhPremul" }, MatDir + "/LhPremul.mat");
                }
                AssetDatabase.SaveAssets();
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var camGo = new GameObject("Main Camera") { tag = "MainCamera" }; var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.04f, 0.07f, 0.13f); cam.orthographic = true; cam.cullingMask = 0; camGo.transform.position = new Vector3(0, 0, -10);
                var host = new GameObject("GameApp"); var app = host.AddComponent<GameApp>();
                app.SpriteMat = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/SpriteUnlit.mat"); app.PremulMat = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/LhPremul.mat");
                EditorSceneManager.SaveScene(scene, MainScene);
                var list = new System.Collections.Generic.List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(MainScene, true) };
                foreach (var s in EditorBuildSettings.scenes) if (s.path != MainScene) list.Add(new EditorBuildSettingsScene(s.path, s.enabled));
                EditorBuildSettings.scenes = list.ToArray();
                AssetDatabase.SaveAssets();
                Debug.Log("[M4] Setup 완료: 머티리얼 2개, 씬 " + MainScene);
            }
            catch (Exception e) { Debug.LogError("[M4] Setup 실패: " + e); EditorApplication.Exit(1); return; }
            EditorApplication.Exit(0);
        }
    }
}
