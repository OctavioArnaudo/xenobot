using UnityEngine;
using Unity.Netcode;

namespace Combating.Scripts
{
    [RequireComponent(typeof(Collider))]
    public class HookProjectileController : NetworkBehaviour
    {
        [Header("Hook Settings")]
        public float flySpeed = 35f;
        public float maxDistance = 30f;
        public float pullSpeed = 22f;
        public float stopDistance = 1.8f;

        [Header("Line Visuals")]
        public LineRenderer lineRenderer;

        [Header("Audio & VFX")]
        public AudioClip hookHitSound;
        public ParticleSystem hitVfxPrefab;

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
        private MonoBehaviour m_TargetEnemyScript;

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
            UpdateLineVisuals();

            if (!m_HasHit)
            {
                // VUELO
                transform.position += m_Direction * (flySpeed * Time.deltaTime);

                if (Vector3.Distance(m_StartPos, transform.position) >= maxDistance)
                {
                    DestroyHook();
                }
            }
            else if (m_IsPulling && m_CaughtTarget != null && m_OwnerPlayer != null)
            {
                // ARRASTRE
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
                else if (m_TargetAgent != null)
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

            // Búsqueda genérica compatible usando System.Type
            System.Type enemyType = System.Type.GetType("EnemyController") ?? System.Type.GetType("Combating.Scripts.EnemyController");
            if (enemyType != null)
            {
                m_TargetEnemyScript = target.GetComponent(enemyType) as MonoBehaviour ??
                                      target.GetComponentInParent(enemyType) as MonoBehaviour;
            }

            if (m_TargetAgent != null && m_TargetAgent.enabled) m_TargetAgent.isStopped = true;
            if (m_TargetRB != null) m_TargetRB.isKinematic = true;
            if (m_TargetEnemyScript != null) m_TargetEnemyScript.enabled = false;
        }

        private void ReleaseTargetAndDestroy()
        {
            if (m_TargetAgent != null && m_TargetAgent.enabled) m_TargetAgent.isStopped = false;
            if (m_TargetRB != null) m_TargetRB.isKinematic = false;
            if (m_TargetEnemyScript != null) m_TargetEnemyScript.enabled = true;

            DestroyHook();
        }

        private void DestroyHook()
        {
            if (lineRenderer != null) lineRenderer.enabled = false;
            Destroy(gameObject);
        }
    }
}