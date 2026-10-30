using System.IO;
using UnityEditor;
using UnityEngine;

namespace DuskYard.EditorTools
{
    /// <summary>
    /// Deterministic content pipeline for the test scene. Everything in Assets/_Project that is not hand-authored
    /// (procedural textures, materials, prefabs, the scene, lighting/volume assets) is produced here, so the whole
    /// project can be regenerated from code:
    ///
    ///   DuskYard/1. Generate Assets (textures, materials, prefabs)
    ///   DuskYard/2. Build Scene
    ///   DuskYard/3. Bake Lighting
    ///   DuskYard/4. Build Player (macOS + Windows)
    ///
    /// CLI: -executeMethod DuskYard.EditorTools.DuskYardBuilder.CliAll   (1 + 2 + 3)
    /// </summary>
    public static partial class DuskYardBuilder
    {
        public const string Root = "Assets/_Project";
        public const string TexDir = Root + "/Art/Textures/Generated";
        public const string ThirdPartyTex = Root + "/Art/ThirdParty/UnitySamples/Textures";
        public const string ThirdPartyModels = Root + "/Art/ThirdParty/UnitySamples/Models";
        public const string MatDir = Root + "/Art/Materials";
        public const string MeshDir = Root + "/Art/Meshes";
        public const string PrefabDir = Root + "/Prefabs";
        public const string DataDir = Root + "/Data";
        public const string SettingsDir = Root + "/Settings";
        public const string ShaderDir = Root + "/Shaders";
        public const string ScenePath = Root + "/Scenes/DuskYard.unity";

        public const int LayerPlayer = 8;
        public const int LayerHittable = 9;
        public const int LayerVFX = 10;

        [MenuItem("DuskYard/1. Generate Assets (textures, materials, prefabs)")]
        public static void GenerateAssets()
        {
            EnsureFolders();
            EnsureLayers();
            GenerateTextures();
            CreateMaterials();
            CreateMeshes();
            CreateVfxPrefabs();
            CreateSurfaceProfiles();
            CreatePrefabs();
            AssetDatabase.SaveAssets();
            Debug.Log("[DuskYardBuilder] assets generated");
        }

        [MenuItem("DuskYard/2. Build Scene")]
        public static void BuildSceneMenu() => BuildScene();

        [MenuItem("DuskYard/3. Bake Lighting")]
        public static void BakeLightingMenu() => BakeLighting();

        public static void CliAll()
        {
            GenerateAssets();
            BuildScene();
            BakeLighting();
            EditorApplication.Exit(ProjectValidator.Validate() ? 0 : 1);
        }

        public static void CliAssetsAndScene()
        {
            GenerateAssets();
            BuildScene();
            EditorApplication.Exit(ProjectValidator.Validate() ? 0 : 1);
        }

        public static void CliBake()
        {
            BakeLighting();
            EditorApplication.Exit(0);
        }

        static void EnsureFolders()
        {
            foreach (var d in new[]
            {
                TexDir, MatDir + "/Environment", MatDir + "/Characters", MatDir + "/VFX", MatDir + "/Decals", MatDir + "/ShaderGraphPresets",
                MeshDir, PrefabDir + "/Characters", PrefabDir + "/Environment", PrefabDir + "/VFX", PrefabDir + "/Systems",
                DataDir + "/SurfaceFX", SettingsDir + "/Volumes", SettingsDir + "/Lighting", Root + "/Scenes",
            })
                CreateFolderRecursive(d);
        }

        static void CreateFolderRecursive(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)!.Replace('\\', '/');
            CreateFolderRecursive(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static void EnsureLayers()
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");
            void Set(int idx, string name)
            {
                var p = layers.GetArrayElementAtIndex(idx);
                if (p.stringValue != name) p.stringValue = name;
            }
            Set(LayerPlayer, "Player");
            Set(LayerHittable, "Hittable");
            Set(LayerVFX, "VFX");
            tagManager.ApplyModifiedPropertiesWithoutUndo();
        }

        // -------------------------------------------------------------------------------- small helpers
        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a) return a;
            a = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(a, path);
            return a;
        }

        static T Load<T>(string path) where T : Object
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (!a) throw new System.Exception("Missing asset " + path);
            return a;
        }

        static void SetLayerRecursive(GameObject go, int layer)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }
    }
}
