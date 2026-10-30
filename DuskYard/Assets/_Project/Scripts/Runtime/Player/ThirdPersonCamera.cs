using UnityEngine;

namespace DuskYard.Player
{
    /// <summary>
    /// Over-the-shoulder orbit camera with collision, optional target framing (lock-on) and a small impulse shake.
    /// Framing goal: hero, target and the impact zone stay inside the frame during a swing.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class ThirdPersonCamera : MonoBehaviour
    {
        public static ThirdPersonCamera Active { get; private set; }

        [SerializeField] Transform target;
        [SerializeField] PlayerInputReader input;
        [SerializeField] Transform focusTarget;

        [Header("Rig")]
        [SerializeField] float pivotHeight = 1.5f;
        [SerializeField] float shoulderOffset = 0.65f;
        [SerializeField] float distance = 3.6f;
        [SerializeField] float minPitch = -25f;
        [SerializeField] float maxPitch = 55f;
        [SerializeField] float followSharpness = 16f;

        [Header("Collision")]
        [SerializeField] LayerMask collisionMask = 1;
        [SerializeField] float collisionRadius = 0.22f;

        [Header("Framing")]
        [Tooltip("Lock-on keeps the focus target and the hero in frame (Q / Tab / MMB).")]
        [SerializeField] bool lockOn;
        [SerializeField] float lockOnMaxDistance = 12f;
        [SerializeField] float lockOnSharpness = 6f;
        [SerializeField] float lockOnPitch = 14f;

        float _yaw, _pitch = 12f, _currentDistance;
        Vector3 _smoothedPivot;
        float _shakeAmp, _shakeTime, _shakeDuration;

        public bool LockOn { get => lockOn; set => lockOn = value; }
        public Transform FocusTarget { get => focusTarget; set => focusTarget = value; }

        void OnEnable() { Active = this; }
        void OnDisable() { if (Active == this) Active = null; }

        void Start()
        {
            if (target)
            {
                _yaw = target.eulerAngles.y;
                _smoothedPivot = target.position + Vector3.up * pivotHeight;
            }
            _currentDistance = distance;
        }

        public void SnapBehindTarget()
        {
            if (!target) return;
            _yaw = target.eulerAngles.y;
            _smoothedPivot = target.position + Vector3.up * pivotHeight;
        }

        public void AddShake(float amplitude, float duration)
        {
            _shakeAmp = Mathf.Max(_shakeAmp * (1f - _shakeTime / Mathf.Max(_shakeDuration, 1e-3f)), amplitude);
            _shakeDuration = duration;
            _shakeTime = 0f;
        }

        void LateUpdate()
        {
            if (!target) return;
            float dt = Time.unscaledDeltaTime; // camera stays responsive during hit-stop

            if (input)
            {
                if (input.LockOnPressed) lockOn = !lockOn;
                _yaw += input.Look.x;
                _pitch = Mathf.Clamp(_pitch - input.Look.y, minPitch, maxPitch);
            }

            if (lockOn && focusTarget)
            {
                // aim from the shoulder point, not from the hero: the target lands in the centre of the frame and the
                // hero is pushed to the left third, so the impact zone is never hidden behind the character
                Vector3 shoulderPoint = target.position + Quaternion.Euler(0f, _yaw, 0f) * Vector3.right * shoulderOffset;
                Vector3 toFocus = focusTarget.position - shoulderPoint;
                toFocus.y = 0f;
                if (toFocus.magnitude < lockOnMaxDistance && toFocus.sqrMagnitude > 0.04f)
                {
                    float desiredYaw = Quaternion.LookRotation(toFocus).eulerAngles.y;
                    float k = 1f - Mathf.Exp(-lockOnSharpness * dt);
                    _yaw = Mathf.LerpAngle(_yaw, desiredYaw, k);
                    _pitch = Mathf.Lerp(_pitch, lockOnPitch, k);
                }
            }

            Vector3 pivotTarget = target.position + Vector3.up * pivotHeight;
            _smoothedPivot = Vector3.Lerp(_smoothedPivot, pivotTarget, 1f - Mathf.Exp(-followSharpness * dt));

            Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 shoulder = _smoothedPivot + rot * (Vector3.right * shoulderOffset);
            Vector3 back = rot * Vector3.back;

            float wanted = distance;
            if (Physics.SphereCast(shoulder, collisionRadius, back, out RaycastHit hit, distance, collisionMask, QueryTriggerInteraction.Ignore))
                wanted = Mathf.Max(0.6f, hit.distance - 0.05f);
            // pull in fast, ease out slowly to avoid popping
            _currentDistance = wanted < _currentDistance ? wanted : Mathf.Lerp(_currentDistance, wanted, 1f - Mathf.Exp(-4f * dt));

            Vector3 pos = shoulder + back * _currentDistance;
            Quaternion finalRot = rot;

            if (_shakeTime < _shakeDuration)
            {
                _shakeTime += dt;
                float fade = 1f - _shakeTime / _shakeDuration;
                float t = Time.unscaledTime * 38f;
                float a = _shakeAmp * fade * fade;
                pos += rot * new Vector3(Mathf.PerlinNoise(t, 0.3f) - 0.5f, Mathf.PerlinNoise(0.7f, t) - 0.5f, 0f) * a;
                finalRot *= Quaternion.Euler((Mathf.PerlinNoise(t, 3.1f) - 0.5f) * a * 20f, 0f, (Mathf.PerlinNoise(5.3f, t) - 0.5f) * a * 14f);
            }

            transform.SetPositionAndRotation(pos, finalRot);
        }
    }
}
