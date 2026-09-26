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
    /// Sistema de escudo de energía (esférico con Material_HexagonPurple1 1) con soporte para Input (Rueda del mouse),
    /// Red (Netcode), uso desde inventario y pickups, mitigación de daño y visuales esféricas autogeneradas.
    /// </summary>
    [ExecuteAlways]
    public class ShieldController : NetworkBehaviour, IItemUseAction, IItemQuitAction, IItemDropAction, IItemPickupAction
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
        [Tooltip("Permite o desactiva la generación automática de la esfera 3D por defecto si no se asigna shieldVisualObject.")]
        public bool generateDefaultVisuals = true;
        [Tooltip("Visualizar el ShieldRender en el Editor para previsualizar su ubicación.")]
        public bool previewInEditor = true;
        public GameObject shieldVisualObject;

        [Header("Shield Sphere Configuration")]
        [Tooltip("Posición relativa del centro de la esfera (X, Y, Z)")]
        public Vector3 generatorOffset = new Vector3(0f, 0.9f, 0f);
        [Tooltip("Escala de la esfera del escudo (Ancho, Alto, Profundidad)")]
        public Vector3 generatorScale = new Vector3(2.2f, 2.2f, 2.2f);

        public AudioClip shieldActivateSound;
        public AudioClip shieldBlockSound;

        [Header("Animation")]
        public string shieldAnimBool = "isShieldActive";

        private Animator m_Animator;
        private HealthController m_Health;

        private bool m_ActivatedByInput = false;

        private readonly NetworkVariable<bool> m_IsShieldActive = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner
        );

        private bool m_OfflineShieldActive = false;

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
                // Si está montado sobre un ítem/pickup en el suelo, siempre es visible
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

            if (IsShieldActive == active) return;

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
        /// Genera una esfera de energía 3D que recubre al robot usando el Material_HexagonPurple1 1.
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
                // Reemplazo definitivo del Cilindro por Esfera
                shieldVisualObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                shieldVisualObject.name = "ShieldRender";
                shieldVisualObject.transform.SetParent(targetParent, false);

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
