using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using Crafting.Scripts;
using System.Linq;

namespace Combating.Scripts
{
    public enum ShieldInputMode
    {
        HoldToActivate, // Mantener botón central de la rueda para activar, soltar para desactivar
        TogglePress     // Pulsar/girar la rueda del mouse para alternar (activar/desactivar)
    }

    /// <summary>
    /// Sistema de escudo de energía con soporte para Input (Rueda del mouse), Red (Netcode),
    /// uso desde inventario (IItemFunctional, IItemUseAction, IItemQuitAction, IItemDropAction, IItemPickupAction),
    /// mitigación de daño y visuales personalizables/autogenerados.
    /// </summary>
    [ExecuteAlways]
    public class ShieldController : NetworkBehaviour, IItemFunctional, IItemUseAction, IItemQuitAction, IItemDropAction, IItemPickupAction
    {
        [Header("Shield Settings")]
        public bool isUnlocked = true; // Permiso para usar el escudo
        [Tooltip("1.0 = Bloqueo total (100%), 0.5 = Mitiga el 50% del daño")]
        [Range(0f, 1f)]
        public float damageReduction = 0.5f;

        [Header("Activation & Input Settings")]
        [Tooltip("Modo de entrada de la rueda del mouse: HoldToActivate (mantener botón central) o TogglePress (pulsar/girar rueda para encender/apagar)")]
        public ShieldInputMode inputMode = ShieldInputMode.HoldToActivate;
        [Tooltip("Si es true, se desbloquea automáticamente al iniciar la escena.")]
        public bool autoUnlockOnStart = false;
        [Tooltip("Si es true, se activa automáticamente al iniciar la escena.")]
        public bool autoActivateOnStart = false;
        [Tooltip("Si es true, al pulsar Usar en el inventario alterna el estado del escudo (On/Off). Si es false, siempre lo activa.")]
        public bool toggleOnUse = true;

        [Header("Visuals & Audio")]
        [Tooltip("Permite o desactiva la generación automática de la fuente de energía 3D por defecto si no se asigna shieldVisualObject.")]
        public bool generateDefaultVisuals = true;
        [Tooltip("Visualizar el ShieldRender en el Editor para previsualizar su ubicación.")]
        public bool previewInEditor = true;
        public GameObject shieldVisualObject;
        public Color shieldColor = new Color(0f, 0.5f, 1f, 0.8f);

        [Header("Back Generator Configuration")]
        [Tooltip("Posición relativa en la espalda del robot o hueso (X, Y, Z)")]
        public Vector3 generatorOffset = new Vector3(0f, 0.8f, -0.35f);
        [Tooltip("Tamaño del cilindro fuente de energía (Ancho, Alto, Profundidad)")]
        public Vector3 generatorScale = new Vector3(0.3f, 0.6f, 0.3f);

        public AudioClip shieldActivateSound;
        public AudioClip shieldBlockSound;

        [Header("Animation")]
        public string shieldAnimBool = "isShieldActive";

        private Animator m_Animator;
        private HealthController m_Health;

        // Indicador de si el escudo fue activado vía mouse
        private bool m_ActivatedByInput = false;

        // NetworkVariable para sincronizar con otros jugadores en multijugador
        private readonly NetworkVariable<bool> m_IsShieldActive = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner
        );

        // Estado local para cuando se prueba sin red (offline)
        private bool m_OfflineShieldActive = false;

        // Propiedad que devuelve el estado actual (sea online u offline)
        public bool IsShieldActive => isUnlocked && (IsNetworkActive ? m_IsShieldActive.Value : m_OfflineShieldActive);

        private bool IsNetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;

        private void Awake()
        {
            if (Application.isPlaying)
            {
                RefreshReferences();
            }

            InitVisuals();
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
            if (generateDefaultVisuals && shieldVisualObject == null)
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.delayCall += () =>
                {
                    if (this != null && generateDefaultVisuals && shieldVisualObject == null)
                    {
                        GenerateShieldMesh();
                    }
                };
#endif
            }
            UpdateVisualsState();
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
                bool shouldBeActive = IsShieldActive || (!Application.isPlaying && previewInEditor);
                if (shieldVisualObject.activeSelf != shouldBeActive)
                {
                    shieldVisualObject.SetActive(shouldBeActive);
                }
            }
        }

        private void Update()
        {
            // Evitar procesar lógica si este componente está montado en un ítem/pickup en el suelo
            if (GetComponent<PickupController>() != null || GetComponentInParent<PickupController>() != null)
            {
                return;
            }

            if (shieldVisualObject == null && generateDefaultVisuals)
            {
                GenerateShieldMesh();
            }

            UpdateVisualsState();

            if (!Application.isPlaying) return;

            // Solo el dueño del jugador procesa su input local
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

            // Detección de la rueda del mouse (Botón central o Giro/Scroll)
            if (Mouse.current != null && isUnlocked)
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
                        // Si el escudo estaba activo (ej: por inventario) e interactuamos con la rueda, lo desactivamos
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
            // Mantiene el offset exacto en cada frame después de procesar animaciones
            if (shieldVisualObject != null && (IsShieldActive || (!Application.isPlaying && previewInEditor)))
            {
                shieldVisualObject.transform.localPosition = generatorOffset;
            }
        }

        public void SetShieldState(bool active)
        {
            if (!isUnlocked && active) return;

            // Si el estado no cambió, no hacemos nada
            if (IsShieldActive == active) return;

            if (IsNetworkActive)
            {
                m_IsShieldActive.Value = active;
            }
            else
            {
                // Modo offline: guardamos el cambio localmente y actualizamos visuales
                bool previous = m_OfflineShieldActive;
                m_OfflineShieldActive = active;
                OnShieldStateChanged(previous, active);
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

            // Reproducir sonido solo al encender
            if (newValue && shieldActivateSound != null)
            {
                AudioSource.PlayClipAtPoint(shieldActivateSound, transform.position);
            }

            // Actualizar el Animator
            if (m_Animator != null && HasParameter(m_Animator, shieldAnimBool))
            {
                m_Animator.SetBool(shieldAnimBool, newValue);
            }
        }

        // --- ACCIONES DEDICADAS DE INVENTARIO ---

        public void OnUseItem(GameObject player)
        {
            ApplyEffect(player);
        }

        public void OnQuitItem(GameObject player)
        {
            SetShieldState(false);
            Debug.Log($"<color=blue>[Shield]</color> Sistema de defensa DESACTIVADO vía QUIT en {player.name}.");
        }

        public void OnDropItem(GameObject player, GameObject droppedInstance)
        {
            SetShieldState(false);
            Debug.Log($"<color=blue>[Shield]</color> Sistema de defensa DESACTIVADO vía DROP en {player.name}.");
        }

        public void OnPickupItem(GameObject player)
        {
            isUnlocked = true;
            Debug.Log($"<color=blue>[Shield]</color> Sistema de defensa DESBLOQUEADO vía PICKUP en {player.name}.");
        }

        /// <summary>
        /// Implementación de IItemFunctional para activar/desbloquear desde inventario o pickups.
        /// </summary>
        public void ApplyEffect(GameObject entity)
        {
            if (entity == null) return;

            GameObject playerRoot = entity.transform.root.gameObject;

            // Buscar un ShieldController existente en el robot que NO sea esta misma instancia
            ShieldController actualController = playerRoot.GetComponentsInChildren<ShieldController>(true)
                .FirstOrDefault(s => s != null && s != this);

            if (actualController != null)
            {
                // El robot ya posee un ShieldController principal (ej: en el PlayerPrefab)
                actualController.isUnlocked = true;

                if (actualController.toggleOnUse)
                {
                    actualController.SetShieldState(!actualController.IsShieldActive);
                }
                else
                {
                    actualController.SetShieldState(true);
                }

                // Desactivar esta instancia duplicada en el ítem clonado para evitar conflicto de updates
                if (this != actualController && transform.IsChildOf(playerRoot.transform))
                {
                    this.enabled = false;
                }

                Debug.Log($"<color=blue>[Shield]</color> Sistema de defensa en {playerRoot.name} ajustado a {(actualController.IsShieldActive ? "ACTIVADO" : "DESACTIVADO")}.");
            }
            else
            {
                // El robot no tenía ShieldController, activamos este
                this.isUnlocked = true;

                if (toggleOnUse)
                {
                    SetShieldState(!IsShieldActive);
                }
                else
                {
                    SetShieldState(true);
                }

                Debug.Log($"<color=blue>[Shield]</color> Sistema de defensa ACTIVADO en {playerRoot.name}.");
            }
        }

        /// <summary>
        /// Genera una fuente de energía cilíndrica ubicada en la espalda/hueso del robot.
        /// </summary>
        [ContextMenu("Re-Generate Shield Mesh")]
        public void GenerateShieldMesh()
        {
            RefreshReferences();

            // 1. Intentar buscar el hueso del Pecho o Columna para acompañar las animaciones de movimiento
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
                shieldVisualObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                shieldVisualObject.name = "ShieldRender";
                shieldVisualObject.transform.SetParent(targetParent, false);

                // Quitar colisionador para que no interfiera con la física ni el movimiento
                var col = shieldVisualObject.GetComponent<Collider>();
                if (col != null)
                {
                    col.enabled = false;
                    if (Application.isPlaying)
                    {
                        Destroy(col);
                    }
                    else
                    {
#if UNITY_EDITOR
                        UnityEditor.EditorApplication.delayCall += () =>
                        {
                            if (col != null) DestroyImmediate(col);
                        };
#else
                        Destroy(col);
#endif
                    }
                }
            }

            // Posición relativa al nuevo padre (hueso o raíz)
            shieldVisualObject.transform.localPosition = generatorOffset;
            shieldVisualObject.transform.localRotation = Quaternion.identity;
            shieldVisualObject.transform.localScale = generatorScale;

            var mr = shieldVisualObject.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                               Shader.Find("Universal Render Pipeline/Lit") ??
                               Shader.Find("Sprites/Default") ??
                               Shader.Find("Standard");

                if (mr.sharedMaterial == null || mr.sharedMaterial.shader != shader)
                {
                    mr.sharedMaterial = new Material(shader);
                    if (mr.sharedMaterial.HasProperty("_Surface")) mr.sharedMaterial.SetFloat("_Surface", 1); // Transparent
                    if (mr.sharedMaterial.HasProperty("_SrcBlend")) mr.sharedMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    if (mr.sharedMaterial.HasProperty("_DstBlend")) mr.sharedMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    if (mr.sharedMaterial.HasProperty("_ZWrite")) mr.sharedMaterial.SetInt("_ZWrite", 0);
                    mr.sharedMaterial.renderQueue = 3000;
                }

                mr.sharedMaterial.color = shieldColor;
                if (mr.sharedMaterial.HasProperty("_BaseColor")) mr.sharedMaterial.SetColor("_BaseColor", shieldColor);
                if (mr.sharedMaterial.HasProperty("_Color")) mr.sharedMaterial.SetColor("_Color", shieldColor);
                if (mr.sharedMaterial.HasProperty("_EmissionColor"))
                {
                    mr.sharedMaterial.EnableKeyword("_EMISSION");
                    mr.sharedMaterial.SetColor("_EmissionColor", shieldColor * 2.5f);
                }
            }

            UpdateVisualsState();
        }

        /// <summary>
        /// Procesa el daño entrante aplicando la reducción configurada si el escudo está activo.
        /// </summary>
        public float ProcessIncomingDamage(float damage)
        {
            if (!IsShieldActive) return damage;

            if (shieldBlockSound != null)
            {
                AudioSource.PlayClipAtPoint(shieldBlockSound, transform.position);
            }

            return damage * (1f - damageReduction);
        }

        public int ProcessIncomingDamage(int damage)
        {
            if (!IsShieldActive) return damage;

            return Mathf.RoundToInt(ProcessIncomingDamage((float)damage));
        }

        public int MitigateDamage(int damage)
        {
            return ProcessIncomingDamage(damage);
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