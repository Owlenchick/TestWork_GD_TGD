using DuskYard.Combat;
using DuskYard.VFX;
using UnityEditor;
using UnityEngine;

namespace DuskYard.EditorTools
{
    /// <summary>
    /// Shuriken effects (HDRP Unlit Shader Graph materials). Every hit effect is a one-shot burst with a hard
    /// maxParticles cap, so the pooled worst case is known up front (see HitFeedbackSystem.ParticleBudget).
    /// Timing rule for contact effects: flash 60-80 ms (reads on the impact frame), secondary 0.2-0.8 s, nothing loops.
    /// </summary>
    public static partial class DuskYardBuilder
    {
        static Material FxMat(string n) => Load<Material>($"{MatDir}/VFX/{n}.mat");

        static void CreateVfxPrefabs()
        {
            // ------------------------------------------------------------ straw contact
            {
                var root = Flash("FX_Hit_Straw", FxMat("M_FX_ContactFlash"), 0.55f, 0.07f, new Color(1f, 0.85f, 0.7f));
                var fibers = Burst(root.transform, "StrawFibers", FxMat("M_FX_StrawFiber"), count: 22, max: 24,
                    life: new Vector2(0.35f, 0.75f), speed: new Vector2(2.2f, 5.2f), size: new Vector2(0.12f, 0.24f),
                    coneAngle: 38f, gravity: 1.1f, color: new Color(1f, 0.95f, 0.85f));
                Spin(fibers, 360f, 900f);
                Drag(fibers, 0.25f);
                FadeOut(fibers, 0.7f);
                var dust = Burst(root.transform, "Dust", FxMat("M_FX_Dust"), count: 5, max: 6,
                    life: new Vector2(0.6f, 1.0f), speed: new Vector2(0.3f, 0.9f), size: new Vector2(0.25f, 0.45f),
                    coneAngle: 55f, gravity: -0.03f, color: new Color(1f, 1f, 1f, 0.55f));
                Grow(dust, 2.2f);
                FadeOut(dust, 0.0f);
                Drag(dust, 0.6f);
                SaveFx(root);
            }
            // ------------------------------------------------------------ wood contact
            {
                var root = Flash("FX_Hit_Wood", FxMat("M_FX_ContactFlash"), 0.45f, 0.06f, new Color(1f, 0.8f, 0.62f));
                var splinters = Burst(root.transform, "Splinters", FxMat("M_FX_StrawFiber"), count: 12, max: 14,
                    life: new Vector2(0.3f, 0.6f), speed: new Vector2(2.5f, 5f), size: new Vector2(0.05f, 0.11f),
                    coneAngle: 30f, gravity: 1.6f, color: new Color(0.62f, 0.48f, 0.34f));
                Spin(splinters, 400f, 1100f);
                FadeOut(splinters, 0.75f);
                var dust = Burst(root.transform, "Dust", FxMat("M_FX_Dust"), count: 3, max: 4,
                    life: new Vector2(0.5f, 0.8f), speed: new Vector2(0.3f, 0.7f), size: new Vector2(0.2f, 0.35f),
                    coneAngle: 50f, gravity: -0.02f, color: new Color(0.8f, 0.72f, 0.62f, 0.45f));
                Grow(dust, 2f);
                FadeOut(dust, 0f);
                SaveFx(root);
            }
            // ------------------------------------------------------------ stone contact (steel on stone -> sparks)
            {
                var root = Flash("FX_Hit_Stone", FxMat("M_FX_ContactFlash"), 0.42f, 0.06f, new Color(1f, 0.9f, 0.75f));
                var sparks = Burst(root.transform, "Sparks", FxMat("M_FX_Spark"), count: 26, max: 30,
                    life: new Vector2(0.16f, 0.42f), speed: new Vector2(4.5f, 9.5f), size: new Vector2(0.022f, 0.04f),
                    coneAngle: 42f, gravity: 1.2f, color: Color.white);
                var r = sparks.GetComponent<ParticleSystemRenderer>();
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.velocityScale = 0.028f;
                r.lengthScale = 1.2f;
                var col = sparks.colorOverLifetime;
                col.enabled = true;
                col.color = new ParticleSystem.MinMaxGradient(Gradient(
                    new[] { (new Color(1f, 0.95f, 0.75f), 0f), (new Color(1f, 0.55f, 0.18f), 0.4f), (new Color(0.9f, 0.25f, 0.05f), 1f) },
                    new[] { (1f, 0f), (1f, 0.6f), (0f, 1f) }));
                Drag(sparks, 0.12f);
                var chips = Burst(root.transform, "Chips", FxMat("M_FX_StoneChip"), count: 8, max: 10,
                    life: new Vector2(0.35f, 0.65f), speed: new Vector2(1.8f, 3.8f), size: new Vector2(0.02f, 0.045f),
                    coneAngle: 35f, gravity: 2.2f, color: Color.white);
                FadeOut(chips, 0.8f);
                var dust = Burst(root.transform, "Dust", FxMat("M_FX_StoneDust"), count: 4, max: 5,
                    life: new Vector2(0.5f, 0.9f), speed: new Vector2(0.25f, 0.7f), size: new Vector2(0.18f, 0.32f),
                    coneAngle: 50f, gravity: -0.02f, color: new Color(1f, 1f, 1f, 0.5f));
                Grow(dust, 2.2f);
                FadeOut(dust, 0f);
                SaveFx(root);
            }
            // ------------------------------------------------------------ contact cue: glint on the edge right before contact
            {
                var root = Flash("FX_SwingGlint", FxMat("M_FX_Glint"), 0.32f, 0.12f, Color.white);
                var main = root.main;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                var sol = root.sizeOverLifetime;
                sol.enabled = true;
                sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.1f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0f)));
                SaveFx(root);
            }
            // ------------------------------------------------------------ looping flames
            SaveFx(Flame("FX_TorchFlame", 1f));
            SaveFx(Flame("FX_BrazierFlame", 2.1f));
        }

        static void SaveFx(ParticleSystem root)
        {
            SetLayerRecursive(root.gameObject, LayerVFX);
            PrefabUtility.SaveAsPrefabAsset(root.gameObject, $"{PrefabDir}/VFX/PF_{root.name}.prefab");
            Object.DestroyImmediate(root.gameObject);
        }

        static ParticleSystem NewSystem(string name, Transform parent, Material mat, int maxParticles)
        {
            var go = new GameObject(name);
            if (parent) go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 1f;
            main.maxParticles = maxParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.stopAction = ParticleSystemStopAction.None;
            main.cullingMode = ParticleSystemCullingMode.Automatic;
            var em = ps.emission;
            em.rateOverTime = 0f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            return ps;
        }

        static ParticleSystem Flash(string name, Material mat, float size, float life, Color color)
        {
            var ps = NewSystem(name, null, mat, 1);
            var main = ps.main;
            main.duration = 0.5f;
            main.startLifetime = life;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.85f, size * 1.15f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = color;
            var em = ps.emission;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            var sh = ps.shape;
            sh.enabled = false;
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.55f), new Keyframe(1f, 1f)));
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(Gradient(new[] { (Color.white, 0f), (Color.white, 1f) }, new[] { (1f, 0f), (0.6f, 0.4f), (0f, 1f) }));
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.sortingFudge = -10f; // draw on top of debris
            return ps;
        }

        static ParticleSystem Burst(Transform parent, string name, Material mat, int count, int max, Vector2 life, Vector2 speed,
            Vector2 size, float coneAngle, float gravity, Color color)
        {
            var ps = NewSystem(name, parent, mat, max);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = gravity;
            main.startColor = color;
            var em = ps.emission;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            var sh = ps.shape;
            sh.enabled = true;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = coneAngle;
            sh.radius = 0.03f;
            return ps;
        }

        static void Spin(ParticleSystem ps, float min, float max)
        {
            var rol = ps.rotationOverLifetime;
            rol.enabled = true;
            rol.z = new ParticleSystem.MinMaxCurve(min * Mathf.Deg2Rad, max * Mathf.Deg2Rad);
        }

        static void Drag(ParticleSystem ps, float drag)
        {
            var lim = ps.limitVelocityOverLifetime;
            lim.enabled = true;
            lim.drag = drag;
            lim.multiplyDragByParticleSize = false;
            lim.multiplyDragByParticleVelocity = true;
        }

        static void Grow(ParticleSystem ps, float factor)
        {
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f / factor, 1f, 1f));
            var main = ps.main;
            main.startSizeMultiplier *= factor;
        }

        static void FadeOut(ParticleSystem ps, float holdUntil)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(Gradient(new[] { (Color.white, 0f), (Color.white, 1f) },
                holdUntil > 0f ? new[] { (1f, 0f), (1f, holdUntil), (0f, 1f) } : new[] { (0f, 0f), (1f, 0.1f), (0f, 1f) }));
        }

        static Gradient Gradient((Color c, float t)[] colors, (float a, float t)[] alphas)
        {
            var g = new Gradient();
            var ck = new GradientColorKey[colors.Length];
            for (int i = 0; i < colors.Length; i++) ck[i] = new GradientColorKey(colors[i].c, colors[i].t);
            var ak = new GradientAlphaKey[alphas.Length];
            for (int i = 0; i < alphas.Length; i++) ak[i] = new GradientAlphaKey(alphas[i].a, alphas[i].t);
            g.SetKeys(ck, ak);
            return g;
        }

        static ParticleSystem Flame(string name, float scale)
        {
            var root = NewSystem(name, null, FxMat("M_FX_Flame"), Mathf.RoundToInt(24 * scale));
            root.transform.localScale = Vector3.one * scale;
            var main = root.main;
            main.loop = true;
            main.prewarm = true;
            main.playOnAwake = true;
            main.duration = 2f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.3f);
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
            main.gravityModifier = -0.15f;
            var em = root.emission;
            em.rateOverTime = 20f * Mathf.Sqrt(scale);
            var sh = root.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 6f;
            sh.radius = 0.035f;
            sh.rotation = new Vector3(-90f, 0f, 0f);
            var sol = root.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.7f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0.15f)));
            var col = root.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(Gradient(
                new[] { (new Color(1f, 0.85f, 0.6f), 0f), (new Color(1f, 0.5f, 0.2f), 0.5f), (new Color(0.8f, 0.2f, 0.05f), 1f) },
                new[] { (0f, 0f), (1f, 0.15f), (0.7f, 0.6f), (0f, 1f) }));
            var r = root.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.VerticalBillboard;

            var embers = NewSystem("Embers", root.transform, FxMat("M_FX_Ember"), Mathf.RoundToInt(8 * scale));
            var em2 = embers.emission;
            em2.rateOverTime = 3.5f * scale;
            var m2 = embers.main;
            m2.loop = true; m2.prewarm = true; m2.playOnAwake = true; m2.duration = 2f;
            m2.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.8f);
            m2.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            m2.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.022f);
            m2.gravityModifier = -0.05f;
            var sh2 = embers.shape;
            sh2.shapeType = ParticleSystemShapeType.Cone; sh2.angle = 18f; sh2.radius = 0.05f; sh2.rotation = new Vector3(-90f, 0f, 0f);
            var noise = embers.noise;
            noise.enabled = true; noise.strength = 0.35f; noise.frequency = 1.2f; noise.quality = ParticleSystemNoiseQuality.Low;
            FadeOut(embers, 0.6f);

            var smoke = NewSystem("Smoke", root.transform, FxMat("M_FX_Smoke"), Mathf.RoundToInt(8 * scale));
            var em3 = smoke.emission;
            em3.rateOverTime = 2.5f * Mathf.Sqrt(scale);
            var m3 = smoke.main;
            m3.loop = true; m3.prewarm = true; m3.playOnAwake = true; m3.duration = 2f;
            m3.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.6f);
            m3.startSpeed = new ParticleSystem.MinMaxCurve(0.35f, 0.55f);
            m3.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.28f);
            m3.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            m3.startColor = new Color(1f, 1f, 1f, 0.35f);
            var sh3 = smoke.shape;
            sh3.shapeType = ParticleSystemShapeType.Cone; sh3.angle = 8f; sh3.radius = 0.04f; sh3.rotation = new Vector3(-90f, 0f, 0f);
            smoke.transform.localPosition = new Vector3(0f, 0.25f, 0f);
            Grow(smoke, 3.5f);
            FadeOut(smoke, 0f);
            Spin(smoke, -20f, 20f);
            return root;
        }

        static void CreateSurfaceProfiles()
        {
            SurfaceFxProfile P(string name, SurfaceType s) => LoadOrCreate<SurfaceFxProfile>($"{DataDir}/SurfaceFX/{name}.asset");
            ParticleSystem Fx(string n) => Load<GameObject>($"{PrefabDir}/VFX/PF_{n}.prefab").GetComponent<ParticleSystem>();
            Material Dec(string n) => Load<Material>($"{MatDir}/Decals/{n}.mat");

            var straw = P("SFX_Straw", SurfaceType.Straw);
            straw.surface = SurfaceType.Straw;
            straw.hitPrefab = Fx("FX_Hit_Straw");
            straw.poolSize = 6;
            straw.sprayAlongSwing = 0.6f;
            straw.decalMaterial = Dec("M_Decal_StrawGash");
            straw.decalPoolSize = 8;
            straw.decalSize = new Vector2(0.36f, 0.11f);
            straw.decalDepth = 0.3f;
            straw.lightColor = new Color(1f, 0.74f, 0.48f);
            straw.lightLumen = 650f;
            straw.lightRange = 3f;
            straw.lightDuration = 0.1f;
            straw.shakeAmplitude = 0.045f;
            straw.hitStop = 0.055f;
            EditorUtility.SetDirty(straw);

            var wood = P("SFX_Wood", SurfaceType.Wood);
            wood.surface = SurfaceType.Wood;
            wood.hitPrefab = Fx("FX_Hit_Wood");
            wood.poolSize = 4;
            wood.sprayAlongSwing = 0.5f;
            wood.decalMaterial = Dec("M_Decal_WoodGash");
            wood.decalPoolSize = 4;
            wood.decalSize = new Vector2(0.28f, 0.07f);
            wood.decalDepth = 0.2f;
            wood.lightColor = new Color(1f, 0.74f, 0.5f);
            wood.lightLumen = 500f;
            wood.lightRange = 2.5f;
            wood.lightDuration = 0.09f;
            wood.shakeAmplitude = 0.05f;
            wood.hitStop = 0.05f;
            EditorUtility.SetDirty(wood);

            var stone = P("SFX_Stone", SurfaceType.Stone);
            stone.surface = SurfaceType.Stone;
            stone.hitPrefab = Fx("FX_Hit_Stone");
            stone.poolSize = 6;
            stone.sprayAlongSwing = 0.7f;
            stone.decalMaterial = Dec("M_Decal_StoneScuff");
            stone.decalPoolSize = 8;
            stone.decalSize = new Vector2(0.34f, 0.12f);
            stone.decalDepth = 0.2f;
            stone.lightColor = new Color(1f, 0.62f, 0.3f);
            stone.lightLumen = 1100f;
            stone.lightRange = 3.5f;
            stone.lightDuration = 0.14f;
            stone.shakeAmplitude = 0.065f;
            stone.hitStop = 0.065f;
            EditorUtility.SetDirty(stone);
        }
    }
}
