using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using Crafting.Scripts;

namespace Combating.Scripts
{
    /// <summary>
    /// Logic controller for shooting mechanics.
    /// Fully compliant with AGENTS.md pattern using Optional<T> for Clean Prefabs and dynamic fallback balance.
    /// </summary>
    public class ShootController : MonoBehaviour, IItemFunctional
    {
        // --- Internal Hardcoded Ranged Defaults ---
        private const float DEFAULT_PLAYER_SHOOT_DAMAGE = 32f;
        private const float DEFAULT_PLAYER_FIRE_RATE = 8.0f;
        private const float DEFAULT_PLAYER_AIM_DISTANCE = 120f;

        private const float DEFAULT_ENEMY_BASE_SHOOT_DAMAGE = 22f;
        private const float DEFAULT_ENEMY_BASE_FIRE_RATE = 5.0f;
        private const float DEFAULT_ENEMY_BASE_AIM_DISTANCE = 100f;

        [Header("References")]
        public bool isUnlocked = false; // Si está marcado, dispara desde el inicio. Si no, requiere arma.
        public Camera AimCamera;
        public GameObject Muzzle;
        public GameObject Projectile;
        public Renderer[] visualsToRotate;

        [Header("Manual Ranged Overrides")]
        public Optional<float> Damage;
        public Optional<float> FireRate;
        public Optional<float> AimDistance;
        public LayerMask AimLayers = ~0;
        public bool HoldToFire = true;
        public bool UsePlayerInput = true;

        [Header("Effects")]
        public ParticleSystem MuzzleFlash;
        public LineRenderer TracerPrefab;
        public float TracerLifetime = 0.05f;
        public float rotationSpeed = 10f;

        private PlayerController m_Player;
        private HealthController m_Health;
        private float m_NextFireTime;

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

        public GameObject OwnerEntity
        {
            get
            {
                if (m_Player != null) return m_Player.gameObject;
                if (m_Health != null) return m_Health.gameObject;
                return transform.root.gameObject;
            }
        }

        public Team EffectiveTeam => m_Health != null ? m_Health.team : (transform.root.CompareTag("Player") ? Team.Player : (transform.root.CompareTag("Enemy") ? Team.Enemy : Team.Neutral));

        // --- Effective Statistics Resolvers with Optional & Fallback Protection ---

        public float EffectiveDamage
        {
            get
            {
                try { return Damage.GetValue(CalculateDynamicShootDamage()); }
                catch (System.Exception ex) { Debug.LogWarning($"[Fallback] Damage: {ex.Message}"); }

                return CalculateDynamicShootDamage();
            }
        }

        public float EffectiveFireRate
        {
            get
            {
                try { return FireRate.GetValue(CalculateDynamicFireRate()); }
                catch (System.Exception ex) { Debug.LogWarning($"[Fallback] FireRate: {ex.Message}"); }

                return CalculateDynamicFireRate();
            }
        }

        public float EffectiveAimDistance
        {
            get
            {
                try
                {
                    var enemy = GetComponent<EnemyController>() ?? GetComponentInParent<EnemyController>();
                    float defaultDist = (enemy != null) ? DEFAULT_ENEMY_BASE_AIM_DISTANCE : DEFAULT_PLAYER_AIM_DISTANCE;
                    return AimDistance.GetValue(defaultDist);
                }
                catch (System.Exception ex) { Debug.LogWarning($"[Fallback] AimDistance: {ex.Message}"); }

                return DEFAULT_PLAYER_AIM_DISTANCE;
            }
        }

        private float CalculateDynamicShootDamage()
        {
            var pc = (m_Player != null) ? m_Player : (GetComponent<PlayerController>() ?? GetComponentInParent<PlayerController>());
            if (pc != null || CompareTag("Player"))
            {
                return DEFAULT_PLAYER_SHOOT_DAMAGE;
            }

            var enemy = GetComponent<EnemyController>() ?? GetComponentInParent<EnemyController>();
            if (enemy != null)
            {
                float dmg = DEFAULT_ENEMY_BASE_SHOOT_DAMAGE;

                switch (enemy.activeArchetype)
                {
                    case AIArchetype.KiterHitAndRun:
                    case AIArchetype.TacticalCover:
                        dmg *= 1.3f;
                        break;
                    case AIArchetype.Berserker:
                        dmg *= 1.5f;
                        break;
                    case AIArchetype.AmbushStalker:
                        dmg *= 1.6f;
                        break;
                    case AIArchetype.SupportCommander:
                        dmg *= 1.1f;
                        break;
                    default:
                        dmg *= 1.0f;
                        break;
                }

                int allies = CountNearbyAllies();
                if (allies >= 3) dmg *= 0.75f;

                return dmg;
            }

            return DEFAULT_ENEMY_BASE_SHOOT_DAMAGE;
        }

        private float CalculateDynamicFireRate()
        {
            var pc = (m_Player != null) ? m_Player : (GetComponent<PlayerController>() ?? GetComponentInParent<PlayerController>());
            if (pc != null || CompareTag("Player"))
            {
                return DEFAULT_PLAYER_FIRE_RATE;
            }

            var enemy = GetComponent<EnemyController>() ?? GetComponentInParent<EnemyController>();
            if (enemy != null)
            {
                switch (enemy.activeArchetype)
                {
                    case AIArchetype.Berserker: return 10.0f;
                    case AIArchetype.KiterHitAndRun: return 6.5f;
                    case AIArchetype.TacticalCover: return 6.0f;
                    case AIArchetype.SupportCommander: return 4.0f;
                    default: return DEFAULT_ENEMY_BASE_FIRE_RATE;
                }
            }

            return DEFAULT_ENEMY_BASE_FIRE_RATE;
        }

        private int CountNearbyAllies()
        {
            int count = 0;
            var enemies = GameObject.FindGameObjectsWithTag("Enemy");
            foreach (var e in enemies)
            {
                if (e != gameObject && Vector3.Distance(transform.position, e.transform.position) <= 25f) count++;
            }
            return count;
        }

        void Awake()
        {
            RefreshReferences();
        }

        public void ApplyEffect(GameObject player)
        {
            m_Player = player.GetComponent<PlayerController>();
            m_Health = player.GetComponent<HealthController>();
            RefreshReferences();
            Debug.Log($"[ShootController] Vinculado a {player.name}. Player detected: {m_Player != null}");
        }

        private void RefreshReferences()
        {
            if (GetComponent<EnemyController>() == null && GetComponentInParent<EnemyController>() == null)
            {
                if (m_Player == null) m_Player = GetComponent<PlayerController>() ?? GetComponentInParent<PlayerController>();
            }
            else
            {
                m_Player = null;
            }

            if (m_Health == null)
            {
                m_Health = GetComponent<HealthController>() ??
                           GetComponentInParent<HealthController>() ??
                           GetComponentInChildren<HealthController>();
            }

            if (AimCamera == null && m_Player != null)
                AimCamera = m_Player.GetComponentInChildren<Camera>();

            if (AimCamera == null) AimCamera = Camera.main;

            if (visualsToRotate == null || visualsToRotate.Length == 0)
            {
                var mr = GetComponentInChildren<MeshRenderer>();
                if (mr != null) visualsToRotate = new Renderer[] { mr };
            }
        }

        void Update()
        {
            if (!isUnlocked || !UsePlayerInput) return;

            bool canHandleInput = (m_Player != null) ? (IsNetworkActive ? m_Player.IsOwner : true) : true;
            if (!canHandleInput) return;

            if (m_Player == null) RefreshReferences();

            if (m_Player != null && WantsToFire())
            {
                TryFire();
            }
        }

        bool WantsToFire()
        {
            if (m_Player == null) return false;

            if (InventoryController.LocalInstance != null && Cursor.visible) return false;

            bool inputActive = false;

            if (Mouse.current != null)
            {
                inputActive = HoldToFire
                    ? Mouse.current.rightButton.isPressed
                    : Mouse.current.rightButton.wasPressedThisFrame;
            }

            if (!inputActive && m_Player != null)
            {
                inputActive = m_Player.aim;
            }

            return inputActive;
        }

        public bool TryFire()
        {
            if (Projectile == null || Muzzle == null)
            {
                RefreshReferences();
                if (Projectile == null || Muzzle == null) return false;
            }

            if (Time.time < m_NextFireTime) return false;
            m_NextFireTime = Time.time + 1f / Mathf.Max(0.01f, EffectiveFireRate);

            Vector3 originPos = Muzzle.transform.position;
            Vector3 direction = GetAimDirection(originPos);
            ExecuteFire(direction, originPos);
            return true;
        }

        public bool FireAt(Vector3 targetPosition)
        {
            if (Projectile == null || Muzzle == null)
            {
                RefreshReferences();
                if (Projectile == null || Muzzle == null) return false;
            }

            if (Time.time < m_NextFireTime) return false;
            m_NextFireTime = Time.time + 1f / Mathf.Max(0.01f, EffectiveFireRate);

            RotateVisualsTowards(targetPosition);

            Vector3 originPos = Muzzle.transform.position;
            Vector3 direction = (targetPosition - originPos);
            if (direction.sqrMagnitude < 0.01f) direction = transform.forward;
            else direction = direction.normalized;

            ExecuteFire(direction, originPos);
            return true;
        }

        private void ExecuteFire(Vector3 direction, Vector3 spawnPos)
        {
            float finalDamage = EffectiveDamage;
            Team team = EffectiveTeam;

            if (m_Player != null && IsNetworkActive)
            {
                m_Player.RequestFire(Projectile, direction, spawnPos, finalDamage, team);
            }
            else
            {
                SpawnProjectileLocally(direction, spawnPos, finalDamage, team, IsNetworkActive && NetworkManager.Singleton.IsServer);
            }

            Animator anim = GetComponentInChildren<Animator>();
            if (anim != null && HasParameter(anim, "shoot"))
            {
                anim.SetTrigger("shoot");
            }

            if (MuzzleFlash != null) MuzzleFlash.Play();
        }

        private Vector3 GetAimDirection(Vector3 muzzlePos)
        {
            if (AimCamera == null) return transform.forward;

            Ray ray = AimCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));

            RaycastHit[] hits = Physics.RaycastAll(ray, EffectiveAimDistance, AimLayers);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            Vector3 targetPoint = ray.GetPoint(EffectiveAimDistance);
            Transform rootTransform = OwnerEntity.transform;

            foreach (var hit in hits)
            {
                if (hit.transform.root == rootTransform) continue;
                if (hit.collider.isTrigger) continue;

                targetPoint = hit.point;
                break;
            }

            Vector3 dir = (targetPoint - muzzlePos);
            if (dir.sqrMagnitude < 0.01f) return AimCamera.transform.forward;
            return dir.normalized;
        }

        private void SpawnProjectileLocally(Vector3 direction, Vector3 spawnPos, float damage, Team team, bool isServer)
        {
            GameObject projGO = Instantiate(Projectile, spawnPos, Quaternion.LookRotation(direction));

            var proj = projGO.GetComponent<ProjectileController>();
            if (proj != null)
            {
                proj.Launch(OwnerEntity, direction, damage, team);
            }

            if (isServer)
            {
                var netObj = projGO.GetComponent<NetworkObject>();
                if (netObj != null) netObj.Spawn();
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

        private bool HasParameter(Animator animator, string paramName)
        {
            foreach (AnimatorControllerParameter param in animator.parameters)
                if (param.name == paramName) return true;
            return false;
        }
    }
}
