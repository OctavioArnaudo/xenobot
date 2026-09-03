using Unity.FPS.Gameplay;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using Crafting.Scripts;

namespace Combating.Scripts
{
    /// <summary>
    /// Logic controller for shooting mechanics.
    /// Handles firing projectiles, cooldowns, and input.
    /// Works for both Players (via bridge) and AI (direct server spawning).
    /// </summary>
    public class ShootController : MonoBehaviour, IItemFunctional
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

        private PlayerController m_Player;
        private HealthController m_Health;
        private float m_NextFireTime;

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

        void Awake()
        {
            RefreshReferences();
        }

        public void ApplyEffect(GameObject player)
        {
            m_Player = player.GetComponent<PlayerController>();
            m_Health = player.GetComponent<HealthController>();
            RefreshReferences();
            Debug.Log("[ShootController] Lógica de disparo vinculada.");
        }

        private void RefreshReferences()
        {
            if (m_Player == null) m_Player = GetComponentInParent<PlayerController>();
            if (m_Health == null) m_Health = GetComponentInParent<HealthController>();

            if (AimCamera == null && m_Player != null)
                AimCamera = m_Player.GetComponentInChildren<Camera>();

            if (AimCamera == null) AimCamera = Camera.main;

            if (Muzzle == null)
            {
                var wc = GetComponent<WeaponController>();
                if (wc != null) Muzzle = wc.muzzlePoint;

                if (Muzzle == null)
                {
                    Transform t = transform.Find("WeaponRender/MuzzlePoint");
                    if (t != null) Muzzle = t;
                }
            }

            if (visualsToRotate == null || visualsToRotate.Length == 0)
            {
                var mr = GetComponentInChildren<MeshRenderer>();
                if (mr != null) visualsToRotate = new Renderer[] { mr };
            }
        }

        void Update()
        {
            if (!UsePlayerInput) return;

            // Safely check ownership and network status
            bool isOwner = (m_Player != null) ? (IsNetworkActive ? m_Player.IsOwner : true) : true;
            if (!isOwner) return;

            if (m_Player == null) RefreshReferences();
            if (m_Player != null && WantsToFire()) TryFire();
        }

        bool WantsToFire()
        {
            bool playerInput = (m_Player != null) ? (HoldToFire ? m_Player.fireHeld : m_Player.fire) : false;
            bool mouseInput = (Mouse.current != null) ? (HoldToFire ? Mouse.current.leftButton.isPressed : Mouse.current.leftButton.wasPressedThisFrame) : false;
            return playerInput || mouseInput;
        }

        public bool TryFire()
        {
            if (ProjectilePrefab == null || Muzzle == null) return false;
            if (Time.time < m_NextFireTime) return false;
            m_NextFireTime = Time.time + 1f / Mathf.Max(0.01f, FireRate);

            Vector3 originPos = Muzzle.position;
            Vector3 direction = GetAimDirection(originPos);
            ExecuteFire(direction, originPos);
            return true;
        }

        public bool FireAt(Vector3 targetPosition)
        {
            if (ProjectilePrefab == null || Muzzle == null) return false;
            if (Time.time < m_NextFireTime) return false;
            m_NextFireTime = Time.time + 1f / Mathf.Max(0.01f, FireRate);

            RotateVisualsTowards(targetPosition);

            Vector3 originPos = Muzzle.position;
            Vector3 direction = (targetPosition - originPos).normalized;
            ExecuteFire(direction, originPos);
            return true;
        }

        private void ExecuteFire(Vector3 direction, Vector3 spawnPos)
        {
            float finalDamage = Damage;
            StatsController stats = (m_Player != null) ? m_Player.GetComponent<StatsController>() : GetComponentInParent<StatsController>();
            if (stats != null) finalDamage = Damage * (stats.Attack / 10f);

            Team team = m_Health != null ? m_Health.team : Team.Neutral;

            if (m_Player != null)
            {
                m_Player.RequestFire(ProjectilePrefab, direction, spawnPos, finalDamage, team);
            }
            else
            {
                // Direct spawn for AI/Enemies
                SpawnProjectileLocally(direction, spawnPos, finalDamage, team, IsNetworkActive && NetworkManager.Singleton.IsServer);
            }

            if (MuzzleFlash != null && !MuzzleFlash.isPlaying) MuzzleFlash.Play();
            SpawnTracer(spawnPos, spawnPos + direction * AimDistance);
        }

        private void SpawnProjectileLocally(Vector3 direction, Vector3 spawnPos, float damage, Team team, bool shouldNetSpawn)
        {
            ProjectileController projectile = Instantiate(ProjectilePrefab, spawnPos, Quaternion.LookRotation(direction));
            if (projectile != null)
            {
                projectile.Launch(gameObject, direction, damage, team);
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
