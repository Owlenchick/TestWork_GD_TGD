using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace DuskYard.EditorTools
{
    /// <summary>
    /// Dusk lighting scenario (physical units):
    ///  Key  - low sun (Mixed / Baked Indirect): realtime direct + shadows, baked bounce. Low elevation through the
    ///         atmosphere gives the warm colour; the yard opens south so the light rakes across the ground.
    ///  Fill - sky (Physically Based Sky, baked into lightmaps + light probes) plus a soft cool area light (Baked)
    ///         standing in for the open eastern sky bounce.
    ///  Rim  - brazier behind the dummy (Realtime, shadowed, flickering) = the dynamic light of the scene; the
    ///         contact flashes add short realtime lights on hits.
    ///  Wall torches are Baked: they cost nothing at runtime and only their particles animate.
    /// </summary>
    public static partial class DuskYardBuilder
    {
        static VolumeProfile CreateVolumeProfile()
        {
            string path = $"{SettingsDir}/Volumes/VP_DuskYard_Global.asset";
            AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);

            T Add<T>() where T : VolumeComponent
            {
                var c = profile.Add<T>(false);
                c.name = typeof(T).Name;
                AssetDatabase.AddObjectToAsset(c, profile);
                return c;
            }

            var env = Add<VisualEnvironment>();
            env.skyType.Override((int)SkyType.PhysicallyBased);
            env.skyAmbientMode.Override(SkyAmbientMode.Static);

            var sky = Add<PhysicallyBasedSky>();
            sky.aerosolDensity.Override(0.12f);           // a bit of evening haze -> warmer, softer horizon
            sky.aerosolTint.Override(new Color(0.85f, 0.8f, 0.75f));
            sky.groundTint.Override(new Color(0.16f, 0.13f, 0.11f));

            var fog = Add<Fog>();
            fog.enabled.Override(true);
            fog.meanFreePath.Override(110f);
            fog.baseHeight.Override(0f);
            fog.maximumHeight.Override(30f);
            fog.enableVolumetricFog.Override(true);
            fog.albedo.Override(new Color(0.85f, 0.82f, 0.78f));
            fog.anisotropy.Override(0.55f);
            fog.depthExtent.Override(48f);

            var exposure = Add<Exposure>();
            exposure.mode.Override(ExposureMode.AutomaticHistogram);
            exposure.meteringMode.Override(MeteringMode.CenterWeighted);
            exposure.limitMin.Override(6f);
            exposure.limitMax.Override(10f);
            exposure.compensation.Override(0f);
            exposure.histogramPercentages.Override(new Vector2(45f, 92f));
            exposure.adaptationSpeedDarkToLight.Override(2f);
            exposure.adaptationSpeedLightToDark.Override(1f);

            var tonemap = Add<Tonemapping>();
            tonemap.mode.Override(TonemappingMode.ACES);

            var wb = Add<WhiteBalance>();
            wb.temperature.Override(-6f); // keep the shadows cool so warm key/rim separate

            var ca = Add<ColorAdjustments>();
            ca.contrast.Override(6f);
            ca.saturation.Override(-4f);

            var bloom = Add<Bloom>();
            bloom.intensity.Override(0.12f);   // low: emissive VFX keep their silhouette
            bloom.scatter.Override(0.55f);
            bloom.threshold.Override(1.0f);

            var ssr = Add<ScreenSpaceReflection>();
            ssr.enabled.Override(true);
            OverridePrivate(ssr, "m_MinSmoothness", 0.8f);   // only puddles / wet stone / steel pay for SSR
            OverridePrivate(ssr, "m_SmoothnessFadeStart", 0.85f);

            var ao = Add<ScreenSpaceAmbientOcclusion>();
            ao.intensity.Override(0.7f);
            ao.radius.Override(1.2f);

            var shadows = Add<HDShadowSettings>();
            shadows.maxShadowDistance.Override(55f);

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        static void BuildLighting(Transform root, VolumeProfile profile)
        {
            // static lighting sky = the sky used by the lightmapper and for the ambient probe
            var sky = root.gameObject.AddComponent<StaticLightingSky>();
            sky.profile = profile;
            sky.staticLightingSkyUniqueID = SkySettings.GetUniqueID<PhysicallyBasedSky>();

            // Key: sun
            var sunGo = new GameObject("Key_Sun");
            sunGo.transform.SetParent(root, false);
            sunGo.transform.rotation = Quaternion.Euler(3.5f, 38f, 0f);
            var sun = AddLight(sunGo, LightType.Directional, LightmapBakeType.Mixed, Color.white, 100000f, LightUnit.Lux, 0f);
            sun.useColorTemperature = true;
            sun.colorTemperature = 5500f;
            sun.shadows = LightShadows.Soft;
            var sunHd = sunGo.GetComponent<HDAdditionalLightData>();
            sunHd.angularDiameter = 0.53f;
            sunHd.SetShadowResolutionLevel(2);
            sunHd.volumetricDimmer = 1f;
            RenderSettings.sun = sun;

            // Fill: cool sky bounce from the open east side (baked, soft)
            var fillGo = new GameObject("Fill_SkyBounce_Area");
            fillGo.transform.SetParent(root, false);
            fillGo.transform.SetPositionAndRotation(new Vector3(13.5f, 3.5f, -4f), Quaternion.Euler(20f, -90f, 0f));
            var fill = AddLight(fillGo, LightType.Rectangle, LightmapBakeType.Baked, Mathf.CorrelatedColorTemperatureToRGB(9000f), 9000f, LightUnit.Lumen, 30f);
            fill.areaSize = new Vector2(8f, 4f);
            fill.shadows = LightShadows.Soft;

            // Torches (baked)
            var torches = new GameObject("Torches_Baked").transform;
            torches.SetParent(root, false);
            void Torch(Vector3 p, float yaw) => Place("Environment/PF_WallTorch", torches, p, yaw);
            Torch(new Vector3(-6f, 2.3f, 14.98f), 180f);
            Torch(new Vector3(2.5f, 2.3f, 14.98f), 180f);
            Torch(new Vector3(-14.98f, 2.3f, -9f), 90f);
            Torch(new Vector3(-2.9f, 2.6f, -14.98f), 0f);
            Torch(new Vector3(2.9f, 2.6f, -14.98f), 0f);
            Torch(new Vector3(11.2f, 2.4f, 7.18f), 180f);

            // Rim / dynamic: brazier behind the dummy
            Place("Environment/PF_Brazier", root, DummyPos + new Vector3(1.9f, 0f, 3.1f), 0f);

            // Light probes: regular grid (no jumps when interpolating), samples inside geometry removed
            var probesGo = new GameObject("LightProbes");
            probesGo.transform.SetParent(root, false);
            var group = probesGo.AddComponent<LightProbeGroup>();
            var positions = new System.Collections.Generic.List<Vector3>();
            Physics.SyncTransforms();
            float[] heights = { 0.35f, 1.4f, 2.6f, 4.2f };
            for (float x = -14f; x <= 14.01f; x += 2f)
            for (float z = -21f; z <= 14.01f; z += 2f)
            foreach (float y in heights)
            {
                if (z < -15.6f && (Mathf.Abs(x) > 7f || y > 2.7f)) continue; // road strip beyond the gate only
                var p = new Vector3(x, y, z);
                if (Physics.CheckSphere(p, 0.25f, 1, QueryTriggerInteraction.Ignore)) continue;
                positions.Add(p);
            }
            // denser ring around the combat spot (dummy / brazier / hero) where the dynamic objects live
            for (int i = 0; i < 12; i++)
            foreach (float r in new[] { 1.3f, 2.6f })
            foreach (float y in new[] { 0.5f, 1.3f, 2.1f })
            {
                float a = i * Mathf.PI / 6f;
                positions.Add(DummyPos + new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r));
            }
            group.probePositions = positions.ToArray();

            // Reflection probe (baked, box projected) covering the yard
            var rpGo = new GameObject("ReflectionProbe_Yard");
            rpGo.transform.SetParent(root, false);
            rpGo.transform.position = new Vector3(0f, 3.5f, 0f); // box is centred on the capture point
            var probe = rpGo.AddComponent<ReflectionProbe>();
            var hdProbe = rpGo.GetComponent<HDAdditionalReflectionData>() ?? rpGo.AddComponent<HDAdditionalReflectionData>();
            hdProbe.mode = ProbeSettings.Mode.Baked;
            hdProbe.influenceVolume.shape = InfluenceShape.Box;
            hdProbe.influenceVolume.boxSize = new Vector3(30.2f, 9f, 30.2f);
            hdProbe.influenceVolume.boxBlendDistancePositive = new Vector3(1.5f, 1.5f, 1.5f);
            hdProbe.influenceVolume.boxBlendDistanceNegative = new Vector3(1.5f, 1.5f, 1.5f);
            var rpRoad = new GameObject("ReflectionProbe_Road");
            rpRoad.transform.SetParent(root, false);
            rpRoad.transform.position = new Vector3(0f, 3.5f, -21f);
            rpRoad.AddComponent<ReflectionProbe>();
            var hdRoad = rpRoad.GetComponent<HDAdditionalReflectionData>() ?? rpRoad.AddComponent<HDAdditionalReflectionData>();
            hdRoad.mode = ProbeSettings.Mode.Baked;
            hdRoad.influenceVolume.shape = InfluenceShape.Box;
            hdRoad.influenceVolume.boxSize = new Vector3(30f, 9f, 12f);

            // lightmapper settings asset
            string lsPath = $"{SettingsDir}/Lighting/LS_DuskYard.lighting";
            var ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(lsPath);
            if (!ls)
            {
                ls = new LightingSettings { name = "LS_DuskYard" };
                AssetDatabase.CreateAsset(ls, lsPath);
            }
            ls.bakedGI = true;
            ls.realtimeGI = false;
            ls.mixedBakeMode = MixedLightingMode.IndirectOnly;
            ls.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
            ls.lightmapResolution = 12f;
            ls.lightmapPadding = 3;
            ls.lightmapMaxSize = 2048;
            ls.directionalityMode = LightmapsMode.CombinedDirectional;
            ls.directSampleCount = 32;
            ls.indirectSampleCount = 384;
            ls.environmentSampleCount = 256;
            ls.maxBounces = 3;
            ls.filteringMode = LightingSettings.FilterMode.Auto;
            ls.ao = false;
            ls.lightmapCompression = LightmapCompression.HighQuality;
            EditorUtility.SetDirty(ls);
            Lightmapping.lightingSettings = ls;
        }

        static void OverridePrivate(VolumeComponent c, string field, float value)
        {
            var f = c.GetType().GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            ((ClampedFloatParameter)f.GetValue(c)).Override(value);
        }

        public static void BakeLighting()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var ls = Lightmapping.lightingSettings;
            var t0 = System.DateTime.Now;
            bool ok = Lightmapping.Bake();
            if (!ok && ls && ls.lightmapper == LightingSettings.Lightmapper.ProgressiveGPU)
            {
                Debug.LogWarning("[DuskYardBuilder] GPU lightmapper failed, retrying on CPU");
                ls.lightmapper = LightingSettings.Lightmapper.ProgressiveCPU;
                ok = Lightmapping.Bake();
            }
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[DuskYardBuilder] bake {(ok ? "OK" : "FAILED")} in {(System.DateTime.Now - t0).TotalSeconds:0}s, lightmaps: {LightmapSettings.lightmaps.Length}");
        }
    }
}
