using Unity.FPS.Gameplay;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

namespace Combating.Scripts
{
    public class ShootController : NetworkBehaviour
    {
        [Header("References")]
        public Camera AimCamera;
        public Transform Muzzle;
        public ProjectileController ProjectilePrefab;

        [Header("Shooting")]
        public float Damage = 25f;
        public float FireRate = 6f;
        public float AimDistance = 100f;
        public LayerMask AimLayers = ~0;
        public bool HoldToFire = true;
        public bool UsePlayerInput = true;

        [Header("Visuals")]
        public ParticleSystem MuzzleFlash;
        public LineRenderer TracerPrefab;
        public float TracerLifetime = 0.05f;

        private PlayerInputHandler m_Input;
        private HealthController m_Health;
        private float m_NextFireTime;

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;

        void Awake()
        {
            m_Input = GetComponent<PlayerInputHandler>();
            m_Health = GetComponent<HealthController>();
            if (AimCamera == null) AimCamera = GetComponentInChildren<Camera>() ?? Camera.main;
            if (Muzzle == null) Muzzle = transform;
        }

        void Update()
        {
            if (!IsOwner || !UsePlayerInput) return;
            if (WantsToFire()) TryFire();
        }

        bool WantsToFire()
        {
            if (m_Input != null && m_Input.CanProcessInput())
                return HoldToFire ? m_Input.GetFireInputHeld() : m_Input.GetFireInputDown();

            if (Mouse.current == null) return false;
            return HoldToFire ? Mouse.current.leftButton.isPressed : Mouse.current.leftButton.wasPressedThisFrame;
        }

        public bool TryFire()
        {
            if (Time.time < m_NextFireTime || ProjectilePrefab == null) return false;
            m_NextFireTime = Time.time + 1f / Mathf.Max(0.01f, FireRate);

            float finalDamage = Damage;
            // Escalado de dano por Stats
            if (TryGetComponent<StatsController>(out var stats))
            {
                finalDamage = Damage * (stats.Attack / 10f);
            }

            Vector3 direction = GetAimDirection();

            // Mayor distancia para que nazca fuera del robot
            Vector3 spawnPos = Muzzle.position + direction * 0.8f;

            if (IsNetworkActive) RequestFireServerRpc(direction, spawnPos, spawnPos + direction * AimDistance, finalDamage);
            else SpawnProjectileLocally(direction, spawnPos, spawnPos + direction * AimDistance, finalDamage);

            return true;
        }

        public bool FireAt(Vector3 targetPosition)
        {
            if (Time.time < m_NextFireTime || ProjectilePrefab == null) return false;
            m_NextFireTime = Time.time + 1f / Mathf.Max(0.01f, FireRate);

            float finalDamage = Damage;
            if (TryGetComponent<StatsController>(out var stats))
            {
                finalDamage = Damage * (stats.Attack / 10f);
            }

            Vector3 direction = (targetPosition - Muzzle.position).normalized;
            Vector3 spawnPos = Muzzle.position + direction * 0.5f;

            if (IsNetworkActive) RequestFireServerRpc(direction, spawnPos, targetPosition, finalDamage);
            else SpawnProjectileLocally(direction, spawnPos, targetPosition, finalDamage);

            return true;
        }

        [ServerRpc]
        private void RequestFireServerRpc(Vector3 direction, Vector3 spawnPos, Vector3 impactPos, float damage)
        {
            SpawnProjectileLocally(direction, spawnPos, impactPos, damage, true);
        }

        private void SpawnProjectileLocally(Vector3 direction, Vector3 spawnPos, Vector3 impactPos, float damage, bool isNetworked = false)
        {
            ProjectileController projectile = Instantiate(ProjectilePrefab, spawnPos, Quaternion.LookRotation(direction));
            if (projectile != null)
            {
                projectile.Launch(gameObject, direction, damage, m_Health != null ? m_Health.team : Team.Neutral);
                if (isNetworked && projectile.TryGetComponent<NetworkObject>(out var netObj)) netObj.Spawn();
            }

            if (MuzzleFlash != null && !MuzzleFlash.isPlaying) MuzzleFlash.Play();
            SpawnTracer(spawnPos, impactPos);
        }

        Vector3 GetAimDirection()
        {
            Ray ray = new Ray(AimCamera.transform.position, AimCamera.transform.forward);
            if (Physics.Raycast(ray, out RaycastHit hit, AimDistance, AimLayers, QueryTriggerInteraction.Ignore))
                return (hit.point - Muzzle.position).normalized;
            return (ray.GetPoint(AimDistance) - Muzzle.position).normalized;
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
