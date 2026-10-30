using System.Collections.Generic;
using DuskYard.Diagnostics;
using DuskYard.Player;
using DuskYard.Target;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace DuskYard.EditorTools
{
    /// <summary>
    /// Scene layout: ~31 x 31 m walled yard that opens south onto the road. Brick walls W/N/E, a house in the NE
    /// corner, a lean-to with hay on the W side, a low fence + gate on the S side. The training dummy stands in the
    /// middle with a brazier behind it (dynamic rim light); the hero spawns south of it facing north.
    /// </summary>
    public static partial class DuskYardBuilder
    {
        static readonly Vector3 DummyPos = new Vector3(0f, 0f, 3f);
        static readonly Vector3 PlayerSpawn = new Vector3(0.4f, 0f, -2.6f);

        public static void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var envRoot = new GameObject("--- Environment").transform;
            var lightRoot = new GameObject("--- Lighting").transform;
            var volRoot = new GameObject("--- PostProcess").transform;
            var gameRoot = new GameObject("--- Gameplay").transform;
            var sysRoot = new GameObject("--- Systems").transform;

            var stressTargets = new List<Collider>();
            BuildEnvironment(envRoot, stressTargets);

            // ---------------------------------------------------------------- gameplay
            var dummyGo = Place("Characters/PF_TrainingDummy", gameRoot, DummyPos, 0f);
            var player = Place("Characters/PF_Player", gameRoot, PlayerSpawn, 0f);
            stressTargets.Insert(0, dummyGo.transform.Find("TiltPivot/BodyCollider").GetComponent<Collider>());
            stressTargets.Insert(1, dummyGo.transform.Find("TiltPivot/HeadCollider").GetComponent<Collider>());

            // ---------------------------------------------------------------- camera
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            camGo.transform.SetParent(gameRoot, false);
            camGo.transform.position = PlayerSpawn + new Vector3(0.5f, 1.8f, -3.5f);
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.08f;
            cam.farClipPlane = 400f;
            var hdCam = camGo.AddComponent<HDAdditionalCameraData>();
            hdCam.antialiasing = HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            hdCam.SMAAQuality = HDAdditionalCameraData.SMAAQualityLevel.High;
            var rig = camGo.AddComponent<ThirdPersonCamera>();
            Wire(rig, ("target", player.transform), ("input", player.GetComponent<PlayerInputReader>()), ("focusTarget", dummyGo.transform),
                ("collisionMask", (LayerMask)(1 << 0)));
            Wire(player.GetComponent<ThirdPersonController>(), ("cameraTransform", camGo.transform));

            // ---------------------------------------------------------------- lighting + volumes
            var profile = CreateVolumeProfile();
            BuildLighting(lightRoot, profile);
            var vol = new GameObject("GlobalVolume");
            vol.transform.SetParent(volRoot, false);
            var v = vol.AddComponent<Volume>();
            v.isGlobal = true;
            v.priority = 0;
            v.sharedProfile = profile;

            // ---------------------------------------------------------------- systems
            Place("Systems/PF_CombatFX", sysRoot, Vector3.zero, 0f);
            var diag = Place("Systems/PF_Diagnostics", sysRoot, Vector3.zero, 0f);
            var anchors = new GameObject("ShowcaseCameraAnchors").transform;
            anchors.SetParent(diag.transform, false);
            Transform Anchor(string n, Vector3 pos, Vector3 look)
            {
                var t = new GameObject(n).transform;
                t.SetParent(anchors, false);
                t.position = pos;
                t.rotation = Quaternion.LookRotation(look - pos);
                return t;
            }
            var shots = new[]
            {
                Anchor("Overview", new Vector3(-10.5f, 6.5f, -17f), new Vector3(2f, 0.8f, 5f)),
                Anchor("Lighting_KeyFillRim", new Vector3(3.2f, 1.7f, -1.6f), new Vector3(0f, 1.25f, 3f)),
                Anchor("SG_Preset_StrawAndSigil", new Vector3(0.55f, 1.45f, 1.35f), new Vector3(0f, 1.2f, 2.85f)),
                Anchor("SG_Preset_Waystone", new Vector3(4.6f, 1.7f, -4.8f), new Vector3(6.5f, 1.4f, -2f)),
                Anchor("WetGround_Reflections", new Vector3(-1.2f, 0.55f, -6.5f), new Vector3(0.5f, 1.0f, 4f)),
            };
            Wire(diag.GetComponent<PerfHarness>(),
                ("input", player.GetComponent<PlayerInputReader>()), ("player", player.GetComponent<ThirdPersonController>()),
                ("cameraRig", rig), ("attack", player.GetComponent<Combat.MeleeAttack>()), ("dummy", dummyGo.GetComponent<TrainingDummy>()),
                ("hud", diag.GetComponent<DebugHud>()), ("stressTargets", stressTargets.ToArray()), ("showcaseCameraAnchors", shots),
                ("stressMask", (LayerMask)(1 << 0 | 1 << LayerHittable)));

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            ConfigurePlayerSettings();
            Debug.Log("[DuskYardBuilder] scene built: " + ScenePath);
        }

        static GameObject Place(string prefab, Transform parent, Vector3 pos, float yaw, float scale = 1f)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>($"{PrefabDir}/{prefab}.prefab"), parent);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            go.transform.localScale = Vector3.one * scale;
            return go;
        }

        static GameObject Block(string name, Transform parent, Vector3 pos, Vector3 size, Material mat, Vector3 euler = default, List<Collider> stress = null)
        {
            var go = Prim(PrimitiveType.Cube, name, parent, pos, size, mat, euler, collider: true);
            GameObjectUtility.SetStaticEditorFlags(go, EnvStatic);
            if (stress != null) stress.Add(go.GetComponent<Collider>());
            return go;
        }

        static void BuildEnvironment(Transform root, List<Collider> stress)
        {
            var bricks = Env("M_Bricks");
            var bricksDark = Env("M_BricksDark");
            var wood = Env("M_WoodDark");
            var roof = Env("M_RoofShingles");

            // ground
            var ground = Prim(PrimitiveType.Plane, "Ground", root, Vector3.zero, new Vector3(8f, 1f, 8f), Env("M_Cobblestone"), collider: true);
            GameObjectUtility.SetStaticEditorFlags(ground, EnvStatic);
            var so = new SerializedObject(ground.GetComponent<MeshRenderer>());
            so.FindProperty("m_ScaleInLightmap").floatValue = 0.35f;
            so.ApplyModifiedPropertiesWithoutUndo();

            // walls
            var walls = new GameObject("Walls").transform;
            walls.SetParent(root, false);
            Block("Wall_North", walls, new Vector3(0f, 2.25f, 15.4f), new Vector3(31.6f, 4.5f, 0.8f), bricks, stress: stress);
            Block("Wall_West", walls, new Vector3(-15.4f, 2.25f, 0f), new Vector3(0.8f, 4.5f, 31.6f), bricks, stress: stress);
            Block("Wall_East", walls, new Vector3(15.4f, 2.25f, 0f), new Vector3(0.8f, 4.5f, 31.6f), bricks, stress: stress);
            Block("Cap_North", walls, new Vector3(0f, 4.58f, 15.4f), new Vector3(32f, 0.16f, 1.0f), bricksDark);
            Block("Cap_West", walls, new Vector3(-15.4f, 4.58f, 0f), new Vector3(1.0f, 0.16f, 32f), bricksDark);
            Block("Cap_East", walls, new Vector3(15.4f, 4.58f, 0f), new Vector3(1.0f, 0.16f, 32f), bricksDark);
            for (int i = -2; i <= 2; i++)
            {
                if (i == 2) continue; // house corner
                Block($"Buttress_N{i}", walls, new Vector3(i * 6f, 1.6f, 14.75f), new Vector3(0.7f, 3.2f, 0.6f), bricksDark);
            }
            for (int i = -2; i <= 2; i++)
                Block($"Buttress_W{i}", walls, new Vector3(-14.75f, 1.6f, i * 6f), new Vector3(0.6f, 3.2f, 0.7f), bricksDark);

            // south: low fence and gate pillars, the road continues south
            var fence = new GameObject("Fence_South").transform;
            fence.SetParent(root, false);
            Block("GatePillar_W", fence, new Vector3(-2.9f, 1.4f, -15.4f), new Vector3(0.8f, 2.8f, 0.8f), bricksDark, stress: stress);
            Block("GatePillar_E", fence, new Vector3(2.9f, 1.4f, -15.4f), new Vector3(0.8f, 2.8f, 0.8f), bricksDark);
            foreach (float side in new[] { -1f, 1f })
            {
                float x0 = side * 3.6f, x1 = side * 15.0f;
                int posts = 7;
                for (int p = 0; p <= posts; p++)
                {
                    float x = Mathf.Lerp(x0, x1, p / (float)posts);
                    Block($"Post_{(side < 0 ? "W" : "E")}{p}", fence, new Vector3(x, 0.55f, -15.4f), new Vector3(0.16f, 1.1f, 0.16f), wood);
                }
                float cx = (x0 + x1) * 0.5f, len = Mathf.Abs(x1 - x0);
                Block($"Rail_{(side < 0 ? "W" : "E")}_Low", fence, new Vector3(cx, 0.45f, -15.4f), new Vector3(len, 0.08f, 0.06f), wood);
                Block($"Rail_{(side < 0 ? "W" : "E")}_High", fence, new Vector3(cx, 0.9f, -15.4f), new Vector3(len, 0.08f, 0.06f), wood);
            }

            // house in the NE corner
            var house = new GameObject("House").transform;
            house.SetParent(root, false);
            Block("House_Body", house, new Vector3(10.6f, 3.0f, 11.0f), new Vector3(8.4f, 6.0f, 7.6f), bricks, stress: stress);
            Block("House_Roof_S", house, new Vector3(10.6f, 7.0f, 9.05f), new Vector3(9.0f, 0.2f, 4.7f), roof, new Vector3(-38f, 0f, 0f));
            Block("House_Roof_N", house, new Vector3(10.6f, 7.0f, 12.95f), new Vector3(9.0f, 0.2f, 4.7f), roof, new Vector3(38f, 0f, 0f));
            Block("House_Door", house, new Vector3(9.4f, 1.15f, 7.17f), new Vector3(1.3f, 2.3f, 0.1f), wood);
            Block("House_Window_A", house, new Vector3(12.4f, 3.6f, 7.17f), new Vector3(1.0f, 1.1f, 0.1f), wood);
            Block("House_Window_B", house, new Vector3(7.8f, 3.6f, 7.17f), new Vector3(1.0f, 1.1f, 0.1f), wood);
            Block("House_Step", house, new Vector3(9.4f, 0.1f, 6.8f), new Vector3(1.8f, 0.2f, 0.7f), bricksDark);

            // lean-to on the west wall
            var lean = new GameObject("LeanTo").transform;
            lean.SetParent(root, false);
            foreach (float z in new[] { 1.5f, 5.0f, 8.5f })
                Block($"LeanTo_Post_{z}", lean, new Vector3(-11.6f, 1.35f, z), new Vector3(0.18f, 2.7f, 0.18f), wood);
            Block("LeanTo_Beam", lean, new Vector3(-11.6f, 2.72f, 5f), new Vector3(0.2f, 0.2f, 7.6f), wood);
            Block("LeanTo_Roof", lean, new Vector3(-13.3f, 3.15f, 5f), new Vector3(3.8f, 0.12f, 8.2f), roof, new Vector3(0f, 0f, -14f));

            // props
            var props = new GameObject("Props").transform;
            props.SetParent(root, false);
            Place("Environment/PF_Well", props, new Vector3(-7f, 0f, 8.5f), 15f);
            var ws = Place("Environment/PF_Waystone", props, new Vector3(6.5f, 0f, -2f), -25f);
            stress.Add(ws.transform.Find("Stone").GetComponent<Collider>());
            Place("Environment/PF_WeaponRack", props, new Vector3(-14.55f, 0f, -4f), 90f);
            Place("Environment/PF_Chest", props, new Vector3(-13.9f, 0f, -7f), 95f);
            var crate = Place("Environment/PF_Crate", props, new Vector3(12.6f, 0f, -9.2f), 8f);
            stress.Add(crate.GetComponentInChildren<Collider>());
            Place("Environment/PF_Crate", props, new Vector3(13.5f, 0f, -8.2f), -14f);
            Place("Environment/PF_Crate", props, new Vector3(13.0f, 0.8f, -8.7f), 30f);
            Place("Environment/PF_Crate", props, new Vector3(-12.2f, 0f, 12.6f), 3f);
            Place("Environment/PF_Barrel", props, new Vector3(13.6f, 0f, -11.6f), 0f);
            Place("Environment/PF_Barrel", props, new Vector3(12.7f, 0f, -12.5f), 40f);
            Place("Environment/PF_Barrel", props, new Vector3(-9.0f, 0f, 13.8f), 0f);
            Place("Environment/PF_Barrel", props, new Vector3(4.6f, 0f, 14.1f), 0f);
            Place("Environment/PF_HayBale", props, new Vector3(-13.6f, 0f, 3.2f), 90f);
            Place("Environment/PF_HayBale", props, new Vector3(-13.6f, 0f, 4.4f), 92f);
            Place("Environment/PF_HayBale", props, new Vector3(-13.6f, 0.5f, 3.8f), 88f);
            Place("Environment/PF_HayBale", props, new Vector3(-12.9f, 0f, 7.2f), 70f);
            Place("Environment/PF_HayBale", props, new Vector3(-3.2f, 0f, 4.8f), 30f);
            Place("Environment/PF_Rocks", props, new Vector3(7.5f, 0f, -19f), 30f);
            Place("Environment/PF_Rocks", props, new Vector3(-8.5f, 0f, -20.5f), 200f, 0.8f);
            Place("Environment/PF_Rocks", props, new Vector3(-12.6f, 0f, -12.8f), 110f, 0.6f);

            var puddles = new GameObject("Puddles").transform;
            puddles.SetParent(root, false);
            Place("Environment/PF_Puddle_0", puddles, new Vector3(1.4f, 0f, 0.4f), 0f, 2.4f);
            Place("Environment/PF_Puddle_1", puddles, new Vector3(-3.4f, 0f, -4.2f), 50f, 3.2f);
            Place("Environment/PF_Puddle_2", puddles, new Vector3(4.2f, 0f, -7.4f), 120f, 2.0f);
            Place("Environment/PF_Puddle_0", puddles, new Vector3(-0.4f, 0f, -18.5f), 80f, 3.8f);
            Place("Environment/PF_Puddle_1", puddles, new Vector3(-1.8f, 0f, 6.2f), 200f, 1.5f);
            Place("Environment/PF_Puddle_2", puddles, new Vector3(-8.5f, 0f, 1.5f), 10f, 2.6f);
        }

        static void ConfigurePlayerSettings()
        {
            PlayerSettings.companyName = "DuskYard";
            PlayerSettings.productName = "DuskYard";
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.macRetinaSupport = true;
            PlayerSettings.usePlayerLog = true;
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Standalone, "com.duskyard.testscene");
        }
    }
}
