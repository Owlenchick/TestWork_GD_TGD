using System;
using System.Collections.Generic;
using DuskYard.Combat;
using UnityEngine;

namespace DuskYard.Target
{
    public enum DummyState { Ready, Hit, Recovering }

    /// <summary>
    /// Straw training dummy with three readable states:
    ///  Ready      - upright, ember ward sigil on the chest glows and slowly pulses.
    ///  Hit        - spring kick away from the blade, straw flashes (SG _HitFlash), sigil burns out (dissolve 0 -> 1).
    ///  Recovering - swings back upright, sigil re-forms behind a glowing burn front (dissolve 1 -> 0).
    /// All visual changes go through SG_DissolveEmissiveLit parameters on per-renderer material instances
    /// (instances keep SRP Batcher compatibility, unlike MaterialPropertyBlocks).
    /// </summary>
    public sealed class TrainingDummy : MonoBehaviour, IHittable
    {
        static readonly List<TrainingDummy> s_All = new List<TrainingDummy>();
        public static IReadOnlyList<TrainingDummy> All => s_All;

        static readonly int HitFlashId = Shader.PropertyToID("_HitFlash");
        static readonly int DissolveId = Shader.PropertyToID("_DissolveAmount");

        [SerializeField] Transform tiltPivot;
        [SerializeField] Renderer[] flashRenderers;
        [SerializeField] Renderer sigilRenderer;

        [Header("Spring")]
        [SerializeField] float stiffness = 85f;
        [SerializeField] float damping = 8.5f;
        [SerializeField] float impulse = 260f;          // deg/s added per hit
        [SerializeField] float maxTilt = 24f;

        [Header("State timing (s)")]
        [SerializeField] float hitHold = 0.55f;
        [SerializeField] float burnOutTime = 0.22f;
        [SerializeField] float recoverTime = 1.1f;
        [SerializeField] float flashDecay = 0.16f;

        Material[] _flashMats;
        Material _sigilMat;
        Vector2 _tilt, _tiltVel;     // x = pitch (around local X), y = roll (around local Z)
        float _flash, _dissolve, _stateTime;

        public DummyState State { get; private set; } = DummyState.Ready;
        public int HitsTaken { get; private set; }
        public event Action<DummyState> StateChanged;

        public static TrainingDummy FindNearest(Vector3 from, float maxDistance)
        {
            TrainingDummy best = null;
            float bestSq = maxDistance * maxDistance;
            foreach (var d in s_All)
            {
                float sq = (d.transform.position - from).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = d; }
            }
            return best;
        }

        void Awake()
        {
            _flashMats = new Material[flashRenderers.Length];
            for (int i = 0; i < flashRenderers.Length; i++) _flashMats[i] = flashRenderers[i].material;
            if (sigilRenderer) _sigilMat = sigilRenderer.material;
            ApplyMaterials();
        }

        void OnEnable() => s_All.Add(this);
        void OnDisable() => s_All.Remove(this);

        void OnDestroy()
        {
            if (_flashMats != null) foreach (var m in _flashMats) Destroy(m);
            if (_sigilMat) Destroy(_sigilMat);
        }

        public void ReceiveHit(in HitInfo hit)
        {
            HitsTaken++;
            Vector3 dir = Vector3.ProjectOnPlane(hit.Direction, Vector3.up);
            if (dir.sqrMagnitude < 1e-4f) dir = Vector3.ProjectOnPlane(-hit.Normal, Vector3.up);
            dir.Normalize();
            // tilt away from the blade: push direction expressed in the pivot's local frame
            Vector3 local = tiltPivot ? tiltPivot.parent.InverseTransformDirection(dir) : dir;
            _tiltVel += new Vector2(local.z, -local.x) * impulse;
            _flash = 1f;
            SetState(DummyState.Hit);
        }

        void SetState(DummyState s)
        {
            State = s;
            _stateTime = 0f;
            StateChanged?.Invoke(s);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            _stateTime += dt;

            // damped spring back to upright
            Vector2 acc = -stiffness * _tilt - damping * _tiltVel;
            _tiltVel += acc * dt;
            _tilt += _tiltVel * dt;
            if (_tilt.magnitude > maxTilt) { _tilt = _tilt.normalized * maxTilt; _tiltVel *= 0.5f; }
            if (tiltPivot) tiltPivot.localRotation = Quaternion.Euler(_tilt.x, 0f, _tilt.y);

            _flash = Mathf.Max(0f, _flash - dt / flashDecay);

            switch (State)
            {
                case DummyState.Hit:
                    _dissolve = Mathf.MoveTowards(_dissolve, 1f, dt / burnOutTime);
                    if (_stateTime >= hitHold) SetState(DummyState.Recovering);
                    break;
                case DummyState.Recovering:
                    _dissolve = Mathf.MoveTowards(_dissolve, 0f, dt / recoverTime);
                    if (_dissolve <= 0f && _tilt.magnitude < 1.5f) SetState(DummyState.Ready);
                    break;
                default:
                    _dissolve = 0f;
                    break;
            }

            ApplyMaterials();
        }

        void ApplyMaterials()
        {
            float flash = _flash * _flash; // sharp attack, soft tail
            for (int i = 0; i < _flashMats.Length; i++) _flashMats[i].SetFloat(HitFlashId, flash);
            if (_sigilMat) _sigilMat.SetFloat(DissolveId, _dissolve);
        }
    }
}
