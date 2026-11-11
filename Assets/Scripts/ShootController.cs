using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using Runtime.Scripts.Optional;
using Runtime.Scripts.Effective;
using Runtime.Scripts.Player;
using Runtime.Scripts.Health;
using Runtime.Scripts.Projectile;
using Runtime.Scripts.Balance;
using Runtime.Scripts.Ammo;
using Runtime.Scripts.Enemy;
using Runtime.Scripts.Hud;
using Runtime.Scripts.Inventory;
using Runtime.Scripts.Items;

namespace Runtime.Scripts.Shoot
{
    public class ShootController : NetworkBehaviour
    {
        private const float DEFAULT_PLAYER_SHOOT_DAMAGE = 2f;
        private const float DEFAULT_PLAYER_FIRE_RATE = 8.0f;
        private const float DEFAULT_PLAYER_AIM_DISTANCE = 120f;
        private const float DEFAULT_ENEMY_BASE_SHOOT_DAMAGE = 1f;
        private const float DEFAULT_ENEMY_BASE_FIRE_RATE = 5.0f;
        private const float DEFAULT_ENEMY_BASE_AIM_DISTANCE = 120f;
        private const int DEFAULT_MAX_AMMO = 30;
        private const float DEFAULT_RELOAD_DURATION = 1.8f;
        private const bool DEFAULT_INFINITE_AMMO = false;
        private const int DEFAULT_BURST_CAPACITY = 30;
        private const float DEFAULT_BURST_COOLDOWN = 1.2f;
        private const float DEFAULT_RECALIBRATION_DELAY = 0.5f;

        [Header("References")]
        public bool isUnlocked = false;
        public Camera AimCamera;
        public GameObject Muzzle;
        public GameObject Projectile;
        public Renderer[] visualsToRotate;

        public float Damage;
        public Optional<float> damage;
        public float FireRate;
        public Optional<float> fireRate;
        public float AimDistance;
        public Optional<float> aimDistance;
        public int MaxAmmo;
        public Optional<int> maxAmmo;
        public float ReloadDuration;
        public Optional<float> reloadDuration;
        public bool InfiniteAmmo;
        public Optional<bool> infiniteAmmo;

        [Header("Burst & Recalibration Settings")]
        public int BurstShotsCapacity;
        public Optional<int> burstShotsCapacity;
        public float BurstCooldownDuration;
        public Optional<float> burstCooldownDuration;
        public float TargetRecalibrationDelay;
        public Optional<float> targetRecalibrationDelay;

        private int m_CurrentBurstShots;
        private float m_BurstCooldownTimer;

        public LayerMask AimLayers = ~0;
        public bool HoldToFire = true;
        public bool UsePlayerInput = false;

        [Header("Ammo Runtime State")]
        public int currentAmmo = 30;
        public float currentAmmoDamageMultiplier = 1.0f;
        public bool isReloading;
        private float m_ReloadTimer;

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

        public Team EffectiveTeam => m_Health != null ? m_Health.EffectiveTeam : (transform.root.CompareTag("Player") || GetComponentInParent<PlayerController>() != null ? Team.Player : (transform.root.CompareTag("Enemy") || GetComponentInParent<EnemyController>() != null ? Team.Enemy : Team.Neutral));

        public float EffectiveDamage
        {
            get
            {
                var stats = BalanceManager.Instance != null ? BalanceManager.Instance.GetEntityBalance(gameObject) : default;
                return damage.Get(Damage, CalculateDynamicShootDamage(), stats.attackDamage);
            }
        }

        public float EffectiveFireRate => fireRate.Get(FireRate, CalculateDynamicFireRate());

        public float EffectiveAimDistance
        {
            get
            {
                var enemy = GetComponent<EnemyController>() ?? GetComponentInParent<EnemyController>();
                float defaultDist = enemy != null ? DEFAULT_ENEMY_BASE_AIM_DISTANCE : DEFAULT_PLAYER_AIM_DISTANCE;
                var stats = BalanceManager.Instance != null ? BalanceManager.Instance.GetEntityBalance(gameObject) : default;
                return aimDistance.Get(AimDistance, defaultDist, stats.shootRange);
            }
        }

        public int EffectiveMaxAmmo
        {
            get
            {
                var stats = BalanceManager.Instance != null ? BalanceManager.Instance.GetEntityBalance(gameObject) : default;
                return maxAmmo.Get(MaxAmmo, DEFAULT_MAX_AMMO, stats.maxAmmo);
            }
        }

        public float EffectiveReloadDuration => reloadDuration.Get(ReloadDuration, DEFAULT_RELOAD_DURATION);
        public bool EffectiveInfiniteAmmo => infiniteAmmo.Get(InfiniteAmmo, DEFAULT_INFINITE_AMMO);

        public int EffectiveBurstShotsCapacity
        {
            get
            {
                var stats = BalanceManager.Instance != null ? BalanceManager.Instance.GetEntityBalance(gameObject) : default;
                return burstShotsCapacity.Get(BurstShotsCapacity, DEFAULT_BURST_CAPACITY, stats.burstShotsCapacity);
            }
        }

        public float EffectiveBurstCooldownDuration
        {
            get
            {
                var stats = BalanceManager.Instance != null ? BalanceManager.Instance.GetEntityBalance(gameObject) : default;
                return burstCooldownDuration.Get(BurstCooldownDuration, DEFAULT_BURST_COOLDOWN, stats.burstCooldownDuration);
            }
        }

        public float EffectiveTargetRecalibrationDelay
        {
            get
            {
                var stats = BalanceManager.Instance != null ? BalanceManager.Instance.GetEntityBalance(gameObject) : default;
                return targetRecalibrationDelay.Get(TargetRecalibrationDelay, DEFAULT_RECALIBRATION_DELAY, stats.targetRecalibrationDelay);
            }
        }

        public void AddAmmo(int amount, float damageMultiplier = 1.0f)
        {
            currentAmmo = Mathf.Min(currentAmmo + amount, EffectiveMaxAmmo);
            currentAmmoDamageMultiplier = damageMultiplier;
            if (isReloading) { isReloading = false; m_ReloadTimer = 0f; }
        }

        private InventoryController Inv => InventoryController.LocalInstance ?? GetComponent<InventoryController>() ?? GetComponentInParent<InventoryController>();

        public bool HasAmmoInInventory()
        {
            var inv = Inv;
            if (inv == null) return false;
            foreach (var kvp in inv.GetMyBag())
            {
                var def = kvp.Value.def;
                if (def != null && def.itemPrefab != null && kvp.Value.qty > 0 && def.itemPrefab.GetComponentInChildren<AmmoController>() != null)
                    return true;
            }
            return false;
        }

        public bool AutoReloadFromInventory()
        {
            var inv = Inv;
            if (inv == null) return false;
            foreach (var kvp in inv.GetMyBag())
            {
                var def = kvp.Value.def;
                if (def == null || def.itemPrefab == null || kvp.Value.qty <= 0) continue;
                var ammo = def.itemPrefab.GetComponentInChildren<AmmoController>();
                if (ammo == null) continue;
                InventoryController.RemoveItem(def.itemName);
                AddAmmo(ammo.EffectiveAmmoAmount, ammo.EffectiveDamageMultiplier);
                return true;
            }
            return false;
        }

        public bool TryReload()
        {
            if (EffectiveInfiniteAmmo || isReloading || currentAmmo >= EffectiveMaxAmmo) return false;
            if (m_Player != null && !HasAmmoInInventory()) return false;
            isReloading = true;
            m_ReloadTimer = 0f;
            return true;
        }

        private PlayerController FindPlayer() => m_Player != null ? m_Player : (GetComponent<PlayerController>() ?? GetComponentInParent<PlayerController>());

        private float CalculateDynamicShootDamage()
        {
            if (FindPlayer() != null || CompareTag("Player"))
            {
                var hud = GetComponent<HudController>() ?? GetComponentInParent<HudController>() ?? HudController.Instance;
                int lvl = hud != null ? Mathf.Max(1, hud.Level) : 1;
                return DEFAULT_PLAYER_SHOOT_DAMAGE + (lvl - 1);
            }
            return DEFAULT_ENEMY_BASE_SHOOT_DAMAGE;
        }

        private float CalculateDynamicFireRate()
        {
            if (FindPlayer() != null || CompareTag("Player")) return DEFAULT_PLAYER_FIRE_RATE;

            var enemy = GetComponent<EnemyController>() ?? GetComponentInParent<EnemyController>();
            if (enemy == null) return DEFAULT_ENEMY_BASE_FIRE_RATE;

            switch (enemy.activeArchetype)
            {
                case AIArchetype.CargaFrenetica: return 10.0f;
                case AIArchetype.AtaqueYHuida: return 6.5f;
                case AIArchetype.FlanqueoYCobertura: return 6.0f;
                case AIArchetype.InvocadorRefuerzos: return 4.0f;
                default: return DEFAULT_ENEMY_BASE_FIRE_RATE;
            }
        }

        void Awake() => RefreshReferences();

        public void ApplyEffect(GameObject player)
        {
            m_Player = player.GetComponent<PlayerController>();
            m_Health = player.GetComponent<HealthController>();
            RefreshReferences();
        }

        private void RefreshReferences()
        {
            if (GetComponent<EnemyController>() == null && GetComponentInParent<EnemyController>() == null)
            {
                if (m_Player == null) m_Player = GetComponent<PlayerController>() ?? GetComponentInParent<PlayerController>();
            }
            else m_Player = null;

            if (m_Health == null)
                m_Health = GetComponent<HealthController>() ?? GetComponentInParent<HealthController>() ?? GetComponentInChildren<HealthController>();

            if (AimCamera == null && m_Player != null) AimCamera = m_Player.GetComponentInChildren<Camera>();
            if (AimCamera == null) AimCamera = Camera.main;

            if (visualsToRotate == null || visualsToRotate.Length == 0)
            {
                var mr = GetComponentInChildren<MeshRenderer>();
                if (mr != null) visualsToRotate = new Renderer[] { mr };
            }

            if (Muzzle == null)
            {
                Transform m = transform.Find("Muzzle") ?? transform.Find("muzzle");
                Muzzle = m != null ? m.gameObject : gameObject;
            }

            if (!isUnlocked && Projectile != null) isUnlocked = true;
        }

        private void UpdateInspectorValues()
        {
            Damage = EffectiveDamage;
            FireRate = EffectiveFireRate;
            AimDistance = EffectiveAimDistance;
            MaxAmmo = EffectiveMaxAmmo;
            ReloadDuration = EffectiveReloadDuration;
            InfiniteAmmo = EffectiveInfiniteAmmo;
        }

        private void OnValidate() => UpdateInspectorValues();

        void Update()
        {
            UpdateInspectorValues();

            if (transform != transform.root)
            {
                var root = transform.root.GetComponent<ShootController>();
                if (root != null && root != this && root.isUnlocked) { enabled = false; return; }
            }

            if (EffectiveInfiniteAmmo)
            {
                currentAmmo = EffectiveMaxAmmo;
                isReloading = false;
            }

            if (m_BurstCooldownTimer > 0f) m_BurstCooldownTimer = Mathf.Max(0f, m_BurstCooldownTimer - Time.deltaTime);

            if (!isReloading) return;
            m_ReloadTimer += Time.deltaTime;
            if (m_ReloadTimer < EffectiveReloadDuration) return;

            bool consumed = AutoReloadFromInventory();
            if (m_Player != null) { if (!consumed) currentAmmo = 0; }
            else currentAmmo = EffectiveMaxAmmo;

            isReloading = false;
            m_ReloadTimer = 0f;
        }

        private bool CanFire()
        {
            if (!isUnlocked) return false;
            if (Projectile == null || Muzzle == null)
            {
                RefreshReferences();
                if (Projectile == null || Muzzle == null) return false;
            }
            if (isReloading || m_BurstCooldownTimer > 0f) return false;
            if (!EffectiveInfiniteAmmo && currentAmmo <= 0) { TryReload(); return false; }
            if (Time.time < m_NextFireTime) return false;

            m_NextFireTime = Time.time + 1f / Mathf.Max(0.01f, EffectiveFireRate);
            if (!EffectiveInfiniteAmmo)
            {
                currentAmmo = Mathf.Max(0, currentAmmo - 1);
                if (currentAmmo <= 0) TryReload();
            }
            return true;
        }

        public bool TryFire()
        {
            if (!isUnlocked) return false;

            if (m_Player == null) m_Player = GetComponent<PlayerController>() ?? GetComponentInParent<PlayerController>();

            if (!CanFire()) return false;

            Vector3 origin = Muzzle.transform.position;
            ExecuteFire(GetAimDirection(origin), origin);
            return true;
        }

        public bool FireAt(Vector3 target)
        {
            if (!CanFire()) return false;

            RotateVisualsTowards(target);

            Vector3 origin = Muzzle.transform.position;
            Vector3 dir = target - origin;
            dir = dir.sqrMagnitude < 0.01f ? transform.forward : dir.normalized;

            ExecuteFire(dir, origin);
            return true;
        }

        private void RotateVisualsTowards(Vector3 target)
        {
            if (visualsToRotate == null) return;
            Vector3 dir = (target - transform.position).normalized;
            if (dir.sqrMagnitude <= 0.001f) return;

            Quaternion rot = Quaternion.LookRotation(dir);
            foreach (var r in visualsToRotate)
                if (r != null) r.transform.rotation = Quaternion.Slerp(r.transform.rotation, rot, rotationSpeed * Time.deltaTime);
        }

        private void ExecuteFire(Vector3 direction, Vector3 spawnPos)
        {
            if (++m_CurrentBurstShots >= EffectiveBurstShotsCapacity)
            {
                m_CurrentBurstShots = 0;
                m_BurstCooldownTimer = EffectiveBurstCooldownDuration + EffectiveTargetRecalibrationDelay;
            }

            float finalDamage = EffectiveDamage * currentAmmoDamageMultiplier;
            Team team = EffectiveTeam;

            if (m_Player != null && IsNetworkActive) m_Player.RequestFire(Projectile, direction, spawnPos, finalDamage, team);
            else SpawnProjectileLocally(direction, spawnPos, finalDamage, team, IsNetworkActive && NetworkManager.Singleton.IsServer);

            Animator anim = GetComponentInChildren<Animator>();
            if (anim != null && HasParameter(anim, "shoot")) anim.SetTrigger("shoot");

            if (MuzzleFlash) MuzzleFlash.Play();
        }

        private Vector3 GetAimDirection(Vector3 muzzlePos)
        {
            if (AimCamera == null) return transform.forward;

            Ray ray = AimCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
            RaycastHit[] hits = Physics.RaycastAll(ray, EffectiveAimDistance, AimLayers);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            Vector3 target = ray.GetPoint(EffectiveAimDistance);
            Transform root = OwnerEntity.transform;

            foreach (var hit in hits)
            {
                if (hit.transform.root == root || hit.collider.isTrigger) continue;
                target = hit.point;
                break;
            }

            Vector3 dir = target - muzzlePos;
            return dir.sqrMagnitude < 0.01f ? AimCamera.transform.forward : dir.normalized;
        }

        private void SpawnProjectileLocally(Vector3 direction, Vector3 spawnPos, float dmg, Team team, bool isServer)
        {
            Quaternion rot = Quaternion.LookRotation(direction);
            GameObject go = BalanceManager.Instance != null
                ? BalanceManager.Instance.SpawnFromPool(Projectile, spawnPos, rot)
                : Instantiate(Projectile, spawnPos, rot);

            var proj = go.GetComponent<ProjectileController>();
            if (proj != null) proj.Launch(OwnerEntity, direction, dmg, team);

            if (isServer)
            {
                var net = go.GetComponent<NetworkObject>();
                if (net != null) net.Spawn();
            }
        }

        private static bool HasParameter(Animator animator, string name)
        {
            foreach (var p in animator.parameters)
                if (p.name == name) return true;
            return false;
        }
    }
}