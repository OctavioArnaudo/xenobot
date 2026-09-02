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
        public Renderer[] visualsToRotate; // Renders or parts that should tilt (Pitch)

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
        public float rotationSpeed = 10f;

        private PlayerController m_Player;
        private HealthController m_Health;
        private float m_NextFireTime;

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        private bool CanExecuteLocalLogic => !IsNetworkActive || IsOwner;

        void Awake()
        {
            m_Player = GetComponent<PlayerController>();
            m_Health = GetComponent<HealthController>();
            if (AimCamera == null) AimCamera = GetComponentInChildren<Camera>() ?? Camera.main;
            if (Muzzle == null) Muzzle = transform;

            // Auto-detect visuals if not assigned (compatibility with previous setup)
            if (visualsToRotate == null || visualsToRotate.Length == 0)
                visualsToRotate = GetComponentsInChildren<Renderer>();
        }

        void Update()
        {
            if (!CanExecuteLocalLogic || !UsePlayerInput) return;
            if (WantsToFire()) TryFire();
        }

        bool WantsToFire()
        {
            bool playerInput = false;
            if (m_Player != null)
            {
                playerInput = HoldToFire ? m_Player.fireHeld : m_Player.fire;
            }

            // Plan B: Lectura directa de hardware si el script del player falla
            bool mouseInput = false;
            if (Mouse.current != null)
            {
                mouseInput = HoldToFire ? Mouse.current.leftButton.isPressed : Mouse.current.leftButton.wasPressedThisFrame;
            }

            return playerInput || mouseInput;
        }

        public bool TryFire()
        {
            if (ProjectilePrefab == null) { Debug.LogWarning("[Shoot] No hay proyectil asignado!"); return false; }

            // Determinar punto de origen dinámico (Cabeza/Pecho)
            Vector3 originPos = GetShootOrigin();

            if (Time.time < m_NextFireTime) return false;
            m_NextFireTime = Time.time + 1f / Mathf.Max(0.01f, FireRate);

            float finalDamage = Damage;
            if (TryGetComponent<StatsController>(out var stats))
            {
                finalDamage = Damage * (stats.Attack / 10f);
            }

            Vector3 direction = GetAimDirection(originPos);
            Vector3 spawnPos = originPos + direction * 0.5f;

            if (IsNetworkActive) RequestFireServerRpc(direction, spawnPos, spawnPos + direction * AimDistance, finalDamage);
            else SpawnProjectileLocally(direction, spawnPos, spawnPos + direction * AimDistance, finalDamage);

            return true;
        }

        private Vector3 GetShootOrigin()
        {
            Animator anim = GetComponentInChildren<Animator>();
            if (anim != null)
            {
                Transform bone = anim.GetBoneTransform(HumanBodyBones.Head) ??
                                 anim.GetBoneTransform(HumanBodyBones.Chest) ??
                                 anim.transform.Find("head") ??
                                 anim.transform.Find("Head") ??
                                 anim.transform.Find("spine");
                if (bone != null) return bone.position;
            }
            return (Muzzle != null) ? Muzzle.position : transform.position + Vector3.up * 1.5f;
        }

        /// <summary>
        /// Logic for AI or remote triggers.
        /// Handles visual rotation towards target internally.
        /// </summary>
        public bool FireAt(Vector3 targetPosition)
        {
            // Internal logic: How to shoot (Rotation + Firing)
            RotateVisualsTowards(targetPosition);

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
                projectile.Launch(gameObject, direction, damage, m_Health != null ? m_Health.team : Team.Neutral);
                if (isNetworked && projectile.TryGetComponent<NetworkObject>(out var netObj)) netObj.Spawn();
            }

            if (MuzzleFlash != null && !MuzzleFlash.isPlaying) MuzzleFlash.Play();
            SpawnTracer(spawnPos, impactPos);
        }

        Vector3 GetAimDirection(Vector3 fromPosition)
        {
            // Raycast desde el centro de la cámara
            Ray ray = new Ray(AimCamera.transform.position, AimCamera.transform.forward);

            // Ignorar Capas 3 (Player), 2 (Ignore Raycast)
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
