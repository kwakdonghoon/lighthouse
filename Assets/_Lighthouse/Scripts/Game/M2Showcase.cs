using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Lighthouse.Core;
using Lighthouse.Render;
using UnityEngine;

namespace Lighthouse.Game
{
    /// <summary>
    /// M2 한 장 화면: 낮/밤(자동 순환 또는 슬라이더)·계절 4·섬/마을/탑 외형·단계 6·꾸미기 풀세트·구름·배 9종 중 8척을 한 화면에 보인다.
    /// 플레이 모드에서는 화면 왼쪽 위 IMGUI 단추로 바꾼다 (글꼴 문제로 영문 표기). 에디터 배치 캡처는 M2Tools 가 같은 Build 를 쓴다.
    /// </summary>
    public sealed class M2Showcase : MonoBehaviour
    {
        public Material SpriteMaterial;
        public int Stage = 6;
        public string Season = "summer", TowerSkin = "classic", IsleSkin = "classic", DecorSkin = "classic";
        public bool Decor = true, Extras = true, AutoCycle = false;
        [Range(0f, 1f)] public float Dusk = 0f;

        public WorldView View { get; private set; }
        public SceneSprites Sprites { get; private set; }
        public double LastBakeMs { get; private set; }
        public LogicalScreen Screen { get; private set; }
        bool dirty = true;
        float shown = -1;

        void Start() { View = gameObject.GetComponent<WorldView>() ?? gameObject.AddComponent<WorldView>(); View.SpriteMaterial = SpriteMaterial; }

        /// <summary>HTML duskAt 빠른 모드: 한 바퀴 cycle 초 (낮 40% → 해질녘 10% → 밤 40% → 새벽 10%).</summary>
        public static double DuskFast(double seconds, double cycle = 600)
        {
            double p = (seconds % cycle) / cycle; Func2 sm = t => t * t * (3 - 2 * t);
            return p < 0.4 ? 0 : p < 0.5 ? sm((p - 0.4) / 0.1) : p < 0.9 ? 1 : 1 - sm((p - 0.9) / 0.1);
        }
        delegate double Func2(double t);

        /// <summary>현재 설정으로 굽고 올린다. 논리 크기는 화면에서 구한다(LogicalScreen). devW/devH 가 0 이면 Screen 값.</summary>
        public void Rebuild(int devW = 0, int devH = 0)
        {
            if (View == null) Start();
            var sw = Stopwatch.StartNew();
            Screen = new LogicalScreen(devW > 0 ? devW : UnityEngine.Screen.width, devH > 0 ? devH : UnityEngine.Screen.height);
            var put = Decor ? DecorLayout.DevFill() : null;
            Sprites = SceneSprites.Build(Screen.W, Screen.H, Season, Stage, IsleSkin, DecorSkin, TowerSkin, gardenLv: 6, slotLv: 32, put: put);
            var ops = Extras ? SceneExtras.OpsWithExtras(Sprites) : SceneComposer.Ops(Sprites);
            View.Build(Screen.W, Screen.H, ops, Dusk);
            View.ShowOnScreen(Screen);
            LastBakeMs = sw.Elapsed.TotalMilliseconds; dirty = false; shown = Dusk;
        }

        void Update()
        {
            if (AutoCycle) Dusk = (float)DuskFast(Time.realtimeSinceStartupAsDouble * 20);      // 20배속으로 한 바퀴 30초
            if (dirty) Rebuild();
            else if (!Mathf.Approximately(shown, Dusk)) { View.SetDusk(Dusk); shown = Dusk; }
        }

        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(6, 6, 330, 400));
            GUILayout.BeginHorizontal();
            GUILayout.Label("Dusk " + Dusk.ToString("F2"), GUILayout.Width(80));
            float d = GUILayout.HorizontalSlider(Dusk, 0, 1, GUILayout.Width(150));
            if (!Mathf.Approximately(d, Dusk)) { Dusk = d; AutoCycle = false; }
            AutoCycle = GUILayout.Toggle(AutoCycle, "auto");
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            foreach (var s in Palettes.Seasons) if (GUILayout.Button(s)) { Season = s; dirty = true; }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            for (int i = 1; i <= 6; i++) if (GUILayout.Button(i.ToString(), GUILayout.Width(36))) { Stage = i; dirty = true; }
            Decor = GUILayout.Toggle(Decor, "decor"); Extras = GUILayout.Toggle(Extras, "ships"); if (GUI.changed) dirty = true;
            GUILayout.EndHorizontal();
            if (GUILayout.Button("tower skin: " + TowerSkin)) { TowerSkin = Cycle(Palettes.TowerSkins.Select(s => s.Id).ToList(), TowerSkin); dirty = true; }
            if (GUILayout.Button("isle skin: " + IsleSkin)) { IsleSkin = Cycle(Palettes.IsleSkinSeason.Keys.ToList(), IsleSkin); dirty = true; }
            if (GUILayout.Button("decor skin: " + DecorSkin)) { DecorSkin = Cycle(new List<string> { "classic", "seaside", "festival", "dev" }, DecorSkin); dirty = true; }
            GUILayout.Label("bake " + LastBakeMs.ToString("F1") + " ms, sprites " + (View != null ? View.SpriteCount : 0));
            GUILayout.EndArea();
        }

        static string Cycle(List<string> l, string cur) => l[(l.IndexOf(cur) + 1) % l.Count];
    }
}
