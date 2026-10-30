using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using DuskYard.Combat;
using DuskYard.Player;
using DuskYard.Target;
using DuskYard.VFX;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;
using Random = UnityEngine.Random;

namespace DuskYard.Diagnostics
{
    /// <summary>
    /// Reproducible measurement + capture tool that ships inside the build.
    ///
    ///   DuskYard -benchmark [-benchOut DIR] [-benchScale 1]      Baseline / Gameplay / Stress, writes JSON + MD + CSV, quits
    ///   DuskYard -showcase  [-benchOut DIR]                      scripted run that saves reference screenshots, quits
    ///
    /// Baseline : hero idle, camera on the dummy, no VFX (cost of the lit scene).
    /// Gameplay : autopilot walks around the dummy and performs forehand/backhand combos (~1.5 contacts/s).
    /// Stress   : continuous combos + synthetic contacts on dummy, walls and props (default 30/s) to saturate every pool.
    /// </summary>
    [DefaultExecutionOrder(-90)] // after PlayerInputReader, before gameplay scripts
    public sealed class PerfHarness : MonoBehaviour
    {
        [Header("Scene references")]
        [SerializeField] PlayerInputReader input;
        [SerializeField] ThirdPersonController player;
        [SerializeField] ThirdPersonCamera cameraRig;
        [SerializeField] MeleeAttack attack;
        [SerializeField] TrainingDummy dummy;
        [SerializeField] DebugHud hud;
        [SerializeField] Collider[] stressTargets;
        [SerializeField] Transform[] showcaseCameraAnchors;
        [SerializeField] LayerMask stressMask = ~0;

        [Header("Protocol (seconds)")]
        [SerializeField] float warmup = 6f;
        [SerializeField] float baseline = 20f;
        [SerializeField] float gameplay = 30f;
        [SerializeField] float stress = 60f;
        [SerializeField] float stressHitsPerSecond = 30f;
        [SerializeField] int width = 1920, height = 1080;

        enum Pilot { Idle, Combat, Stress }

        [Serializable]
        public class SegmentResult
        {
            public string name;
            public int frames;
            public float seconds;
            public float medianFps, meanFps, low1PercentFps;
            public float medianMs, p95Ms, p99Ms, maxMs;
            public int framesOver16_7ms;
            public int gcCollections;
            public float gcAllocKBPerFrameAvg = -1f;   // -1 = recorder unavailable in this player
            public float monoUsedMBStart, monoUsedMBEnd;
            public float totalAllocatedMBStart, totalAllocatedMBEnd;
            public int maxAliveParticles, particleBudget;
            public int maxActiveDecals, decalCapacity;
            public int transformsStart, transformsEnd;
            public int contacts;
        }

        [Serializable]
        public class ProbeCheck
        {
            public int samples;
            public float stepMeters;
            public float meanRelativeStep;
            public float maxRelativeStep;
            public string worstAt;
        }

        [Serializable]
        public class Report
        {
            public string unityVersion, os, cpu, gpu, graphicsApi, qualityLevel, resolution, timestamp;
            public int systemMemoryMB, cpuCores, vSyncCount, targetFrameRate;
            public SegmentResult[] segments;
            public ProbeCheck probeContinuity;
        }

        Pilot _pilot;
        bool _manualInput;   // showcase drives the input reader itself
        float[] _frameMs = new float[60000];
        int _frameCount;
        float _segmentTime;
        bool _recording;
        SegmentResult _current;
        ProfilerRecorder _gcAllocRecorder;
        double _gcAllocSum;
        int _gcAllocFrames;
        readonly StringBuilder _timeline = new StringBuilder(64 * 1024);
        float _timelineNext;

        // autopilot
        float _angle, _stateTimer, _hitAccumulator;
        int _autoState;
        Vector3 _goal;

        string _outDir;
        ProbeCheck _probeCheck;

        IEnumerator Start()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            bool bench = HasArg(args, "-benchmark");
            bool showcase = HasArg(args, "-showcase");
            if (!bench && !showcase) { enabled = false; yield break; }

            _outDir = GetArg(args, "-benchOut") ?? Path.Combine(Application.persistentDataPath, "Benchmark");
            Directory.CreateDirectory(_outDir);
            float scale = float.TryParse(GetArg(args, "-benchScale"), NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : 1f;

            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            Screen.SetResolution(width, height, FullScreenMode.Windowed);
            if (hud) hud.enabled = false;
            if (input) input.Scripted = true;
            _angle = 180f;

            if (showcase) { _manualInput = true; yield return Showcase(); Application.Quit(); yield break; }

            _gcAllocRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            var segments = new SegmentResult[3];
            _probeCheck = CheckProbeContinuity(0.05f);

            _pilot = Pilot.Idle;
            yield return new WaitForSecondsRealtime(warmup * scale);
            yield return RunSegment("Baseline", baseline * scale, Pilot.Idle, r => segments[0] = r);
            yield return RunSegment("Gameplay", gameplay * scale, Pilot.Combat, r => segments[1] = r);
            HitStop.Enabled = false; // a permanent hit-stop would distort the stress frame pacing
            yield return RunSegment("Stress", stress * scale, Pilot.Stress, r => segments[2] = r);
            HitStop.Enabled = true;
            _pilot = Pilot.Idle;

            WriteReport(segments);
            _gcAllocRecorder.Dispose();
            Application.Quit();
        }

        IEnumerator RunSegment(string name, float duration, Pilot pilot, Action<SegmentResult> done)
        {
            _pilot = pilot;
            _autoState = 0;
            _stateTimer = 0f;
            GC.Collect(); // start every segment from a clean heap so GC counts are comparable
            yield return null;

            var fx = HitFeedbackSystem.Instance;
            _current = new SegmentResult
            {
                name = name,
                monoUsedMBStart = Profiler.GetMonoUsedSizeLong() / 1048576f,
                totalAllocatedMBStart = Profiler.GetTotalAllocatedMemoryLong() / 1048576f,
                transformsStart = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length,
                particleBudget = fx ? fx.ParticleBudget() : 0,
                decalCapacity = fx ? fx.DecalCapacity() : 0,
            };
            int gc0 = GC.CollectionCount(0);
            int contacts0 = fx ? fx.HitsPlayed : 0;
            _frameCount = 0;
            _segmentTime = 0f;
            _gcAllocSum = 0;
            _gcAllocFrames = 0;
            _timelineNext = 0f;
            _recording = true;

            while (_segmentTime < duration) yield return null;

            _recording = false;
            _current.gcCollections = GC.CollectionCount(0) - gc0;
            _current.contacts = fx ? fx.HitsPlayed - contacts0 : 0;
            _current.monoUsedMBEnd = Profiler.GetMonoUsedSizeLong() / 1048576f;
            _current.totalAllocatedMBEnd = Profiler.GetTotalAllocatedMemoryLong() / 1048576f;
            _current.transformsEnd = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            if (_gcAllocFrames > 0) _current.gcAllocKBPerFrameAvg = (float)(_gcAllocSum / _gcAllocFrames / 1024.0);
            FinalizeFrameStats(_current);
            Debug.Log($"[PerfHarness] {name}: median {_current.medianFps:0.0} FPS, 1% low {_current.low1PercentFps:0.0}, " +
                      $"p99 {_current.p99Ms:0.00} ms, GC {_current.gcCollections}, particles max {_current.maxAliveParticles}/{_current.particleBudget}");
            done(_current);
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (_recording)
            {
                if (_frameCount < _frameMs.Length) _frameMs[_frameCount++] = dt * 1000f;
                _segmentTime += dt;
                if (_gcAllocRecorder.Valid) { _gcAllocSum += _gcAllocRecorder.LastValue; _gcAllocFrames++; }
                var fx = HitFeedbackSystem.Instance;
                if (fx)
                {
                    _current.maxAliveParticles = Mathf.Max(_current.maxAliveParticles, fx.AliveParticles());
                    _current.maxActiveDecals = Mathf.Max(_current.maxActiveDecals, fx.ActiveDecals());
                }
                if (_segmentTime >= _timelineNext)
                {
                    _timelineNext += 1f;
                    _timeline.Append(_current.name).Append(',')
                        .Append(_segmentTime.ToString("0.0", CultureInfo.InvariantCulture)).Append(',')
                        .Append((1f / Mathf.Max(dt, 1e-5f)).ToString("0.0", CultureInfo.InvariantCulture)).Append(',')
                        .Append(fx ? fx.AliveParticles() : 0).Append(',')
                        .Append(fx ? fx.ActiveDecals() : 0).Append(',')
                        .Append((Profiler.GetMonoUsedSizeLong() / 1048576f).ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
                        .Append(GC.CollectionCount(0)).Append('\n');
                }
            }
            DriveAutopilot(Time.deltaTime);
        }

        // ------------------------------------------------------------------------------------------- autopilot
        void DriveAutopilot(float dt)
        {
            if (!input || !input.Scripted || _manualInput) return;
            if (_pilot == Pilot.Idle || !dummy || !player) { input.SetScripted(Vector2.zero, Vector2.zero, false, false); return; }

            if (cameraRig) { cameraRig.FocusTarget = dummy.transform; cameraRig.LockOn = true; }
            _stateTimer += dt;
            bool attackNow = false;
            Vector3 dPos = dummy.transform.position;
            Vector3 pPos = player.transform.position;

            switch (_autoState)
            {
                case 0: // pick a new approach point on a ring around the dummy
                    _angle += Random.Range(35f, 75f) * (Random.value < 0.5f ? -1f : 1f);
                    _angle = Mathf.Clamp(Mathf.DeltaAngle(0f, _angle), -150f, 150f) + 180f;
                    _goal = dPos + Quaternion.Euler(0f, _angle, 0f) * Vector3.forward * 1.3f;
                    _goal.y = pPos.y;
                    _autoState = 1; _stateTimer = 0f;
                    break;
                case 1: // walk
                    if (Vector3.Distance(Flat(pPos), Flat(_goal)) < 0.2f || _stateTimer > 4f) { _autoState = 2; _stateTimer = 0f; }
                    break;
                case 2: // forehand
                    player.FaceTowards(dPos, 720f * dt);
                    if (_stateTimer > 0.15f) { attackNow = true; _autoState = 3; _stateTimer = 0f; }
                    break;
                case 3: // backhand (combo window)
                    if (_stateTimer > 0.42f) { attackNow = true; _autoState = 4; _stateTimer = 0f; }
                    break;
                case 4: // recover / repeat
                    float wait = _pilot == Pilot.Stress ? 0.45f : 0.9f;
                    if (_stateTimer > wait) { _autoState = _pilot == Pilot.Stress && Random.value < 0.6f ? 2 : 0; _stateTimer = 0f; }
                    break;
            }

            Vector2 move = Vector2.zero;
            if (_autoState == 1)
            {
                Vector3 to = Flat(_goal) - Flat(pPos);
                Transform cam = cameraRig ? cameraRig.transform : player.transform;
                Vector3 f = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
                Vector3 r = Vector3.Cross(Vector3.up, f);
                Vector3 dir = to.normalized * Mathf.Clamp01(to.magnitude * 2f);
                move = new Vector2(Vector3.Dot(dir, r), Vector3.Dot(dir, f));
            }
            input.SetScripted(move, Vector2.zero, false, attackNow);

            if (_pilot == Pilot.Stress) SpawnSyntheticHits(dt);
        }

        void SpawnSyntheticHits(float dt)
        {
            var fx = HitFeedbackSystem.Instance;
            if (!fx || stressTargets == null || stressTargets.Length == 0) return;
            _hitAccumulator += stressHitsPerSecond * Time.unscaledDeltaTime;
            while (_hitAccumulator >= 1f)
            {
                _hitAccumulator -= 1f;
                var c = stressTargets[Random.Range(0, stressTargets.Length)];
                if (!c) continue;
                Bounds b = c.bounds;
                Vector3 aim = b.center + Vector3.Scale(b.extents * 0.6f, new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), Random.Range(-1f, 1f)));
                Vector3 from = aim + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * 2.5f + Vector3.up * Random.Range(-0.3f, 0.6f);
                Vector3 dir = (aim - from).normalized;
                if (!Physics.Raycast(from, dir, out RaycastHit h, 5f, stressMask, QueryTriggerInteraction.Ignore)) continue;
                var info = new HitInfo
                {
                    Point = h.point,
                    Normal = h.normal,
                    Direction = (Vector3.Cross(h.normal, Vector3.up).normalized * (Random.value < 0.5f ? -1f : 1f) + dir * 0.3f).normalized,
                    Speed = 12f,
                    Collider = h.collider,
                    Surface = HitSurface.Resolve(h.collider),
                };
                h.collider.GetComponentInParent<IHittable>()?.ReceiveHit(info);
                fx.Play(info);
            }
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        // ------------------------------------------------------------------------------------------- showcase
        IEnumerator Showcase()
        {
            yield return new WaitForSecondsRealtime(4f);
            int shot = 0;

            // 1..n: static framing anchors (overview, shader presets, lighting)
            if (showcaseCameraAnchors != null)
            {
                cameraRig.enabled = false;
                foreach (var a in showcaseCameraAnchors)
                {
                    if (!a) continue;
                    cameraRig.transform.SetPositionAndRotation(a.position, a.rotation);
                    yield return new WaitForSecondsRealtime(1.2f);
                    yield return Capture($"{++shot:00}_{a.name}");
                }
                cameraRig.enabled = true;
            }

            // gameplay framing: stand in front of the dummy, then hit it
            Vector3 d = dummy.transform.position;
            player.Teleport(d + new Vector3(0.35f, 0f, -1.35f), Quaternion.LookRotation(Vector3.forward));
            cameraRig.FocusTarget = dummy.transform;
            cameraRig.LockOn = true;
            cameraRig.SnapBehindTarget();
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Capture($"{++shot:00}_Gameplay_Ready");

            int contacts = attack.ContactsMade;
            input.SetScripted(Vector2.zero, Vector2.zero, false, true);
            yield return null;
            input.SetScripted(Vector2.zero, Vector2.zero, false, false);
            // windup frame (contact cue glint + trail starting)
            yield return new WaitForSecondsRealtime(0.13f);
            yield return Capture($"{++shot:00}_Swing_ContactCue");
            float timeout = Time.realtimeSinceStartup + 2f;
            while (attack.ContactsMade == contacts && Time.realtimeSinceStartup < timeout) yield return null;
            yield return Capture($"{++shot:00}_Contact_Flash");
            yield return new WaitForSecondsRealtime(0.12f);
            yield return Capture($"{++shot:00}_Contact_Secondary");
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Capture($"{++shot:00}_Dummy_Hit");
            yield return CloseUp(HitFeedbackSystem.Instance.LastHit, $"{++shot:00}_Decal_Straw_Closeup");
            while (dummy.State != DummyState.Recovering && Time.realtimeSinceStartup < timeout + 3f) yield return null;
            yield return new WaitForSecondsRealtime(0.45f);
            yield return Capture($"{++shot:00}_Dummy_Recovering");
            while (dummy.State != DummyState.Ready && Time.realtimeSinceStartup < timeout + 6f) yield return null;
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Capture($"{++shot:00}_Dummy_Ready_Again");

            // stone contact (sparks) on the nearest stress target that is not the dummy
            foreach (var c in stressTargets)
            {
                if (!c || c.GetComponentInParent<TrainingDummy>()) continue;
                if (HitSurface.Resolve(c) != SurfaceType.Stone) continue;
                // aim between the wall buttresses (they sit on x = 0, +-6)
                Vector3 probe = d + new Vector3(2.8f, 1.2f, 0f);
                Vector3 p = c.ClosestPoint(probe);
                Vector3 n = (probe - p).normalized;
                n = Vector3.ProjectOnPlane(n, Vector3.up).normalized;
                cameraRig.enabled = false;
                cameraRig.transform.SetPositionAndRotation(p + n * 2.2f + Vector3.up * 0.3f + Vector3.Cross(Vector3.up, n) * 0.8f,
                    Quaternion.LookRotation(p - (p + n * 2.2f + Vector3.up * 0.3f + Vector3.Cross(Vector3.up, n) * 0.8f)));
                yield return new WaitForSecondsRealtime(0.8f);
                HitFeedbackSystem.Instance.Play(new HitInfo { Point = p, Normal = n, Direction = Vector3.Cross(n, Vector3.up), Speed = 10f, Collider = c, Surface = SurfaceType.Stone });
                yield return null;
                yield return Capture($"{++shot:00}_Stone_Contact");
                yield return new WaitForSecondsRealtime(0.15f);
                yield return Capture($"{++shot:00}_Stone_Sparks");
                yield return new WaitForSecondsRealtime(0.6f);
                yield return CloseUp(HitFeedbackSystem.Instance.LastHit, $"{++shot:00}_Decal_Stone_Closeup");
                break;
            }
        }

        IEnumerator CloseUp(HitInfo hit, string name)
        {
            bool was = cameraRig.enabled;
            cameraRig.enabled = false;
            Vector3 eye = hit.Point + hit.Normal * 0.75f + Vector3.up * 0.15f;
            cameraRig.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(hit.Point - eye));
            yield return null;
            yield return Capture(name);
            cameraRig.enabled = was;
        }

        IEnumerator Capture(string name)
        {
            ScreenCapture.CaptureScreenshot(Path.Combine(_outDir, name + ".png"));
            yield return null;
            yield return null;
        }

        // ------------------------------------------------------------------------------------------- reporting
        void FinalizeFrameStats(SegmentResult r)
        {
            int n = _frameCount;
            r.frames = n;
            r.seconds = _segmentTime;
            if (n == 0) return;
            Array.Sort(_frameMs, 0, n);
            double sum = 0;
            int over = 0;
            for (int i = 0; i < n; i++) { sum += _frameMs[i]; if (_frameMs[i] > 16.667f) over++; }
            r.medianMs = Percentile(n, 0.5f);
            r.p95Ms = Percentile(n, 0.95f);
            r.p99Ms = Percentile(n, 0.99f);
            r.maxMs = _frameMs[n - 1];
            r.medianFps = 1000f / r.medianMs;
            r.meanFps = (float)(1000.0 / (sum / n));
            // 1% low = average FPS of the slowest 1% of frames
            int k = Mathf.Max(1, n / 100);
            double slow = 0;
            for (int i = n - k; i < n; i++) slow += _frameMs[i];
            r.low1PercentFps = (float)(1000.0 / (slow / k));
            r.framesOver16_7ms = over;
        }

        float Percentile(int n, float p) => _frameMs[Mathf.Clamp(Mathf.RoundToInt((n - 1) * p), 0, n - 1)];

        void WriteReport(SegmentResult[] segs)
        {
            var rep = new Report
            {
                unityVersion = Application.unityVersion,
                os = SystemInfo.operatingSystem,
                cpu = SystemInfo.processorType,
                cpuCores = SystemInfo.processorCount,
                gpu = SystemInfo.graphicsDeviceName,
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                systemMemoryMB = SystemInfo.systemMemorySize,
                qualityLevel = QualitySettings.names[QualitySettings.GetQualityLevel()],
                resolution = $"{Screen.width}x{Screen.height}",
                vSyncCount = QualitySettings.vSyncCount,
                targetFrameRate = Application.targetFrameRate,
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                segments = segs,
                probeContinuity = _probeCheck,
            };
            File.WriteAllText(Path.Combine(_outDir, "benchmark.json"), JsonUtility.ToJson(rep, true));
            File.WriteAllText(Path.Combine(_outDir, "timeline.csv"), "segment,t,fps,particles,decals,monoMB,gcCount\n" + _timeline);

            var md = new StringBuilder();
            md.AppendLine($"Device: {rep.cpu} ({rep.cpuCores} threads), {rep.gpu}, {rep.graphicsApi}, {rep.systemMemoryMB} MB RAM, {rep.os}");
            md.AppendLine($"Unity {rep.unityVersion}, quality '{rep.qualityLevel}', {rep.resolution}, vSyncCount={rep.vSyncCount}, targetFrameRate={rep.targetFrameRate}");
            md.AppendLine();
            md.AppendLine("| Segment | Duration s | Frames | Median FPS | 1% low FPS | p95 ms | p99 ms | Max ms | GC.Collect | GC alloc KB/frame | Mono MB start→end | Max particles (budget) | Max decals (cap) | Transforms start→end | Contacts |");
            md.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (var s in segs)
            {
                string alloc = s.gcAllocKBPerFrameAvg < 0 ? "n/a" : s.gcAllocKBPerFrameAvg.ToString("0.000", CultureInfo.InvariantCulture);
                md.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "| {0} | {1:0.0} | {2} | {3:0.0} | {4:0.0} | {5:0.00} | {6:0.00} | {7:0.00} | {8} | {9} | {10:0.0}→{11:0.0} | {12} ({13}) | {14} ({15}) | {16}→{17} | {18} |",
                    s.name, s.seconds, s.frames, s.medianFps, s.low1PercentFps, s.p95Ms, s.p99Ms, s.maxMs, s.gcCollections, alloc,
                    s.monoUsedMBStart, s.monoUsedMBEnd, s.maxAliveParticles, s.particleBudget, s.maxActiveDecals, s.decalCapacity,
                    s.transformsStart, s.transformsEnd, s.contacts));
            }
            if (_probeCheck != null)
                md.AppendLine().AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "Light probe continuity along the hero paths: {0} samples every {1:0.00} m, mean step {2:0.00}%, max step {3:0.00}% at {4}",
                    _probeCheck.samples, _probeCheck.stepMeters, _probeCheck.meanRelativeStep * 100f, _probeCheck.maxRelativeStep * 100f, _probeCheck.worstAt));
            File.WriteAllText(Path.Combine(_outDir, "benchmark.md"), md.ToString());
            Debug.Log("[PerfHarness] report written to " + _outDir + "\n" + md);
        }

        /// <summary>
        /// Samples the baked light probe field (tetrahedral interpolation, exactly what a moving renderer gets) along the
        /// routes the hero actually takes and reports the largest relative change of probe irradiance between
        /// neighbouring samples. A badly placed probe (inside geometry, missing ring) shows up as a spike here.
        /// </summary>
        ProbeCheck CheckProbeContinuity(float step)
        {
            Vector3 d = dummy ? dummy.transform.position : Vector3.zero;
            var routes = new Vector3[][]
            {
                new[] { new Vector3(-12f, 1f, -13f), new Vector3(0.4f, 1f, -2.6f), new Vector3(10f, 1f, 4f) },          // across sun and shade
                new[] { new Vector3(-13f, 1f, 2f), new Vector3(-6f, 1f, 4f), d + new Vector3(0f, 1f, -1.4f) },           // from the lean-to into the yard
                new[] { new Vector3(0f, 1f, -20f), new Vector3(0f, 1f, -15.4f), new Vector3(0f, 1f, -8f) },             // road through the gate
            };
            var dirs = new[] { Vector3.up, Vector3.forward, Vector3.back, Vector3.left, Vector3.right, Vector3.down };
            var cols = new Color[dirs.Length];
            var result = new ProbeCheck { stepMeters = step };
            double sum = 0;

            void Walk(Vector3 a, Vector3 b, ref float prev)
            {
                int n = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / step));
                for (int i = 0; i <= n; i++)
                {
                    Vector3 p = Vector3.Lerp(a, b, i / (float)n);
                    LightProbes.GetInterpolatedProbe(p, null, out var sh);
                    sh.Evaluate(dirs, cols);
                    float lum = 0f;
                    foreach (var c in cols) lum += c.r * 0.2126f + c.g * 0.7152f + c.b * 0.0722f;
                    lum /= cols.Length;
                    if (prev >= 0f)
                    {
                        float rel = Mathf.Abs(lum - prev) / Mathf.Max(Mathf.Max(lum, prev), 1e-4f);
                        sum += rel;
                        result.samples++;
                        if (rel > result.maxRelativeStep)
                        {
                            result.maxRelativeStep = rel;
                            result.worstAt = $"({p.x:0.0}, {p.y:0.0}, {p.z:0.0})";
                        }
                    }
                    prev = lum;
                }
            }

            foreach (var r in routes)
            {
                float prev = -1f;
                for (int i = 0; i < r.Length - 1; i++) Walk(r[i], r[i + 1], ref prev);
            }
            // full circle around the dummy at chest height (where the hero fights)
            {
                float prev = -1f;
                for (int i = 0; i < 64; i++)
                {
                    float a0 = i / 64f * Mathf.PI * 2f, a1 = (i + 1) / 64f * Mathf.PI * 2f;
                    Walk(d + new Vector3(Mathf.Cos(a0) * 1.4f, 1f, Mathf.Sin(a0) * 1.4f),
                         d + new Vector3(Mathf.Cos(a1) * 1.4f, 1f, Mathf.Sin(a1) * 1.4f), ref prev);
                }
            }
            result.meanRelativeStep = result.samples > 0 ? (float)(sum / result.samples) : 0f;
            Debug.Log(string.Format(CultureInfo.InvariantCulture, "[PerfHarness] probe continuity: {0} samples, max step {1:0.00}% at {2}",
                result.samples, result.maxRelativeStep * 100f, result.worstAt));
            return result;
        }

        static bool HasArg(string[] args, string name)
        {
            foreach (var a in args) if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static string GetArg(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }
    }
}
