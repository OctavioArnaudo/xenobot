using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using Crafting.Scripts;
using Combating.Scripts;

namespace Testing.Scripts
{
    public class ShootController : MonoBehaviour, IItemFunctional, IModular
    {
        [Header("References")]
        public Camera AimCamera;
        public Transform Muzzle;
        public ProjectileController ProjectilePrefab;
        public Renderer[] visualsToRotate;

        [Header("Shooting")]
        public float Damage = 25f;
        public float FireRate = 6f;
        public float AimDistance = 100f;
        public LayerMask AimLayers = ~0;
        public bool HoldToFire = true;
        public bool UsePlayerInput = true;

        [Header("Effects")]
        public ParticleSystem MuzzleFlash;
        public LineRenderer TracerPrefab;
        public float TracerLifetime = 0.05f;
        public float rotationSpeed = 10f;

        private ModularController _hub;
        private HealthController m_Health;
        private AnimationController _anim;
        private float m_NextFireTime;

        void Awake()
        {
            if (_hub == null) _hub = GetComponentInParent<ModularController>();
        }

        public void ApplyEffect(GameObject player)
        {
            _hub = player.GetComponent<ModularController>();
            if (_hub != null) Bind(_hub);
        }

        public void Bind(ModularController hub)
        {
            _hub = hub;
            if (_hub != null)
            {
                _hub.RegisterModule(this);

                // Module Locking: Players start with shooting disabled
                if (_hub is PlayerController)
                {
                    Damage = Random.Range(35f, 51f);
                    enabled = false;
                }
                else if (_hub is EnemyController)
                {
                    Damage = Random.Range(15f, 26f);
                    enabled = true;
                }

                OnRefreshModule();
            }
        }

        public void OnRefreshModule()
        {
            if (_hub != null)
            {
                m_Health = _hub.GetModule<HealthController>();
                _anim = _hub.GetModule<AnimationController>();

                if (_hub.mainCamera != null) AimCamera = _hub.mainCamera.GetComponent<Camera>();
                if (AimCamera == null) AimCamera = _hub.GetComponentInChildren<Camera>();

                if (_hub.MuzzlePoint != null) Muzzle = _hub.MuzzlePoint;
                else if (_hub.activeModel != null)
                {
                    _hub.activeModel.EnsurePoints();
                    if (_hub.activeModel.muzzlePoint != null) Muzzle = _hub.activeModel.muzzlePoint;
                }
            }

            if (Muzzle == null || Muzzle == transform)
            {
                Muzzle = transform.Find("Muzzle") ?? transform.Find("FirePoint") ?? transform.Find("muzzlePoint") ?? transform;
            }
        }

        void Update()
        {
            if (!UsePlayerInput || _hub == null || !(_hub is PlayerController player)) return;

            bool isOwner = (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || _hub.IsOwner);
            if (!isOwner) return;

            if (WantsToFire(player))
            {
                TryFire();
            }
        }

        bool WantsToFire(PlayerController player)
        {
            bool inputActive = HoldToFire ? player.fireHeld : player.fire;
            if (!inputActive && Mouse.current != null)
            {
                inputActive = HoldToFire ? Mouse.current.leftButton.isPressed : Mouse.current.leftButton.wasPressedThisFrame;
            }
            return inputActive;
        }

        public bool TryFire()
        {
            if (ProjectilePrefab == null || Muzzle == null)
            {
                if (_hub != null) OnRefreshModule();
                if (ProjectilePrefab == null || Muzzle == null) return false;
            }

            if (Time.time < m_NextFireTime) return false;
            m_NextFireTime = Time.time + 1f / Mathf.Max(0.01f, FireRate);

            Vector3 originPos = Muzzle.position;
            Vector3 direction = GetAimDirection(originPos);
            ExecuteFire(direction, originPos);
            return true;
        }

        public bool FireAt(Vector3 targetPosition)
        {
            if (ProjectilePrefab == null || Muzzle == null)
            {
                if (_hub != null) OnRefreshModule();
                if (ProjectilePrefab == null || Muzzle == null) return false;
            }

            RotateVisualsTowards(targetPosition);

            if (Time.time < m_NextFireTime) return false;
            m_NextFireTime = Time.time + 1f / Mathf.Max(0.01f, FireRate);

            Vector3 originPos = Muzzle.position;
            Vector3 direction = (targetPosition - originPos).normalized;
            ExecuteFire(direction, originPos);
            return true;
        }

        private void ExecuteFire(Vector3 direction, Vector3 spawnPos)
        {
            float finalDamage = Damage;
            if (_hub != null) finalDamage = Damage * (_hub.Attack.Value / 10f);

            Team team = _hub != null ? _hub.MyTeam : Team.Neutral;

            if (_hub != null && _hub is PlayerController player && (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening))
            {
                player.RequestFire(ProjectilePrefab, direction, spawnPos, finalDamage, team);
            }
            else
            {
                SpawnProjectileLocally(direction, spawnPos, finalDamage, team, _hub != null && _hub.IsServer);
            }

            if (MuzzleFlash != null && !MuzzleFlash.isPlaying) MuzzleFlash.Play();
            SpawnTracer(spawnPos, spawnPos + direction * AimDistance);

            if (_anim != null) _anim.TriggerShoot();
        }

        private void SpawnProjectileLocally(Vector3 direction, Vector3 spawnPos, float damage, Team team, bool shouldNetSpawn)
        {
            ProjectileController projectile = Instantiate(ProjectilePrefab, spawnPos, Quaternion.LookRotation(direction));
            if (projectile != null)
            {
                projectile.Launch(_hub != null ? _hub.gameObject : gameObject, direction, damage, team);
                if (shouldNetSpawn && projectile.TryGetComponent<NetworkObject>(out var netObj)) netObj.Spawn();
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

        Vector3 GetAimDirection(Vector3 fromPosition)
        {
            if (AimCamera == null) return transform.forward;
            Ray ray = new Ray(AimCamera.transform.position, AimCamera.transform.forward);
            int layerMask = ~((1 << 3) | (1 << 2));

            if (Physics.Raycast(ray, out RaycastHit hit, AimDistance, layerMask, QueryTriggerInteraction.Ignore))
                return (hit.point - fromPosition).normalized;

            return ray.direction;
        }

        void SpawnTracer(Vector3 start, Vector3 end)
        {
            if (TracerPrefab == null) return;
            LineRenderer tracer = Instantiate(TracerPrefab, start, Quaternion.identity);
            tracer.positionCount = 2;
            tracer.SetPosition(0, start);
            tracer.SetPosition(1, end);
            Destroy(tracer.gameObject, TracerLifetime);
        }
    }
}
