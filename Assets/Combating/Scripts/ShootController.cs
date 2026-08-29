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
        public CombatProjectile ProjectilePrefab;

        [Header("Shooting")]
        public float Damage = 25f;
        public float ProjectileSpeed = 40f;
        public float FireRate = 6f;
        public float AimDistance = 100f;
        public LayerMask AimLayers = ~0;
        public bool HoldToFire = true;
        public bool UsePlayerInput = true;

        [Header("Visuals")]
        public ParticleSystem MuzzleFlash;
        public LineRenderer TracerPrefab;
        public float TracerLifetime = 0.05f;

        PlayerInputHandler m_Input;
        float m_NextFireTime;

        void Awake()
        {
            m_Input = GetComponent<PlayerInputHandler>();

            if (AimCamera == null)
                AimCamera = GetComponentInChildren<Camera>();

            if (AimCamera == null)
                AimCamera = Camera.main;

            if (Muzzle == null)
                Muzzle = transform;
        }

        void Update()
        {
            if (!IsOwner) return; // Solo el dueño dispara localmente

            if (!UsePlayerInput)
                return;

            if (!WantsToFire())
                return;

            TryFire();
        }

        bool WantsToFire()
        {
            if (m_Input != null && m_Input.CanProcessInput())
                return HoldToFire ? m_Input.GetFireInputHeld() : m_Input.GetFireInputDown();

            if (Mouse.current == null)
                return false;

            return HoldToFire ? Mouse.current.leftButton.isPressed : Mouse.current.leftButton.wasPressedThisFrame;
        }

        public bool TryFire()
        {
            if (Time.time < m_NextFireTime || ProjectilePrefab == null)
                return false;

            m_NextFireTime = Time.time + 1f / Mathf.Max(0.01f, FireRate);
            Vector3 direction = GetAimDirection();

            // Spawn del proyectil (Idealmente esto debería ser un RPC, pero por ahora bloqueamos el input remoto)
            CombatProjectile projectile = Instantiate(ProjectilePrefab, Muzzle.position, Quaternion.LookRotation(direction));
            projectile.Launch(gameObject, direction, Damage, ProjectileSpeed);

            if (MuzzleFlash != null)
                MuzzleFlash.Play();

            SpawnTracer(Muzzle.position, Muzzle.position + direction * AimDistance);
            return true;
        }

        public bool FireAt(Vector3 targetPosition)
        {
            if (Time.time < m_NextFireTime || ProjectilePrefab == null)
                return false;

            m_NextFireTime = Time.time + 1f / Mathf.Max(0.01f, FireRate);
            Vector3 direction = (targetPosition - Muzzle.position).normalized;
            CombatProjectile projectile = Instantiate(ProjectilePrefab, Muzzle.position, Quaternion.LookRotation(direction));
            projectile.Launch(gameObject, direction, Damage, ProjectileSpeed);

            if (MuzzleFlash != null)
                MuzzleFlash.Play();

            SpawnTracer(Muzzle.position, targetPosition);
            return true;
        }

        Vector3 GetAimDirection()
        {
            if (AimCamera == null)
                return Muzzle.forward;

            Ray ray = new Ray(AimCamera.transform.position, AimCamera.transform.forward);
            if (Physics.Raycast(ray, out RaycastHit hit, AimDistance, AimLayers, QueryTriggerInteraction.Ignore))
                return (hit.point - Muzzle.position).normalized;

            return (ray.GetPoint(AimDistance) - Muzzle.position).normalized;
        }

        void SpawnTracer(Vector3 start, Vector3 end)
        {
            if (TracerPrefab == null)
                return;

            LineRenderer tracer = Instantiate(TracerPrefab, start, Quaternion.identity);
            tracer.positionCount = 2;
            tracer.SetPosition(0, start);
            tracer.SetPosition(1, end);
            Destroy(tracer.gameObject, TracerLifetime);
        }
    }
}
