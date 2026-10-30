using UnityEngine;

namespace DuskYard.Player
{
    /// <summary>
    /// Minimal third-person locomotion on a CharacterController: camera-relative movement, walk/run,
    /// gravity and a procedural bob/lean on the visual root (no animator by design).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class ThirdPersonController : MonoBehaviour
    {
        [SerializeField] PlayerInputReader input;
        [SerializeField] Transform cameraTransform;
        [SerializeField] Transform visualRoot;

        [Header("Locomotion")]
        [SerializeField] float walkSpeed = 2.3f;
        [SerializeField] float runSpeed = 4.6f;
        [SerializeField] float acceleration = 14f;
        [SerializeField] float turnSpeed = 720f;
        [SerializeField] float gravity = -18f;

        [Header("Procedural motion")]
        [SerializeField] float bobAmplitude = 0.035f;
        [SerializeField] float bobFrequency = 1.9f;
        [SerializeField] float leanDegrees = 6f;

        CharacterController _cc;
        Vector3 _planarVelocity;
        float _verticalVelocity;
        float _bobPhase;
        Vector3 _visualBaseLocalPos;

        /// <summary>Multiplier applied to the target speed (melee attack slows the character down).</summary>
        public float SpeedMultiplier { get; set; } = 1f;
        /// <summary>While true the character keeps its facing (attack commits to a direction).</summary>
        public bool RotationLocked { get; set; }
        public Vector3 Velocity => _planarVelocity;
        public PlayerInputReader Input => input;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            if (visualRoot) _visualBaseLocalPos = visualRoot.localPosition;
            if (!cameraTransform && Camera.main) cameraTransform = Camera.main.transform;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            Vector2 move = input ? input.Move : Vector2.zero;

            Vector3 fwd = cameraTransform ? Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized : Vector3.forward;
            if (fwd.sqrMagnitude < 1e-4f) fwd = transform.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 wishDir = fwd * move.y + right * move.x;

            float speed = (input && input.Sprint ? runSpeed : walkSpeed) * SpeedMultiplier;
            Vector3 targetVel = wishDir * speed;
            _planarVelocity = Vector3.MoveTowards(_planarVelocity, targetVel, acceleration * speed * dt + acceleration * 0.1f * dt);

            if (!RotationLocked && wishDir.sqrMagnitude > 0.01f)
            {
                Quaternion targetRot = Quaternion.LookRotation(wishDir, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeed * dt);
            }

            if (_cc.isGrounded && _verticalVelocity < 0f) _verticalVelocity = -2f;
            _verticalVelocity += gravity * dt;

            _cc.Move((_planarVelocity + Vector3.up * _verticalVelocity) * dt);
            UpdateProceduralMotion(dt);
        }

        void UpdateProceduralMotion(float dt)
        {
            if (!visualRoot) return;
            float speed01 = Mathf.Clamp01(_planarVelocity.magnitude / runSpeed);
            _bobPhase += dt * bobFrequency * Mathf.Lerp(1f, 1.6f, speed01) * Mathf.PI * 2f * (speed01 > 0.02f ? 1f : 0f);
            float bob = Mathf.Abs(Mathf.Sin(_bobPhase)) * bobAmplitude * speed01;
            visualRoot.localPosition = _visualBaseLocalPos + Vector3.up * bob;

            Vector3 localVel = transform.InverseTransformDirection(_planarVelocity);
            Quaternion lean = Quaternion.Euler(localVel.z / runSpeed * leanDegrees, 0f, -localVel.x / runSpeed * leanDegrees);
            visualRoot.localRotation = Quaternion.Slerp(visualRoot.localRotation, lean, 1f - Mathf.Exp(-10f * dt));
        }

        /// <summary>Instantly turns towards a point (clamped), used by the attack's soft auto-aim.</summary>
        public void FaceTowards(Vector3 worldPoint, float maxDegrees)
        {
            Vector3 dir = Vector3.ProjectOnPlane(worldPoint - transform.position, Vector3.up);
            if (dir.sqrMagnitude < 1e-4f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), maxDegrees);
        }

        public void Teleport(Vector3 position, Quaternion rotation)
        {
            _cc.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            _planarVelocity = Vector3.zero;
            _cc.enabled = true;
        }
    }
}
