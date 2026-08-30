using Unity.FPS.Gameplay;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

namespace Combating.Scripts
{
    /// <summary>
    /// Component that handles ranged attacks by spawning projectiles.
    /// Works for both players (input) and enemies (automated).
    /// </summary>
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
            if (!IsOwner || !UsePlayerInput || !WantsToFire()) return;
            TryFire();
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
            Vector3 direction = GetAimDirection();

            SpawnProjectile(direction, Muzzle.position + direction * AimDistance);

            if (MuzzleFlash != null) MuzzleFlash.Play();
            return true;
        }

        public bool FireAt(Vector3 targetPosition)
        {
            if (Time.time < m_NextFireTime || ProjectilePrefab == null) return false;

            m_NextFireTime = Time.time + 1f / Mathf.Max(0.01f, FireRate);
            Vector3 direction = (targetPosition - Muzzle.position).normalized;

            SpawnProjectile(direction, targetPosition);

            if (MuzzleFlash != null) MuzzleFlash.Play();
            return true;
        }

        private void SpawnProjectile(Vector3 direction, Vector3 targetImpact)
        {
            ProjectileController projectile = Instantiate(ProjectilePrefab, Muzzle.position, Quaternion.LookRotation(direction));

            if (projectile != null)
            {
                projectile.Launch(gameObject, direction, Damage, m_Health != null ? m_Health.team : Team.Neutral);

                if (IsNetworkActive && IsServer)
                {
                    if (projectile.TryGetComponent<NetworkObject>(out var netObj))
                        netObj.Spawn();
                }
            }

            SpawnTracer(Muzzle.position, targetImpact);
        }

        Vector3 GetAimDirection()
        {
            if (AimCamera == null) return Muzzle.forward;

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
