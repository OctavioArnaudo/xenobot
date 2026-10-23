using UnityEngine;
using Unity.Netcode;
using Runtime.Scripts.Optional;
using Runtime.Scripts.Health;

namespace Runtime.Scripts.Hook
{
    public interface IStunnable
    {
        void ApplyStun(float duration);
        bool IsStunned { get; }
    }

    public interface IHookable
    {
        void OnHookGrabbed();
        void OnHookReleased();
    }

    [RequireComponent(typeof(Collider))]
    public class HookProjectileController : NetworkBehaviour
    {
        private const float DEFAULT_STUN_DURATION = 2.5f;

        [Header("Hook Settings")]
        public float flySpeed = 35f;
        public float maxDistance = 30f;
        public float pullSpeed = 22f;
        public float stopDistance = 1.8f;

        [Header("Stun Settings")]
        public float StunDuration;
        public Optional<float> stunDuration;

        [Header("Line Visuals")]
        public LineRenderer lineRenderer;

        [Header("Audio & VFX")]
        public AudioClip hookHitSound;
        public ParticleSystem hitVfxPrefab;
        public ParticleSystem stunVfxPrefab;

        private GameObject m_OwnerPlayer;
        private Transform m_MuzzleTransform;
        private Vector3 m_StartPos;
        private Vector3 m_Direction;

        private Transform m_CaughtTarget;
        private bool m_IsPulling = false;
        private bool m_HasHit = false;

        private CharacterController m_TargetCC;
        private UnityEngine.AI.NavMeshAgent m_TargetAgent;
        private Rigidbody m_TargetRB;
        private IHookable m_HookableTarget;
        private IStunnable m_StunnableTarget;

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

        public float EffectiveStunDuration
        {
            get
            {
                try { return stunDuration.GetValue(DEFAULT_STUN_DURATION); }
                catch (System.Exception ex) { Debug.LogWarning($"[Fallback] StunDuration: {ex.Message}"); }
                return DEFAULT_STUN_DURATION;
            }
        }

        private void UpdateInspectorValues()
        {
            StunDuration = EffectiveStunDuration;
        }

        private void OnValidate()
        {
            UpdateInspectorValues();
        }

        public void Launch(GameObject owner, Vector3 direction, Transform muzzleTransform)
        {
            m_OwnerPlayer = owner;
            m_Direction = direction.normalized;
            m_MuzzleTransform = muzzleTransform;
            m_StartPos = transform.position;

            transform.rotation = Quaternion.LookRotation(m_Direction);

            if (lineRenderer == null) lineRenderer = GetComponent<LineRenderer>();
            if (lineRenderer != null)
            {
                lineRenderer.positionCount = 2;
                lineRenderer.enabled = true;
            }
        }

        private void Update()
        {
            UpdateInspectorValues();
            UpdateLineVisuals();

            if (!m_HasHit)
            {
                transform.position += m_Direction * (flySpeed * Time.deltaTime);

                if (Vector3.Distance(m_StartPos, transform.position) >= maxDistance)
                {
                    DestroyHook();
                }
            }
            else if (m_IsPulling && m_CaughtTarget != null && m_OwnerPlayer != null)
            {
                Vector3 playerPos = m_OwnerPlayer.transform.position + Vector3.up * 0.9f;
                Vector3 currentTargetPos = m_CaughtTarget.position;
                Vector3 pullDirection = (playerPos - currentTargetPos);
                float distance = pullDirection.magnitude;

                if (distance <= stopDistance)
                {
                    ReleaseTargetAndDestroy();
                    return;
                }

                Vector3 moveStep = pullDirection.normalized * (pullSpeed * Time.deltaTime);

                if (m_TargetCC != null)
                {
                    m_TargetCC.Move(moveStep);
                }
                else if (m_TargetAgent != null && m_TargetAgent.isOnNavMesh)
                {
                    m_TargetAgent.Warp(currentTargetPos + moveStep);
                }
                else
                {
                    m_CaughtTarget.position += moveStep;
                }

                transform.position = m_CaughtTarget.position + Vector3.up * 0.8f;
            }
        }

        private void UpdateLineVisuals()
        {
            if (lineRenderer == null) return;

            Vector3 startPoint = m_MuzzleTransform != null
                ? m_MuzzleTransform.position
                : (m_OwnerPlayer != null ? m_OwnerPlayer.transform.position + Vector3.up * 1f : transform.position);

            lineRenderer.SetPosition(0, startPoint);
            lineRenderer.SetPosition(1, transform.position);
        }

        private void OnTriggerEnter(Collider other) => TryAttachHook(other.gameObject, other.ClosestPoint(transform.position));
        private void OnCollisionEnter(Collision collision) => TryAttachHook(collision.gameObject, collision.contacts.Length > 0 ? collision.contacts[0].point : transform.position);

        private void TryAttachHook(GameObject hitObj, Vector3 impactPoint)
        {
            if (m_HasHit) return;
            if (hitObj == m_OwnerPlayer || hitObj.transform.IsChildOf(m_OwnerPlayer.transform)) return;

            m_HasHit = true;

            HealthController health = hitObj.GetComponentInParent<HealthController>() ?? hitObj.GetComponent<HealthController>();

            if (health != null && health.CurrentHP > 0)
            {
                m_CaughtTarget = health.transform;
                m_IsPulling = true;

                DisableTargetMovement(m_CaughtTarget.gameObject);

                if (hookHitSound != null) AudioSource.PlayClipAtPoint(hookHitSound, impactPoint);
                if (hitVfxPrefab != null) Instantiate(hitVfxPrefab, impactPoint, Quaternion.LookRotation(-m_Direction));
            }
            else
            {
                DestroyHook();
            }
        }

        private void DisableTargetMovement(GameObject target)
        {
            m_TargetCC = target.GetComponent<CharacterController>();
            m_TargetAgent = target.GetComponent<UnityEngine.AI.NavMeshAgent>();
            m_TargetRB = target.GetComponent<Rigidbody>();

            m_HookableTarget = target.GetComponentInParent<IHookable>() ?? target.GetComponent<IHookable>();
            m_StunnableTarget = target.GetComponentInParent<IStunnable>() ?? target.GetComponent<IStunnable>();

            if (m_HookableTarget != null)
            {
                m_HookableTarget.OnHookGrabbed();
            }

            if (m_TargetAgent != null && m_TargetAgent.enabled && m_TargetAgent.isOnNavMesh) m_TargetAgent.isStopped = true;
            if (m_TargetRB != null) m_TargetRB.isKinematic = true;
        }

        private void ReleaseTargetAndDestroy()
        {
            if (m_HookableTarget != null)
            {
                m_HookableTarget.OnHookReleased();
            }

            if (m_StunnableTarget != null)
            {
                m_StunnableTarget.ApplyStun(EffectiveStunDuration);
            }

            if (m_TargetAgent != null && m_TargetAgent.enabled && m_TargetAgent.isOnNavMesh) m_TargetAgent.isStopped = false;
            if (m_TargetRB != null) m_TargetRB.isKinematic = false;

            if (stunVfxPrefab != null && m_CaughtTarget != null)
            {
                Instantiate(stunVfxPrefab, m_CaughtTarget.position + Vector3.up * 1f, Quaternion.identity, m_CaughtTarget);
            }

            DestroyHook();
        }

        private void DestroyHook()
        {
            if (lineRenderer != null) lineRenderer.enabled = false;

            if (IsNetworkActive && IsServer)
            {
                if (TryGetComponent<NetworkObject>(out var netObj) && netObj.IsSpawned)
                    netObj.Despawn(true);
                else
                    Destroy(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }
    }
}