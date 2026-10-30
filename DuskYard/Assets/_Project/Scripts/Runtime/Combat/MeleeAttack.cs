using System;
using DuskYard.Player;
using DuskYard.Target;
using DuskYard.VFX;
using UnityEngine;

namespace DuskYard.Combat
{
    /// <summary>
    /// Procedural two-swing combo (forehand / backhand) without an animator.
    /// Phases: Windup -> Active (blade sweep collision) -> Recovery. One contact per swing.
    /// Contact is detected by sphere-sweeping several points of the blade between the previous and the current frame,
    /// so the reported point and direction match what the player sees, independent of frame rate.
    /// </summary>
    public sealed class MeleeAttack : MonoBehaviour
    {
        enum Phase { Idle, Windup, Active, Recovery }

        [Serializable]
        struct Swing
        {
            public Vector3 windup;      // weapon pivot local euler at the end of windup
            public Vector3 follow;      // weapon pivot local euler at the end of the active window
        }

        [Header("References")]
        [SerializeField] PlayerInputReader input;
        [SerializeField] ThirdPersonController controller;
        [SerializeField] Transform weaponPivot;
        [SerializeField] Transform bladeBase;
        [SerializeField] Transform bladeTip;
        [SerializeField] TrailRenderer trail;
        [SerializeField] ParticleSystem contactCueGlint;

        [Header("Timing (seconds)")]
        [SerializeField] float windupTime = 0.17f;
        [SerializeField] float activeTime = 0.11f;
        [SerializeField] float recoveryTime = 0.30f;
        [SerializeField] float inputBufferTime = 0.25f;

        [Header("Poses")]
        [SerializeField] Vector3 restPose = new Vector3(38f, 28f, 0f);
        [SerializeField] Swing[] swings =
        {
            new Swing { windup = new Vector3(-8f, 100f, -35f), follow = new Vector3(22f, -85f, 25f) },
            new Swing { windup = new Vector3(4f, -95f, 35f), follow = new Vector3(18f, 90f, -25f) },
        };

        [Header("Collision")]
        [SerializeField] LayerMask hitMask = ~0;
        [SerializeField] float bladeRadius = 0.06f;
        [SerializeField] int bladeSamples = 4;

        [Header("Assist")]
        [SerializeField] float autoAimRange = 3.0f;
        [SerializeField] float autoAimMaxAngle = 80f;
        [SerializeField, Range(0f, 1f)] float moveSpeedWhileAttacking = 0.25f;

        Phase _phase;
        float _phaseTime;
        int _swingIndex;
        float _bufferedUntil = -1f;
        bool _hitThisSwing;
        Vector3 _prevBase, _prevTip;
        Vector3 _poseFrom;
        readonly RaycastHit[] _hits = new RaycastHit[8];

        public event Action<HitInfo> Hit;
        public bool IsAttacking => _phase != Phase.Idle;
        public int SwingsStarted { get; private set; }
        public int ContactsMade { get; private set; }

        void Start()
        {
            if (weaponPivot) weaponPivot.localRotation = Quaternion.Euler(restPose);
            if (trail) trail.emitting = false;
        }

        void Update()
        {
            if (input && input.AttackPressed) _bufferedUntil = Time.time + inputBufferTime;

            switch (_phase)
            {
                case Phase.Idle:
                    if (_bufferedUntil >= Time.time) BeginSwing(fromCombo: false);
                    break;
                case Phase.Windup:
                    Advance(windupTime, Phase.Active);
                    break;
                case Phase.Active:
                    Advance(activeTime, Phase.Recovery);
                    break;
                case Phase.Recovery:
                    // combo: a buffered press during the second half of recovery chains into the next swing
                    if (_bufferedUntil >= Time.time && _phaseTime > recoveryTime * 0.35f) { BeginSwing(fromCombo: true); break; }
                    Advance(recoveryTime, Phase.Idle);
                    break;
            }

            ApplyPose();
            if (_phase == Phase.Active) SweepBlade();
            CacheBlade();
        }

        void BeginSwing(bool fromCombo)
        {
            _bufferedUntil = -1f;
            _swingIndex = fromCombo ? (_swingIndex + 1) % swings.Length : 0;
            _poseFrom = weaponPivot ? NormalizeEuler(weaponPivot.localEulerAngles) : restPose;
            _phase = Phase.Windup;
            _phaseTime = 0f;
            _hitThisSwing = false;
            SwingsStarted++;
            if (controller)
            {
                controller.SpeedMultiplier = moveSpeedWhileAttacking;
                var dummy = TrainingDummy.FindNearest(transform.position, autoAimRange);
                if (dummy)
                {
                    Vector3 to = Vector3.ProjectOnPlane(dummy.transform.position - transform.position, Vector3.up);
                    if (Vector3.Angle(transform.forward, to) < autoAimMaxAngle) controller.FaceTowards(dummy.transform.position, 180f);
                }
                controller.RotationLocked = true;
            }
        }

        void Advance(float duration, Phase next)
        {
            _phaseTime += Time.deltaTime;
            if (_phaseTime < duration) return;
            _phaseTime -= duration;
            _phase = next;
            if (next == Phase.Active) CacheBlade(); // sweep starts from the exact windup pose
            if (next == Phase.Idle)
            {
                _phaseTime = 0f;
                if (controller) { controller.SpeedMultiplier = 1f; controller.RotationLocked = false; }
            }
        }

        void ApplyPose()
        {
            if (!weaponPivot) return;
            Swing s = swings[_swingIndex];
            Vector3 euler;
            bool trailOn = false;
            switch (_phase)
            {
                case Phase.Windup:
                {
                    float t = EaseOutCubic(_phaseTime / windupTime);
                    euler = Vector3.Lerp(_poseFrom, s.windup, t);
                    trailOn = t > 0.75f;
                    if (contactCueGlint && _phaseTime / windupTime > 0.6f && !contactCueGlint.isEmitting && !_glintPlayed)
                    {
                        contactCueGlint.Play(true);
                        _glintPlayed = true;
                    }
                    break;
                }
                case Phase.Active:
                    euler = Vector3.Lerp(s.windup, s.follow, EaseInOutQuad(_phaseTime / activeTime));
                    trailOn = true;
                    break;
                case Phase.Recovery:
                {
                    float t = EaseInOutQuad(_phaseTime / recoveryTime);
                    euler = Vector3.Lerp(s.follow, restPose, t);
                    trailOn = t < 0.2f;
                    break;
                }
                default:
                    euler = restPose;
                    _glintPlayed = false;
                    break;
            }
            if (_phase != Phase.Windup) _glintPlayed = false;
            weaponPivot.localRotation = Quaternion.Euler(euler);
            if (trail) trail.emitting = trailOn;
        }

        bool _glintPlayed;

        void SweepBlade()
        {
            if (_hitThisSwing || !bladeBase || !bladeTip) return;
            Vector3 curBase = bladeBase.position, curTip = bladeTip.position;
            float bestScore = float.MaxValue;
            HitInfo best = default;
            bool found = false;

            for (int i = 0; i < bladeSamples; i++)
            {
                float f = bladeSamples == 1 ? 1f : (float)i / (bladeSamples - 1);
                Vector3 p0 = Vector3.Lerp(_prevBase, _prevTip, f);
                Vector3 p1 = Vector3.Lerp(curBase, curTip, f);
                Vector3 delta = p1 - p0;
                float dist = delta.magnitude;
                if (dist < 1e-4f) continue;
                int n = Physics.SphereCastNonAlloc(p0, bladeRadius, delta / dist, _hits, dist, hitMask, QueryTriggerInteraction.Ignore);
                for (int h = 0; h < n; h++)
                {
                    var rh = _hits[h];
                    if (rh.collider.transform.IsChildOf(transform)) continue;
                    // distance 0 means the sphere started inside the collider: use the closest point instead
                    Vector3 point = rh.distance <= 0f ? rh.collider.ClosestPoint(p0) : rh.point;
                    Vector3 normal = rh.distance <= 0f ? (p0 - point).normalized : rh.normal;
                    if (normal.sqrMagnitude < 0.5f) normal = -delta / dist;
                    // prefer the earliest contact along the sweep, then the outer part of the blade
                    float score = rh.distance - f * 0.05f;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        found = true;
                        best = new HitInfo
                        {
                            Point = point,
                            Normal = normal,
                            Direction = delta / dist,
                            Speed = dist / Mathf.Max(Time.deltaTime, 1e-4f),
                            Collider = rh.collider,
                            Surface = HitSurface.Resolve(rh.collider),
                            SwingIndex = _swingIndex,
                        };
                    }
                }
            }

            if (!found) return;
            _hitThisSwing = true;
            ContactsMade++;
            var hittable = best.Collider.GetComponentInParent<IHittable>();
            hittable?.ReceiveHit(best);
            if (HitFeedbackSystem.Instance) HitFeedbackSystem.Instance.Play(best);
            Hit?.Invoke(best);
        }

        void CacheBlade()
        {
            if (!bladeBase || !bladeTip) return;
            _prevBase = bladeBase.position;
            _prevTip = bladeTip.position;
        }

        static Vector3 NormalizeEuler(Vector3 e) =>
            new Vector3(Mathf.DeltaAngle(0f, e.x), Mathf.DeltaAngle(0f, e.y), Mathf.DeltaAngle(0f, e.z));
        static float EaseOutCubic(float t) { t = Mathf.Clamp01(t); return 1f - (1f - t) * (1f - t) * (1f - t); }
        static float EaseInOutQuad(float t) { t = Mathf.Clamp01(t); return t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) * 0.5f; }
    }
}
