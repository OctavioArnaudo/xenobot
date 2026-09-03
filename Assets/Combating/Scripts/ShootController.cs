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
    /// </summary>
    public class ShootController : NetworkBehaviour, IItemFunctional
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
        private bool CanExecuteLocalLogic => !IsNetworkActive || IsOwner;

        void Awake()
        {
            RefreshReferences();
        }

        public void ApplyEffect(GameObject player)
        {
            m_Player = player.GetComponent<PlayerController>();
            m_Health = player.GetComponent<HealthController>();

            // Forzar refresco para encontrar el MuzzlePoint del WeaponController
            RefreshReferences();

            Debug.Log("[ShootController] Lógica de disparo vinculada al jugador.");
        }

        private void RefreshReferences()
        {
            if (m_Player == null) m_Player = GetComponentInParent<PlayerController>();
            if (m_Health == null) m_Health = GetComponentInParent<HealthController>();
            if (AimCamera == null) AimCamera = GetComponentInChildren<Camera>() ?? Camera.main;

            // Si Muzzle es null, intentar buscarlo en un WeaponController en el mismo objeto
            if (Muzzle == null)
            {
                var wc = GetComponent<WeaponController>();
                if (wc != null) Muzzle = wc.muzzlePoint;

                // Fallback: Buscar en hijos si no está asignado
                if (Muzzle == null)
                {
                    Transform t = transform.Find("WeaponRender/MuzzlePoint");
                    if (t != null) Muzzle = t;
                }
            }

            // Si visualsToRotate está vacío, intentar rotar el render del arma
            if (visualsToRotate == null || visualsToRotate.Length == 0)
            {
                var mr = GetComponentInChildren<MeshRenderer>();
                if (mr != null) visualsToRotate = new Renderer[] { mr };
            }
        }

        void Update()
        {
            if (!CanExecuteLocalLogic || !UsePlayerInput) return;
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

            Vector3 originPos = GetShootOrigin();
            if (Time.time < m_NextFireTime) return false;
            m_NextFireTime = Time.time + 1f / Mathf.Max(0.01f, FireRate);

            float finalDamage = Damage;
            StatsController stats = m_Player != null ? m_Player.GetComponent<StatsController>() : null;
            if (stats != null) finalDamage = Damage * (stats.Attack / 10f);

            Vector3 direction = GetAimDirection(originPos);
            Vector3 spawnPos = originPos + direction * 0.5f;

            if (IsNetworkActive) RequestFireServerRpc(direction, spawnPos, spawnPos + direction * AimDistance, finalDamage);
            else SpawnProjectileLocally(direction, spawnPos, spawnPos + direction * AimDistance, finalDamage);
            return true;
        }

        private Vector3 GetShootOrigin()
        {
            return (Muzzle != null) ? Muzzle.position : transform.position;
        }

        public bool FireAt(Vector3 targetPosition)
        {
            RotateVisualsTowards(targetPosition);
            if (Time.time < m_NextFireTime || ProjectilePrefab == null || Muzzle == null) return false;
            m_NextFireTime = Time.time + 1f / Mathf.Max(0.01f, FireRate);

            float finalDamage = Damage;
            StatsController stats = m_Player != null ? m_Player.GetComponent<StatsController>() : null;
            if (stats != null) finalDamage = Damage * (stats.Attack / 10f);

            Vector3 direction = (targetPosition - Muzzle.position).normalized;
            Vector3 spawnPos = Muzzle.position + direction * 0.5f;

            if (IsNetworkActive) RequestFireServerRpc(direction, spawnPos, targetPosition, finalDamage);
            else SpawnProjectileLocally(direction, spawnPos, targetPosition, finalDamage);
            return true;
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
                projectile.Launch(m_Player != null ? m_Player.gameObject : gameObject, direction, damage, m_Health != null ? m_Health.team : Team.Neutral);
                if (isNetworked && projectile.TryGetComponent<NetworkObject>(out var netObj)) netObj.Spawn();
            }
            if (MuzzleFlash != null && !MuzzleFlash.isPlaying) MuzzleFlash.Play();
            SpawnTracer(spawnPos, impactPos);
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
