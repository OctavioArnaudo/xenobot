using UnityEngine;
using Unity.Netcode;
using Crafting.Scripts;

namespace Combating.Scripts
{
    public class MeleeController : MonoBehaviour, IModular
    {
        [Header("Settings")]
        public float attackRange = 2.5f;
        public float attackDamage = 35f;
        public float attackCooldown = 1f;
        public LayerMask targetLayers = 72; // Default to Player (3) and Enemy (6)

        [Header("Visuals")]
        public ProjectileController swingVfxPrefab;
        public Renderer[] visualsToRotate;
        public float rotationSpeed = 10f;

        private HealthController m_Health;
        private ModularController _hub;
        private AnimationController _anim;
        private float m_NextAttackTime;

        void Awake()
        {
            if (_hub == null) _hub = GetComponentInParent<ModularController>();

            if (visualsToRotate == null || visualsToRotate.Length == 0)
                visualsToRotate = GetComponentsInChildren<Renderer>();
        }

        public void Bind(ModularController hub)
        {
            _hub = hub;
            if (_hub != null)
            {
                _hub.RegisterModule(this);

                if (_hub is PlayerController) attackDamage = Random.Range(35f, 51f);
                else if (_hub is EnemyController) attackDamage = Random.Range(15f, 26f);

                OnRefreshModule();
            }
        }

        public void OnRefreshModule()
        {
            if (_hub != null)
            {
                m_Health = _hub.GetModule<HealthController>();
                _anim = _hub.GetModule<AnimationController>();
                visualsToRotate = _hub.renderRoot?.GetComponentsInChildren<Renderer>() ?? GetComponentsInChildren<Renderer>();
            }
        }

        private void Update()
        {
            if (_hub != null && _hub is PlayerController player && player.fire && Time.time >= m_NextAttackTime)
            {
                PerformMeleeAction();
                player.fire = false;
            }
        }

        public void PerformMeleeAction(Vector3? targetPosition = null)
        {
            if (Time.time < m_NextAttackTime) return;

            if (targetPosition.HasValue)
            {
                RotateVisualsTowards(targetPosition.Value);
            }

            m_NextAttackTime = Time.time + attackCooldown;

            if (_anim != null) _anim.TriggerMeleeAttack();

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                if (_hub.IsOwner) _hub.RequestMeleeServerRpc();
            }
            else
            {
                ExecuteMeleeServerSide();
            }
        }

        private void RotateVisualsTowards(Vector3 targetPosition)
        {
            if (visualsToRotate == null) return;
            Vector3 direction = (targetPosition - transform.position).normalized;
            if (direction.sqrMagnitude > 0.001f)
            {
                Quaternion targetFullRotation = Quaternion.LookRotation(direction);
                foreach (var r in visualsToRotate)
                {
                    if (r != null)
                        r.transform.rotation = Quaternion.Slerp(r.transform.rotation, targetFullRotation, rotationSpeed * Time.deltaTime);
                }
            }
        }

        public void ExecuteMeleeServerSide()
        {
            float finalDamage = attackDamage;
            if (_hub != null)
            {
                finalDamage = attackDamage * (_hub.Attack.Value / 10f);
            }

            Vector3 attackCenter = transform.position + transform.forward * (attackRange * 0.5f);
            Collider[] hits = Physics.OverlapSphere(attackCenter, attackRange, targetLayers);

            foreach (Collider hit in hits)
            {
                var targetHealth = hit.GetComponentInParent<HealthController>();
                if (targetHealth != null)
                {
                    if (targetHealth.team == _hub.MyTeam && _hub.MyTeam != Team.Neutral) continue;
                    var targetDamage = hit.GetComponentInParent<DamageController>();
                    if (targetDamage != null) targetDamage.TakeDamage((int)finalDamage, _hub.MyTeam);
                }
            }

            if (swingVfxPrefab != null)
            {
                ProjectileController vfx = Instantiate(swingVfxPrefab, transform.position + transform.forward, transform.rotation);
                vfx.Launch(_hub.gameObject, transform.forward, 0f, _hub.MyTeam);
            }
        }
    }
}
