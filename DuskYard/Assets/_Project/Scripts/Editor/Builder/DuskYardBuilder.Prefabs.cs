using DuskYard.Combat;
using DuskYard.Diagnostics;
using DuskYard.Environment;
using DuskYard.Player;
using DuskYard.Target;
using DuskYard.VFX;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace DuskYard.EditorTools
{
    /// <summary>Character, target, props and system prefabs assembled from primitives + the imported sample meshes.</summary>
    public static partial class DuskYardBuilder
    {
        const StaticEditorFlags EnvStatic = StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic |
                                            StaticEditorFlags.BatchingStatic | StaticEditorFlags.ReflectionProbeStatic;

        static Material Mat(string folder, string n) => Load<Material>($"{MatDir}/{folder}/{n}.mat");
        static Material Env(string n) => Mat("Environment", n);
        static Material Chr(string n) => Mat("Characters", n);
        static Material Sg(string n) => Mat("ShaderGraphPresets", n);

        // -------------------------------------------------------------------------------- meshes
        static void CreateMeshes()
        {
            for (int v = 0; v < 3; v++)
            {
                var mesh = new Mesh { name = $"SM_Puddle_{v}" };
                const int n = 48;
                var verts = new Vector3[n + 1];
                var uvs = new Vector2[n + 1];
                var tris = new int[n * 3];
                verts[0] = Vector3.zero;
                uvs[0] = new Vector2(0.5f, 0.5f);
                for (int i = 0; i < n; i++)
                {
                    float a = i / (float)n * Mathf.PI * 2f;
                    float r = 0.5f * (0.78f + 0.22f * Mathf.PerlinNoise(Mathf.Cos(a) * 1.7f + v * 5f, Mathf.Sin(a) * 1.7f + v * 3f) +
                                      0.08f * Mathf.Sin(a * 3f + v));
                    verts[i + 1] = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                    uvs[i + 1] = new Vector2(verts[i + 1].x + 0.5f, verts[i + 1].z + 0.5f);
                    tris[i * 3] = 0;
                    tris[i * 3 + 1] = (i + 1) % n + 1;
                    tris[i * 3 + 2] = i + 1;
                }
                mesh.vertices = verts;
                mesh.uv = uvs;
                mesh.uv2 = uvs;
                mesh.triangles = tris;
                mesh.RecalculateNormals();
                mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                string path = $"{MeshDir}/SM_Puddle_{v}.asset";
                var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (existing) { EditorUtility.CopySerialized(mesh, existing); Object.DestroyImmediate(mesh); }
                else AssetDatabase.CreateAsset(mesh, path);
            }
        }

        // -------------------------------------------------------------------------------- generic helpers
        static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat,
            Vector3 euler = default, bool collider = false)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localEulerAngles = euler;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        static GameObject Empty(string name, Transform parent, Vector3 pos = default, Vector3 euler = default)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localEulerAngles = euler;
            return go;
        }

        static void Wire(Object target, params (string field, object value)[] values)
        {
            var so = new SerializedObject(target);
            foreach (var (field, value) in values)
            {
                var p = so.FindProperty(field);
                if (p == null) throw new System.Exception($"{target.GetType().Name}.{field} not found");
                switch (value)
                {
                    case Object o: p.objectReferenceValue = o; break;
                    case Object[] arr:
                        p.arraySize = arr.Length;
                        for (int i = 0; i < arr.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = arr[i];
                        break;
                    case LayerMask lm: p.intValue = lm.value; break;
                    case int i: p.intValue = i; break;
                    case float f: p.floatValue = f; break;
                    case bool b: p.boolValue = b; break;
                    case null: p.objectReferenceValue = null; break;
                    default: throw new System.Exception("unsupported " + value.GetType());
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static GameObject SavePrefab(GameObject go, string folder)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabDir}/{folder}/PF_{go.name}.prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        /// <summary>
        /// Props are static (contribute to GI, batching, occlusion, reflection probes) but receive GI from light probes:
        /// small primitives would only get a few lightmap texels with visible seams, probes are cheaper and smoother.
        /// </summary>
        static void MakeStatic(GameObject go)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                if (!t.GetComponent<Renderer>() && !t.GetComponent<Collider>()) continue;
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, EnvStatic);
                if (t.TryGetComponent<MeshRenderer>(out var mr) && !t.name.StartsWith("Surface")) mr.receiveGI = ReceiveGI.LightProbes;
            }
        }

        static Light AddLight(GameObject go, LightType type, LightmapBakeType bake, Color color, float intensity, LightUnit unit, float range)
        {
            go.AddHDLight(type);
            var l = go.GetComponent<Light>();
            l.lightmapBakeType = bake;
            l.color = color;
            l.lightUnit = unit;
            l.intensity = intensity;
            l.range = range;
            return l;
        }

        // -------------------------------------------------------------------------------- prefabs
        static void CreatePrefabs()
        {
            CreatePlayerPrefab();
            CreateDummyPrefab();
            CreatePropPrefabs();
            CreateLightPrefabs();
            CreateSystemPrefabs();
        }

        static void CreatePlayerPrefab()
        {
            var root = new GameObject("Player");
            var cc = root.AddComponent<CharacterController>();
            cc.height = 1.8f; cc.radius = 0.32f; cc.center = new Vector3(0f, 0.92f, 0f); cc.stepOffset = 0.35f; cc.skinWidth = 0.04f;
            var input = root.AddComponent<PlayerInputReader>();
            var controller = root.AddComponent<ThirdPersonController>();
            var attack = root.AddComponent<MeleeAttack>();

            var visual = Empty("Visual", root.transform).transform;
            Prim(PrimitiveType.Capsule, "Torso", visual, new Vector3(0f, 1.08f, 0f), new Vector3(0.52f, 0.42f, 0.36f), Chr("M_Tunic"));
            Prim(PrimitiveType.Capsule, "Skirt", visual, new Vector3(0f, 0.74f, 0f), new Vector3(0.5f, 0.2f, 0.4f), Chr("M_Tunic"));
            Prim(PrimitiveType.Cylinder, "Belt", visual, new Vector3(0f, 0.86f, 0f), new Vector3(0.47f, 0.035f, 0.38f), Chr("M_Leather"));
            Prim(PrimitiveType.Capsule, "Leg_L", visual, new Vector3(-0.11f, 0.4f, 0f), new Vector3(0.17f, 0.42f, 0.17f), Chr("M_Trousers"));
            Prim(PrimitiveType.Capsule, "Leg_R", visual, new Vector3(0.11f, 0.4f, 0f), new Vector3(0.17f, 0.42f, 0.17f), Chr("M_Trousers"));
            Prim(PrimitiveType.Cube, "Boot_L", visual, new Vector3(-0.11f, 0.06f, 0.04f), new Vector3(0.13f, 0.12f, 0.26f), Chr("M_Leather"));
            Prim(PrimitiveType.Cube, "Boot_R", visual, new Vector3(0.11f, 0.06f, 0.04f), new Vector3(0.13f, 0.12f, 0.26f), Chr("M_Leather"));
            Prim(PrimitiveType.Sphere, "Head", visual, new Vector3(0f, 1.63f, 0.01f), new Vector3(0.22f, 0.25f, 0.23f), Chr("M_Skin"));
            Prim(PrimitiveType.Sphere, "Hair", visual, new Vector3(0f, 1.67f, -0.02f), new Vector3(0.235f, 0.22f, 0.24f), Chr("M_Hair"));
            Prim(PrimitiveType.Capsule, "Arm_L", visual, new Vector3(-0.31f, 1.12f, 0.04f), new Vector3(0.12f, 0.3f, 0.12f), Chr("M_Tunic"), new Vector3(10f, 0f, 8f));
            Prim(PrimitiveType.Sphere, "Pauldron_R", visual, new Vector3(0.25f, 1.38f, 0f), new Vector3(0.2f, 0.14f, 0.2f), Chr("M_Leather"));

            // weapon arm: everything below the shoulder pivot is driven by MeleeAttack
            var pivot = Empty("WeaponPivot", visual, new Vector3(0.27f, 1.37f, 0.03f)).transform;
            Prim(PrimitiveType.Capsule, "Arm_R", pivot, new Vector3(0f, 0f, 0.27f), new Vector3(0.12f, 0.29f, 0.12f), Chr("M_Tunic"), new Vector3(90f, 0f, 0f));
            Prim(PrimitiveType.Sphere, "Hand_R", pivot, new Vector3(0f, 0f, 0.56f), new Vector3(0.1f, 0.1f, 0.11f), Chr("M_Leather"));
            var sword = Empty("Sword", pivot, new Vector3(0f, 0f, 0.58f)).transform;
            Prim(PrimitiveType.Cylinder, "Grip", sword, new Vector3(0f, 0f, 0f), new Vector3(0.035f, 0.08f, 0.035f), Chr("M_Leather"), new Vector3(90f, 0f, 0f));
            Prim(PrimitiveType.Sphere, "Pommel", sword, new Vector3(0f, 0f, -0.09f), new Vector3(0.05f, 0.05f, 0.05f), Chr("M_Steel"));
            Prim(PrimitiveType.Cube, "Guard", sword, new Vector3(0f, 0f, 0.09f), new Vector3(0.22f, 0.025f, 0.035f), Chr("M_Steel"));
            Prim(PrimitiveType.Cube, "Blade", sword, new Vector3(0f, 0f, 0.53f), new Vector3(0.065f, 0.014f, 0.84f), Chr("M_Steel"));
            var bladeBase = Empty("BladeBase", sword, new Vector3(0f, 0f, 0.14f)).transform;
            var bladeTip = Empty("BladeTip", sword, new Vector3(0f, 0f, 0.94f)).transform;

            var trailGo = Empty("Trail", sword, new Vector3(0f, 0f, 0.86f));
            var trail = trailGo.AddComponent<TrailRenderer>();
            trail.sharedMaterial = FxMat("M_FX_Trail");
            trail.time = 0.13f;
            trail.minVertexDistance = 0.02f;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 0.16f), new Keyframe(1f, 0.0f));
            trail.colorGradient = Gradient(new[] { (Color.white, 0f), (Color.white, 1f) }, new[] { (0.8f, 0f), (0.35f, 0.5f), (0f, 1f) });
            trail.textureMode = LineTextureMode.Stretch;
            trail.alignment = LineAlignment.View;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.emitting = false;
            trail.generateLightingData = false;

            var glint = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>($"{PrefabDir}/VFX/PF_FX_SwingGlint.prefab"), sword);
            glint.transform.localPosition = new Vector3(0.02f, 0f, 0.8f);

            foreach (var r in visual.GetComponentsInChildren<MeshRenderer>())
                r.shadowCastingMode = ShadowCastingMode.On;
            SetLayerRecursive(root, LayerPlayer);

            Wire(controller, ("input", input), ("visualRoot", visual));
            Wire(attack, ("input", input), ("controller", controller), ("weaponPivot", pivot), ("bladeBase", bladeBase), ("bladeTip", bladeTip),
                ("trail", trail), ("contactCueGlint", glint.GetComponent<ParticleSystem>()),
                ("hitMask", (LayerMask)(~(1 << LayerPlayer | 1 << LayerVFX | 1 << 2))));
            SavePrefab(root, "Characters");
        }

        static void CreateDummyPrefab()
        {
            var root = new GameObject("TrainingDummy");
            var dummy = root.AddComponent<TrainingDummy>();
            var wood = Env("M_WoodDark");
            var straw = Sg("M_SG_Straw_Dummy");

            Prim(PrimitiveType.Cube, "Foot_A", root.transform, new Vector3(0f, 0.04f, 0f), new Vector3(1.0f, 0.08f, 0.14f), wood, collider: true);
            Prim(PrimitiveType.Cube, "Foot_B", root.transform, new Vector3(0f, 0.04f, 0f), new Vector3(0.14f, 0.08f, 1.0f), wood, collider: true);

            var pivot = Empty("TiltPivot", root.transform, new Vector3(0f, 0.08f, 0f));
            var rb = pivot.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            var pt = pivot.transform;
            Prim(PrimitiveType.Cylinder, "Post", pt, new Vector3(0f, 0.92f, 0f), new Vector3(0.1f, 0.92f, 0.1f), wood);
            var body = Prim(PrimitiveType.Cylinder, "StrawBody", pt, new Vector3(0f, 1.12f, 0f), new Vector3(0.52f, 0.46f, 0.52f), straw);
            Prim(PrimitiveType.Cylinder, "Rope_Low", pt, new Vector3(0f, 0.86f, 0f), new Vector3(0.545f, 0.018f, 0.545f), Chr("M_Leather"));
            Prim(PrimitiveType.Cylinder, "Rope_High", pt, new Vector3(0f, 1.36f, 0f), new Vector3(0.545f, 0.018f, 0.545f), Chr("M_Leather"));
            Prim(PrimitiveType.Cylinder, "CrossBar", pt, new Vector3(0f, 1.5f, 0f), new Vector3(0.08f, 0.56f, 0.08f), wood, new Vector3(0f, 0f, 90f));
            var head = Prim(PrimitiveType.Sphere, "StrawHead", pt, new Vector3(0f, 1.84f, 0f), new Vector3(0.36f, 0.38f, 0.36f), straw);
            var sigil = Prim(PrimitiveType.Cube, "SigilPlaque", pt, new Vector3(0f, 1.16f, -0.27f), new Vector3(0.3f, 0.3f, 0.03f), Sg("M_SG_Ember_Sigil"));

            // colliders live on unit-scale children so decals can be parented without skew
            var bodyCol = Empty("BodyCollider", pt, new Vector3(0f, 1.12f, 0f));
            var cap = bodyCol.AddComponent<CapsuleCollider>();
            cap.radius = 0.28f; cap.height = 1.0f; cap.direction = 1;
            bodyCol.AddComponent<HitSurface>().surface = SurfaceType.Straw;
            var headCol = Empty("HeadCollider", pt, new Vector3(0f, 1.84f, 0f));
            headCol.AddComponent<SphereCollider>().radius = 0.19f;
            headCol.AddComponent<HitSurface>().surface = SurfaceType.Straw;
            var barCol = Empty("CrossBarCollider", pt, new Vector3(0f, 1.5f, 0f));
            var bc = barCol.AddComponent<CapsuleCollider>();
            bc.radius = 0.05f; bc.height = 1.12f; bc.direction = 0;
            barCol.AddComponent<HitSurface>().surface = SurfaceType.Wood;
            var postCol = Empty("PostCollider", pt, new Vector3(0f, 0.35f, 0f));
            var pc = postCol.AddComponent<CapsuleCollider>();
            pc.radius = 0.06f; pc.height = 0.7f; pc.direction = 1;
            postCol.AddComponent<HitSurface>().surface = SurfaceType.Wood;

            SetLayerRecursive(root, LayerHittable);
            Wire(dummy, ("tiltPivot", pt), ("flashRenderers", new Object[] { body.GetComponent<Renderer>(), head.GetComponent<Renderer>() }),
                ("sigilRenderer", sigil.GetComponent<Renderer>()));
            SavePrefab(root, "Characters");
        }

        static void CreatePropPrefabs()
        {
            // crate
            {
                var root = new GameObject("Crate");
                Prim(PrimitiveType.Cube, "Box", root.transform, new Vector3(0f, 0.4f, 0f), new Vector3(0.8f, 0.8f, 0.8f), Env("M_WoodPlanks"), collider: true)
                    .AddComponent<HitSurface>().surface = SurfaceType.Wood;
                foreach (float y in new[] { 0.12f, 0.68f })
                    Prim(PrimitiveType.Cube, "Band", root.transform, new Vector3(0f, y, 0f), new Vector3(0.82f, 0.06f, 0.82f), Env("M_IronDark"));
                MakeStatic(root);
                SavePrefab(root, "Environment");
            }
            // barrel
            {
                var root = new GameObject("Barrel");
                Prim(PrimitiveType.Cylinder, "Staves", root.transform, new Vector3(0f, 0.5f, 0f), new Vector3(0.6f, 0.5f, 0.6f), Env("M_WoodPlanks"), collider: true)
                    .AddComponent<HitSurface>().surface = SurfaceType.Wood;
                foreach (float y in new[] { 0.15f, 0.85f })
                    Prim(PrimitiveType.Cylinder, "Hoop", root.transform, new Vector3(0f, y, 0f), new Vector3(0.62f, 0.03f, 0.62f), Env("M_IronDark"));
                MakeStatic(root);
                SavePrefab(root, "Environment");
            }
            // hay bale
            {
                var root = new GameObject("HayBale");
                Prim(PrimitiveType.Cube, "Bale", root.transform, new Vector3(0f, 0.25f, 0f), new Vector3(1.0f, 0.5f, 0.55f), Env("M_StrawBale"), collider: true)
                    .AddComponent<HitSurface>().surface = SurfaceType.Straw;
                MakeStatic(root);
                SavePrefab(root, "Environment");
            }
            // well
            {
                var root = new GameObject("Well");
                Prim(PrimitiveType.Cylinder, "Ring", root.transform, new Vector3(0f, 0.45f, 0f), new Vector3(1.8f, 0.45f, 1.8f), Env("M_BricksDark"), collider: true);
                Prim(PrimitiveType.Cylinder, "Water", root.transform, new Vector3(0f, 0.82f, 0f), new Vector3(1.45f, 0.02f, 1.45f), Env("M_Puddle"));
                Prim(PrimitiveType.Cube, "Post_L", root.transform, new Vector3(-0.85f, 1.3f, 0f), new Vector3(0.14f, 1.8f, 0.14f), Env("M_WoodDark"), collider: true);
                Prim(PrimitiveType.Cube, "Post_R", root.transform, new Vector3(0.85f, 1.3f, 0f), new Vector3(0.14f, 1.8f, 0.14f), Env("M_WoodDark"), collider: true);
                Prim(PrimitiveType.Cylinder, "Axle", root.transform, new Vector3(0f, 1.9f, 0f), new Vector3(0.08f, 0.9f, 0.08f), Env("M_WoodDark"), new Vector3(0f, 0f, 90f));
                Prim(PrimitiveType.Cube, "Roof_A", root.transform, new Vector3(0f, 2.35f, -0.42f), new Vector3(2.1f, 0.06f, 1.0f), Env("M_RoofShingles"), new Vector3(-35f, 0f, 0f));
                Prim(PrimitiveType.Cube, "Roof_B", root.transform, new Vector3(0f, 2.35f, 0.42f), new Vector3(2.1f, 0.06f, 1.0f), Env("M_RoofShingles"), new Vector3(35f, 0f, 0f));
                MakeStatic(root);
                SavePrefab(root, "Environment");
            }
            // weapon rack
            {
                var root = new GameObject("WeaponRack");
                var w = Env("M_WoodDark");
                Prim(PrimitiveType.Cube, "Leg_L", root.transform, new Vector3(-0.7f, 0.6f, 0f), new Vector3(0.08f, 1.2f, 0.08f), w, collider: true);
                Prim(PrimitiveType.Cube, "Leg_R", root.transform, new Vector3(0.7f, 0.6f, 0f), new Vector3(0.08f, 1.2f, 0.08f), w, collider: true);
                Prim(PrimitiveType.Cube, "Rail", root.transform, new Vector3(0f, 1.0f, 0f), new Vector3(1.5f, 0.07f, 0.1f), w);
                Prim(PrimitiveType.Cube, "Base", root.transform, new Vector3(0f, 0.1f, 0.12f), new Vector3(1.5f, 0.07f, 0.25f), w);
                for (int i = 0; i < 4; i++)
                {
                    var s = Empty($"Weapon_{i}", root.transform, new Vector3(-0.5f + i * 0.33f, 0.62f, 0.08f), new Vector3(-12f, 0f, (i - 1.5f) * 3f)).transform;
                    Prim(PrimitiveType.Cube, "Blade", s, new Vector3(0f, 0.15f, 0f), new Vector3(0.045f, 0.9f, 0.01f), Chr("M_Steel"));
                    Prim(PrimitiveType.Cube, "Guard", s, new Vector3(0f, -0.31f, 0f), new Vector3(0.18f, 0.025f, 0.03f), Chr("M_Steel"));
                    Prim(PrimitiveType.Cylinder, "Grip", s, new Vector3(0f, -0.4f, 0f), new Vector3(0.03f, 0.08f, 0.03f), Chr("M_Leather"));
                }
                MakeStatic(root);
                SavePrefab(root, "Environment");
            }
            // waystone (Shader Graph preset C)
            {
                var root = new GameObject("Waystone");
                Prim(PrimitiveType.Cube, "Plinth", root.transform, new Vector3(0f, 0.15f, 0f), new Vector3(1.1f, 0.3f, 0.9f), Env("M_Rock"), collider: true);
                Prim(PrimitiveType.Cube, "Stone", root.transform, new Vector3(0f, 1.35f, 0f), new Vector3(0.7f, 2.1f, 0.5f), Sg("M_SG_Waystone_Runes"),
                    new Vector3(0f, 0f, 2f), collider: true);
                MakeStatic(root);
                SavePrefab(root, "Environment");
            }
            // chest (Unity HDRP sample mesh)
            {
                var src = Load<GameObject>($"{ThirdPartyModels}/chest.fbx");
                var root = new GameObject("Chest");
                var model = (GameObject)PrefabUtility.InstantiatePrefab(src, root.transform);
                model.transform.localScale = Vector3.one;
                foreach (var r in model.GetComponentsInChildren<Renderer>()) r.sharedMaterial = Env("M_Chest");
                var b = new Bounds(root.transform.position, Vector3.zero);
                foreach (var r in model.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
                float target = 0.9f / Mathf.Max(0.01f, Mathf.Max(b.size.x, b.size.z));
                model.transform.localScale = Vector3.one * target;
                b = new Bounds(root.transform.position, Vector3.zero);
                foreach (var r in model.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
                model.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z);
                var col = root.AddComponent<BoxCollider>();
                col.center = new Vector3(0f, b.size.y * 0.5f, 0f);
                col.size = b.size;
                col.gameObject.AddComponent<HitSurface>().surface = SurfaceType.Wood;
                MakeStatic(root);
                SavePrefab(root, "Environment");
            }
            // rocks (Unity HDRP sample mesh)
            {
                var src = Load<GameObject>($"{ThirdPartyModels}/Rocks.fbx");
                var root = new GameObject("Rocks");
                var model = (GameObject)PrefabUtility.InstantiatePrefab(src, root.transform);
                foreach (var r in model.GetComponentsInChildren<Renderer>()) { var mats = new Material[r.sharedMaterials.Length]; for (int i = 0; i < mats.Length; i++) mats[i] = Env("M_Rock"); r.sharedMaterials = mats; }
                var b = new Bounds(root.transform.position, Vector3.zero);
                foreach (var r in model.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
                float target = 2.4f / Mathf.Max(0.01f, Mathf.Max(b.size.x, b.size.z));
                model.transform.localScale = Vector3.one * target;
                b = new Bounds(root.transform.position, Vector3.zero);
                foreach (var r in model.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
                model.transform.localPosition = new Vector3(-b.center.x, -b.min.y - 0.1f, -b.center.z);
                foreach (var mf in model.GetComponentsInChildren<MeshFilter>()) mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                MakeStatic(root);
                SavePrefab(root, "Environment");
            }
            // puddles
            for (int v = 0; v < 3; v++)
            {
                var root = new GameObject($"Puddle_{v}");
                var go = Empty("Surface", root.transform, new Vector3(0f, 0.004f, 0f));
                go.AddComponent<MeshFilter>().sharedMesh = Load<Mesh>($"{MeshDir}/SM_Puddle_{v}.asset");
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = Env("M_Puddle");
                mr.shadowCastingMode = ShadowCastingMode.Off;
                MakeStatic(root);
                SavePrefab(root, "Environment");
            }
        }

        static void CreateLightPrefabs()
        {
            // baked wall torch: static light in lightmaps/probes, only the flame particles animate
            {
                var root = new GameObject("WallTorch");
                Prim(PrimitiveType.Cube, "Bracket", root.transform, new Vector3(0f, 0f, 0.08f), new Vector3(0.06f, 0.06f, 0.18f), Env("M_IronDark"));
                Prim(PrimitiveType.Cylinder, "Stick", root.transform, new Vector3(0f, 0.12f, 0.16f), new Vector3(0.045f, 0.2f, 0.045f), Env("M_WoodDark"), new Vector3(-15f, 0f, 0f));
                Prim(PrimitiveType.Cylinder, "Head", root.transform, new Vector3(0f, 0.32f, 0.21f), new Vector3(0.075f, 0.05f, 0.075f), Env("M_EmberCoals"));
                MakeStatic(root);
                var flame = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>($"{PrefabDir}/VFX/PF_FX_TorchFlame.prefab"), root.transform);
                flame.transform.localPosition = new Vector3(0f, 0.36f, 0.21f);
                var lightGo = Empty("Light", root.transform, new Vector3(0f, 0.5f, 0.35f));
                var l = AddLight(lightGo, LightType.Point, LightmapBakeType.Baked, Mathf.CorrelatedColorTemperatureToRGB(1900f), 650f, LightUnit.Lumen, 9f);
                l.useColorTemperature = false;
                lightGo.GetComponent<HDAdditionalLightData>().shapeRadius = 0.08f;
                SavePrefab(root, "Environment");
            }
            // brazier: the dynamic (realtime, shadowed, flickering) light that rims the dummy and the hero
            {
                var root = new GameObject("Brazier");
                var iron = Env("M_IronDark");
                for (int i = 0; i < 3; i++)
                {
                    float a = i * 120f;
                    var leg = Prim(PrimitiveType.Cube, $"Leg_{i}", root.transform, Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 0.45f, 0.22f),
                        new Vector3(0.05f, 0.95f, 0.05f), iron, new Vector3(-12f, a, 0f));
                }
                Prim(PrimitiveType.Cylinder, "Bowl", root.transform, new Vector3(0f, 0.92f, 0f), new Vector3(0.62f, 0.09f, 0.62f), iron, collider: true)
                    .AddComponent<HitSurface>().surface = SurfaceType.Metal;
                Prim(PrimitiveType.Sphere, "Coals", root.transform, new Vector3(0f, 1.0f, 0f), new Vector3(0.5f, 0.12f, 0.5f), Env("M_EmberCoals"));
                MakeStatic(root);
                var flame = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>($"{PrefabDir}/VFX/PF_FX_BrazierFlame.prefab"), root.transform);
                flame.transform.localPosition = new Vector3(0f, 1.02f, 0f);
                var lightGo = Empty("FireLight", root.transform, new Vector3(0f, 1.45f, 0f));
                var l = AddLight(lightGo, LightType.Point, LightmapBakeType.Realtime, Mathf.CorrelatedColorTemperatureToRGB(1800f), 2600f, LightUnit.Lumen, 12f);
                l.shadows = LightShadows.Soft;
                var hd = lightGo.GetComponent<HDAdditionalLightData>();
                hd.shapeRadius = 0.25f;
                hd.SetShadowResolutionLevel(0); // low tier: soft fire shadows, cheap
                hd.shadowUpdateMode = ShadowUpdateMode.EveryFrame;
                var flicker = lightGo.AddComponent<TorchFlicker>();
                Wire(flicker, ("flameVisual", flame.transform));
                SavePrefab(root, "Environment");
            }
        }

        static void CreateSystemPrefabs()
        {
            var root = new GameObject("CombatFX");
            var sys = root.AddComponent<HitFeedbackSystem>();
            Wire(sys, ("profiles", new Object[]
            {
                Load<SurfaceFxProfile>($"{DataDir}/SurfaceFX/SFX_Straw.asset"),
                Load<SurfaceFxProfile>($"{DataDir}/SurfaceFX/SFX_Wood.asset"),
                Load<SurfaceFxProfile>($"{DataDir}/SurfaceFX/SFX_Stone.asset"),
            }));
            SavePrefab(root, "Systems");

            var diag = new GameObject("Diagnostics");
            diag.AddComponent<GameBootstrap>();
            diag.AddComponent<DebugHud>();
            diag.AddComponent<PerfHarness>();
            SavePrefab(diag, "Systems");
        }
    }
}
