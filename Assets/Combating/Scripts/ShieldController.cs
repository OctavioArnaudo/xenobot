using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using Crafting.Scripts;
using System.Collections.Generic;
using System.Linq;

namespace Combating.Scripts
{
    public enum ShieldInputMode
    {
        HoldToActivate, // Mantener botón central de la rueda para activar, soltar para desactivar
        TogglePress     // Pulsar/girar la rueda del mouse para alternar (activar/desactivar)
    }

    /// <summary>
    /// Sistema de escudo de energía esférico con mitigación de daño, colisionadores de impacto,
    /// capacidad de resistencia, cooldowns por tiempo/daño, escalado por nivel (Jugador)
    /// y degradación inversa por ciclos (Enemigo).
    /// </summary>
    [ExecuteAlways]
    public class ShieldController : NetworkBehaviour, IItemUseAction, IItemQuitAction, IItemDropAction, IItemPickupAction
    {
        private const float DEFAULT_DAMAGE_REDUCTION = 0.50f;
        private const float DEFAULT_MAX_SHIELD_HEALTH = 150.0f;
        private const float DEFAULT_MAX_SHIELD_DURATION = 10.0f;
        private const float DEFAULT_COOLDOWN_DURATION = 5.0f;

        [Header("Shield Settings")]
        public bool isUnlocked = true; // Permiso para usar el escudo

        [Tooltip("Mitigación de daño: 1.0 = Bloqueo total, 0.5 = Mitiga el 50%")]
        public Optional<float> damageReductionOverride;

        [Tooltip("Capacidad máxima de daño que absorbe el escudo activo antes de romperse")]
        public Optional<float> maxShieldHealthOverride;

        [Tooltip("Duración máxima sostenida en segundos estando activo")]
        public Optional<float> maxShieldDurationOverride;

        [Tooltip("Tiempo de enfriamiento/recarga en segundos tras agotarse o desactivarse")]
        public Optional<float> cooldownDurationOverride;

        [Header("Activation & Input Settings")]
        [Tooltip("Modo de entrada de la rueda del mouse: HoldToActivate (mantener botón) o TogglePress (pulsar/girar para alternar)")]
        public ShieldInputMode inputMode = ShieldInputMode.HoldToActivate;
        public bool autoUnlockOnStart = false;
        public bool autoActivateOnStart = false;
        public bool toggleOnUse = true;

        [Header("Visuals & Audio")]
        public bool generateDefaultVisuals = true;
        public bool previewInEditor = true;
        public GameObject shieldVisualObject;

        [Header("Shield Sphere Configuration")]
        public Vector3 generatorOffset = new Vector3(0f, 0.9f, 0f);
        public Vector3 generatorScale = new Vector3(2.2f, 2.2f, 2.2f);

        public AudioClip shieldActivateSound;
        public AudioClip shieldBlockSound;

        [Header("Animation")]
        public string shieldAnimBool = "isShieldActive";

        private Animator m_Animator;
        private HealthController m_Health;

        private bool m_ActivatedByInput = false;

        private float m_CurrentShieldHealth;
        private float m_ActiveTimer;
        private float m_CooldownTimer;
        private int m_EnemyCycleCount = 0;
        private float m_HitFlashTimer;
        private Dictionary<Material, Color> m_ShieldOriginalColors = new Dictionary<Material, Color>();

        private readonly NetworkVariable<bool> m_IsShieldActive = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner
        );

        private bool m_OfflineShieldActive = false;

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;

        private bool IsEnemy
        {
            get
            {
                if (m_Health != null) return m_Health.EffectiveTeam == Team.Enemy;
                return CompareTag("Enemy") || GetComponent<EnemyController>() != null || GetComponentInParent<EnemyController>() != null;
            }
        }

        public float EffectiveDamageReduction
        {
            get
            {
                float baseVal = damageReductionOverride.GetValue(DEFAULT_DAMAGE_REDUCTION);
                if (IsEnemy)
                {
                    float peak = Mathf.Min(0.90f, baseVal * 1.5f);
                    float cycleFactor = Mathf.Pow(0.80f, m_EnemyCycleCount);
                    return Mathf.Max(0.20f, peak * cycleFactor);
                }
                else
                {
                    int lvl = HudController.Instance != null ? HudController.Instance.Level : 1;
                    int bonus = Mathf.Max(0, lvl - 1);
                    return Mathf.Min(0.95f, baseVal + bonus * 0.05f);
                }
            }
        }

        public float EffectiveMaxShieldHealth
        {
            get
            {
                float baseVal = maxShieldHealthOverride.GetValue(DEFAULT_MAX_SHIELD_HEALTH);
                if (IsEnemy)
                {
                    float peak = baseVal * 1.6f;
                    float cycleFactor = Mathf.Pow(0.80f, m_EnemyCycleCount);
                    return Mathf.Max(40f, peak * cycleFactor);
                }
                else
                {
                    int lvl = HudController.Instance != null ? HudController.Instance.Level : 1;
                    int bonus = Mathf.Max(0, lvl - 1);
                    return baseVal + bonus * 30f;
                }
            }
        }

        public float EffectiveMaxShieldDuration
        {
            get
            {
                float baseVal = maxShieldDurationOverride.GetValue(DEFAULT_MAX_SHIELD_DURATION);
                if (IsEnemy)
                {
                    float peak = baseVal * 1.5f;
                    float cycleFactor = Mathf.Pow(0.80f, m_EnemyCycleCount);
                    return Mathf.Max(3f, peak * cycleFactor);
                }
                else
                {
                    int lvl = HudController.Instance != null ? HudController.Instance.Level : 1;
                    int bonus = Mathf.Max(0, lvl - 1);
                    return baseVal + bonus * 2f;
                }
            }
        }

        public float EffectiveCooldownDuration
        {
            get
            {
                float baseVal = cooldownDurationOverride.GetValue(DEFAULT_COOLDOWN_DURATION);
                if (IsEnemy)
                {
                    float peak = baseVal * 0.8f;
                    float cycleFactor = Mathf.Pow(0.80f, m_EnemyCycleCount);
                    return Mathf.Min(12f, peak / Mathf.Max(0.01f, cycleFactor));
                }
                else
                {
                    int lvl = HudController.Instance != null ? HudController.Instance.Level : 1;
                    int bonus = Mathf.Max(0, lvl - 1);
                    return Mathf.Max(1.5f, baseVal - bonus * 0.3f);
                }
            }
        }

        // --- State Properties ---
        public bool IsShieldActive => isUnlocked && !InCooldown && (IsNetworkActive ? m_IsShieldActive.Value : m_OfflineShieldActive);
        public bool InCooldown => m_CooldownTimer > 0f;
        public float CooldownRemaining => Mathf.Max(0f, m_CooldownTimer);
        public float CooldownProgress => InCooldown ? Mathf.Clamp01(1f - (m_CooldownTimer / Mathf.Max(0.01f, EffectiveCooldownDuration))) : 1f;
        public float CurrentShieldHealth => m_CurrentShieldHealth;
        public float MaxShieldHealth => EffectiveMaxShieldHealth;

        private void Awake()
        {
            if (Application.isPlaying)
            {
                RefreshReferences();
            }

            InitVisuals();
            m_CurrentShieldHealth = EffectiveMaxShieldHealth;
        }

        private void Start()
        {
            if (Application.isPlaying)
            {
                if (autoUnlockOnStart) isUnlocked = true;
                if (autoActivateOnStart) SetShieldState(true);
            }
            else
            {
                InitVisuals();
            }
        }

        private void OnValidate()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this == null) return;
                if (generateDefaultVisuals && shieldVisualObject == null)
                {
                    GenerateShieldMesh();
                }
                UpdateVisualsState();
            };
#endif
        }

        private void RefreshReferences()
        {
            m_Animator = GetComponentInChildren<Animator>();
            m_Health = GetComponentInParent<HealthController>() ?? GetComponentInChildren<HealthController>();
        }

        private void InitVisuals()
        {
            if (shieldVisualObject == null && generateDefaultVisuals)
            {
                GenerateShieldMesh();
            }

            UpdateVisualsState();
        }

        private void UpdateVisualsState()
        {
            if (shieldVisualObject != null)
            {
                bool isPickupItem = GetComponent<PickupController>() != null || GetComponentInParent<PickupController>() != null;
                bool shouldBeActive = isPickupItem || IsShieldActive || (!Application.isPlaying && previewInEditor);

                if (shieldVisualObject.activeSelf != shouldBeActive)
                {
                    shieldVisualObject.SetActive(shouldBeActive);
                }
            }
        }

        private void Update()
        {
            if (GetComponent<PickupController>() != null || GetComponentInParent<PickupController>() != null)
            {
                UpdateVisualsState();
                return;
            }

            if (shieldVisualObject == null && generateDefaultVisuals)
            {
                GenerateShieldMesh();
            }

            UpdateVisualsState();

            if (!Application.isPlaying) return;

            // Manejo de Tiempos de Cooldown y Duración Activa
            if (InCooldown)
            {
                m_CooldownTimer -= Time.deltaTime;
                if (m_CooldownTimer <= 0f)
                {
                    m_CooldownTimer = 0f;
                    m_CurrentShieldHealth = EffectiveMaxShieldHealth;
                    m_ActiveTimer = 0f;
                }
            }

            if (IsShieldActive)
            {
                m_ActiveTimer += Time.deltaTime;

                // Desactivación y Cooldown al superar la duración o capacidad de daño
                if (m_ActiveTimer >= EffectiveMaxShieldDuration || m_CurrentShieldHealth <= 0f)
                {
                    StartCooldown();
                }
            }

            // Destello visual de impacto
            if (m_HitFlashTimer > 0f)
            {
                m_HitFlashTimer -= Time.deltaTime;
                if (m_HitFlashTimer <= 0f)
                {
                    RestoreShieldOriginalColors();
                }
            }

            if (IsNetworkActive && !IsOwner) return;

            if (Cursor.visible)
            {
                if (m_ActivatedByInput || IsShieldActive)
                {
                    SetShieldState(false);
                    m_ActivatedByInput = false;
                }
                return;
            }

            if (Mouse.current != null && isUnlocked && !InCooldown)
            {
                var middleButton = Mouse.current.middleButton;
                float scrollValue = Mouse.current.scroll.ReadValue().y;
                bool isScrolling = Mathf.Abs(scrollValue) > 0.01f;

                bool middlePressedThisFrame = middleButton != null && middleButton.wasPressedThisFrame;
                bool middleIsPressed = middleButton != null && middleButton.isPressed;
                bool middleReleasedThisFrame = middleButton != null && middleButton.wasReleasedThisFrame;

                if (inputMode == ShieldInputMode.TogglePress)
                {
                    if (middlePressedThisFrame || isScrolling)
                    {
                        SetShieldState(!IsShieldActive);
                        m_ActivatedByInput = IsShieldActive;
                    }
                }
                else // HoldToActivate
                {
                    if ((middlePressedThisFrame || isScrolling) && IsShieldActive && !m_ActivatedByInput)
                    {
                        SetShieldState(false);
                        m_ActivatedByInput = false;
                    }
                    else if (middleIsPressed)
                    {
                        SetShieldState(true);
                        m_ActivatedByInput = true;
                    }
                    else if (middleReleasedThisFrame || (!middleIsPressed && m_ActivatedByInput))
                    {
                        SetShieldState(false);
                        m_ActivatedByInput = false;
                    }
                }
            }
        }

        private void LateUpdate()
        {
            if (shieldVisualObject != null && (IsShieldActive || (!Application.isPlaying && previewInEditor)))
            {
                shieldVisualObject.transform.localPosition = generatorOffset;
            }
        }

        public void SetShieldState(bool active)
        {
            if (!isUnlocked && active) return;

            if (active && InCooldown) return;

            if (IsShieldActive == active) return;

            if (active)
            {
                if (m_CurrentShieldHealth <= 0f)
                {
                    m_CurrentShieldHealth = EffectiveMaxShieldHealth;
                }
                m_ActiveTimer = 0f;
            }
            else if (IsShieldActive && !InCooldown)
            {
                StartCooldown();
                return;
            }

            if (IsNetworkActive)
            {
                m_IsShieldActive.Value = active;
            }
            else
            {
                bool previous = m_OfflineShieldActive;
                m_OfflineShieldActive = active;
                OnShieldStateChanged(previous, active);
            }
        }

        public void StartCooldown()
        {
            m_CooldownTimer = EffectiveCooldownDuration;
            m_ActiveTimer = 0f;

            if (IsNetworkActive)
            {
                if (m_IsShieldActive.Value) m_IsShieldActive.Value = false;
            }
            else
            {
                if (m_OfflineShieldActive)
                {
                    m_OfflineShieldActive = false;
                    OnShieldStateChanged(true, false);
                }
            }

            if (IsEnemy)
            {
                m_EnemyCycleCount++;
            }
        }

        public override void OnNetworkSpawn()
        {
            m_IsShieldActive.OnValueChanged += OnShieldStateChanged;
        }

        public override void OnNetworkDespawn()
        {
            m_IsShieldActive.OnValueChanged -= OnShieldStateChanged;
        }

        private void OnShieldStateChanged(bool previousValue, bool newValue)
        {
            UpdateVisualsState();

            if (newValue && shieldActivateSound != null)
            {
                AudioSource.PlayClipAtPoint(shieldActivateSound, transform.position);
            }

            if (m_Animator != null && HasParameter(m_Animator, shieldAnimBool))
            {
                m_Animator.SetBool(shieldAnimBool, newValue);
            }
        }

        // --- ACCIONES DEDICADAS DE INVENTARIO ---

        public void OnQuitItem(GameObject player) => SetShieldState(false);

        public void OnDropItem(GameObject player) => SetShieldState(false);

        public void OnPickupItem(GameObject player) => isUnlocked = true;

        public void OnUseItem(GameObject entity)
        {
            if (entity == null) return;

            GameObject playerRoot = entity.transform.root.gameObject;

            ShieldController actualController = playerRoot.GetComponentsInChildren<ShieldController>(true)
                .FirstOrDefault(s => s != null && s != this);

            if (actualController != null)
            {
                actualController.isUnlocked = true;

                if (actualController.toggleOnUse)
                {
                    actualController.SetShieldState(!actualController.IsShieldActive);
                }
                else
                {
                    actualController.SetShieldState(true);
                }

                if (this != actualController && transform.IsChildOf(playerRoot.transform))
                {
                    this.enabled = false;
                }
            }
            else
            {
                this.isUnlocked = true;

                if (toggleOnUse)
                {
                    SetShieldState(!IsShieldActive);
                }
                else
                {
                    SetShieldState(true);
                }
            }
        }

        private Material GetHexagonPurpleMaterial()
        {
            Material mat = null;

#if UNITY_EDITOR
            string path = "Assets/Characters/Materials/Material_HexagonPurple1 1.mat";
            mat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Characters/Materials/Material_HexagonPurple1.mat");
            }
#endif

            if (mat == null)
            {
                mat = Resources.Load<Material>("Material_HexagonPurple1 1") ?? Resources.Load<Material>("Material_HexagonPurple1");
            }

            if (mat == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ??
                               Shader.Find("Universal Render Pipeline/Unlit") ??
                               Shader.Find("Standard");
                mat = new Material(shader);
                mat.name = "Material_HexagonPurple1 1";
                Color purple = new Color(0.6f, 0.1f, 0.9f, 0.7f);
                if (mat.HasProperty("_Color")) mat.color = purple;
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", purple);
                if (mat.HasProperty("_EmissionColor"))
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", purple * 2.0f);
                }
            }

            return mat;
        }

        /// <summary>
        /// Genera una esfera de energía 3D con colisionador Trigger que recubre al robot.
        /// </summary>
        [ContextMenu("Re-Generate Shield Mesh")]
        public void GenerateShieldMesh()
        {
            RefreshReferences();

            Transform targetParent = transform;
            if (m_Animator != null && m_Animator.isHuman)
            {
                Transform chestBone = m_Animator.GetBoneTransform(HumanBodyBones.Chest)
                                   ?? m_Animator.GetBoneTransform(HumanBodyBones.Spine);
                if (chestBone != null)
                {
                    targetParent = chestBone;
                }
            }

            Transform existing = transform.Find("ShieldRender") ?? targetParent.Find("ShieldRender");
            if (existing != null)
            {
                shieldVisualObject = existing.gameObject;
            }
            else
            {
                shieldVisualObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                shieldVisualObject.name = "ShieldRender";
                shieldVisualObject.transform.SetParent(targetParent, false);
            }

            // Colisionador Trigger para interceptar disparos e impactos en la superficie de la esfera
            SphereCollider sc = shieldVisualObject.GetComponent<SphereCollider>();
            if (sc == null) sc = shieldVisualObject.AddComponent<SphereCollider>();
            sc.isTrigger = true;
            sc.radius = 0.5f;

            shieldVisualObject.transform.localPosition = generatorOffset;
            shieldVisualObject.transform.localRotation = Quaternion.identity;
            shieldVisualObject.transform.localScale = generatorScale;

            var mr = shieldVisualObject.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                Material hexMat = GetHexagonPurpleMaterial();
                if (hexMat != null)
                {
                    mr.sharedMaterial = hexMat;
                }
            }

            UpdateVisualsState();
        }

        public float ProcessIncomingDamage(float damage)
        {
            if (!IsShieldActive || InCooldown) return damage;

            float dr = EffectiveDamageReduction;
            float unmitigatedDamage = damage * (1f - dr);

            // El escudo absorbe el impacto de su capacidad interna de vida
            m_CurrentShieldHealth -= damage;

            TriggerShieldHitEffect();

            if (m_CurrentShieldHealth <= 0f)
            {
                m_CurrentShieldHealth = 0f;
                StartCooldown();
            }

            return unmitigatedDamage;
        }

        public int ProcessIncomingDamage(int damage)
        {
            if (!IsShieldActive || InCooldown) return damage;

            return Mathf.RoundToInt(ProcessIncomingDamage((float)damage));
        }

        public int MitigateDamage(int damage)
        {
            return ProcessIncomingDamage(damage);
        }

        private void TriggerShieldHitEffect()
        {
            if (shieldBlockSound != null)
            {
                AudioSource.PlayClipAtPoint(shieldBlockSound, transform.position);
            }

            if (shieldVisualObject != null)
            {
                var mr = shieldVisualObject.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    CacheShieldOriginalColors(mr);
                    foreach (var mat in mr.materials)
                    {
                        if (mat == null) continue;
                        Color flash = new Color(1f, 1f, 1f, 0.95f);
                        if (mat.HasProperty("_Color")) mat.color = flash;
                        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", flash);
                        if (mat.HasProperty("_EmissionColor"))
                        {
                            mat.EnableKeyword("_EMISSION");
                            mat.SetColor("_EmissionColor", flash * 3.5f);
                        }
                    }
                    m_HitFlashTimer = 0.12f;
                }
            }
        }

        private void CacheShieldOriginalColors(MeshRenderer mr)
        {
            if (mr == null) return;
            foreach (var mat in mr.materials)
            {
                if (mat == null || m_ShieldOriginalColors.ContainsKey(mat)) continue;
                if (mat.HasProperty("_Color")) m_ShieldOriginalColors[mat] = mat.color;
                else if (mat.HasProperty("_BaseColor")) m_ShieldOriginalColors[mat] = mat.GetColor("_BaseColor");
            }
        }

        private void RestoreShieldOriginalColors()
        {
            foreach (var kvp in m_ShieldOriginalColors)
            {
                if (kvp.Key == null) continue;
                if (kvp.Key.HasProperty("_Color")) kvp.Key.color = kvp.Value;
                if (kvp.Key.HasProperty("_BaseColor")) kvp.Key.SetColor("_BaseColor", kvp.Value);
            }
        }

        private bool HasParameter(Animator animator, string paramName)
        {
            if (animator == null) return false;
            foreach (AnimatorControllerParameter param in animator.parameters)
                if (param.name == paramName) return true;
            return false;
        }
    }
}
