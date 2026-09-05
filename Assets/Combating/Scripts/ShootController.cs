using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using Crafting.Scripts;

namespace Combating.Scripts
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

        [Header("Animation")]
        public Animator animator;

        private ModularController _hub;
        private HealthController m_Health;
        private float m_NextFireTime;

        private static readonly int _animIDShoot = Animator.StringToHash("shoot");
        private bool _hasAnimator;
        private bool _hasAnimIDShoot;

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

        void Awake()
        {
            _hub = GetComponentInParent<ModularController>();
            if (_hub != null) Bind(_hub);
            else
            {
                if (m_Health == null) m_Health = GetComponentInParent<HealthController>();
                if (Muzzle == null)
                {
                    Transform foundMuzzle = transform.Find("Muzzle") ?? transform.Find("FirePoint");
                    Muzzle = foundMuzzle != null ? foundMuzzle : transform;
                }
            }
            RefreshAnimatorReference();
        }

        public void ApplyEffect(GameObject player)
        {
            _hub = player.GetComponent<ModularController>();
            if (_hub != null) Bind(_hub);
            RefreshAnimatorReference();
        }

        public void Bind(ModularController hub)
        {
            _hub = hub;
            if (_hub != null)
            {
                _hub.RegisterModule(this);
                OnRefreshModule();
            }
        }

        public void OnRefreshModule()
        {
            if (_hub != null)
            {
                m_Health = _hub.GetModule<HealthController>();
                AimCamera = _hub.mainCamera?.GetComponent<Camera>() ?? _hub.GetComponentInChildren<Camera>();

                if (_hub.MuzzlePoint != null)
                {
                    Muzzle = _hub.MuzzlePoint;
                }
            }
            RefreshAnimatorReference();
        }

        private void RefreshAnimatorReference()
        {
            if (animator == null)
            {
                animator = (_hub != null) ? _hub.animator : GetComponentInChildren<Animator>();
            }

            _hasAnimator = animator != null;
            if (_hasAnimator) _hasAnimIDShoot = HasParameter(animator, _animIDShoot);
        }

        void Update()
        {
            if (!UsePlayerInput || _hub == null || !(_hub is PlayerController)) return;

            PlayerController player = (PlayerController)_hub;

            bool canHandleInput = IsNetworkActive ? player.IsOwner : true;
            if (!canHandleInput) return;

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
            HudController stats = (_hub != null) ? _hub.GetModule<HudController>() : GetComponentInParent<HudController>();
            if (stats != null) finalDamage = Damage * (stats.Attack / 10f);

            Team team = m_Health != null ? m_Health.team : Team.Neutral;

            if (_hub != null && _hub is PlayerController player && IsNetworkActive)
            {
                player.RequestFire(ProjectilePrefab, direction, spawnPos, finalDamage, team);
            }
            else
            {
                SpawnProjectileLocally(direction, spawnPos, finalDamage, team, IsNetworkActive && NetworkManager.Singleton.IsServer);
            }

            if (MuzzleFlash != null && !MuzzleFlash.isPlaying) MuzzleFlash.Play();
            SpawnTracer(spawnPos, spawnPos + direction * AimDistance);
            TriggerShootAnimation();
        }

        private void TriggerShootAnimation()
        {
            if (!_hasAnimator || animator == null)
            {
                RefreshAnimatorReference();
                if (!_hasAnimator || animator == null) return;
            }
            if (_hasAnimIDShoot) animator.SetTrigger(_animIDShoot);
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

        private bool HasParameter(Animator anim, int paramHash)
        {
            if (anim == null) return false;
            foreach (AnimatorControllerParameter param in anim.parameters)
                if (param.nameHash == paramHash) return true;
            return false;
        }
    }
}
