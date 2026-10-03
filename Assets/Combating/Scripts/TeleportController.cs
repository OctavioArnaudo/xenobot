using System.Collections;
using UnityEngine;

namespace Combating.Scripts
{
    /// <summary>
    /// Controla la lógica de teletransporte entre dos puntos interconectados (Portal A y Portal B).
    /// Auto-genera y configura visuales de portales cilíndricos ovalados para representar los puntos interconectados.
    /// </summary>
    public class TeleportController : MonoBehaviour
    {
        [Header("Ajustes de Teletransporte")]
        [Tooltip("Distancia por delante del portal de destino hacia donde saldrá expulsado el jugador.")]
        [SerializeField] private float spawnOffset = 1.5f;

        [Tooltip("Tiempo en segundos antes de poder volver a usar el teletransporte.")]
        [SerializeField] private float cooldownTime = 1.0f;

        [Tooltip("Tag requerido para activar el teletransporte.")]
        [SerializeField] private string playerTag = "Player";

        [Header("Ajustes de Cámara")]
        [Tooltip("Si está activo, intenta rotar la cámara principal/Cinemachine para alinearse con la dirección del portal.")]
        [SerializeField] private bool rotateCamera = true;

        [Header("Visuales de Portales Interconectados")]
        [Tooltip("Color característico para el Portal A.")]
        [SerializeField] private Color portalColorA = new Color(0f, 0.85f, 1f, 0.85f); // Cyan eléctrico

        [Tooltip("Color característico para el Portal B.")]
        [SerializeField] private Color portalColorB = new Color(1f, 0.15f, 0.85f, 0.85f); // Magenta Neón

        [Tooltip("Dimensiones del portal cilíndrico ovalado (Ancho, Alto, Profundidad).")]
        [SerializeField] private Vector3 portalScale = new Vector3(1.6f, 2.4f, 0.2f);

        [Tooltip("Distancia por defecto entre Portal A y Portal B si son creados automáticamente.")]
        [SerializeField] private float defaultPortalDistance = 6.0f;

        [Tooltip("Añade una luz puntual en cada portal con el color correspondiente.")]
        [SerializeField] private bool addPortalLight = true;

        private Transform portalA;
        private Transform portalB;
        private bool isOnCooldown;

        private void Awake()
        {
            InitializePortals();
        }

        private void OnValidate()
        {
            if (!Application.isPlaying)
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.delayCall -= DelayInitializePortals;
                UnityEditor.EditorApplication.delayCall += DelayInitializePortals;
#endif
            }
        }

#if UNITY_EDITOR
        private void DelayInitializePortals()
        {
            if (this == null) return;
            InitializePortals();
        }
#endif

        [ContextMenu("Reconstruir Portales")]
        public void InitializePortals()
        {
            Rigidbody parentRb = GetComponent<Rigidbody>();
            if (parentRb == null)
            {
                parentRb = gameObject.AddComponent<Rigidbody>();
            }
            parentRb.isKinematic = true;
            parentRb.useGravity = false;

            EnsurePortalChildrenExist();

            if (portalA != null)
            {
                SetupPortalChild(portalA, isPortalA: true, portalColorA);
            }

            if (portalB != null)
            {
                SetupPortalChild(portalB, isPortalA: false, portalColorB);
            }
        }

        /// <summary>
        /// Garantiza que existan exactamente los 2 objetos hijos Portal_A y Portal_B hardcodeados.
        /// </summary>
        private void EnsurePortalChildrenExist()
        {
            if (transform.childCount < 1)
            {
                GameObject pA = new GameObject("Portal_A");
                pA.transform.SetParent(transform, false);
                pA.transform.localPosition = new Vector3(-defaultPortalDistance * 0.5f, 0f, 0f);
            }

            if (transform.childCount < 2)
            {
                GameObject pB = new GameObject("Portal_B");
                pB.transform.SetParent(transform, false);
                pB.transform.localPosition = new Vector3(defaultPortalDistance * 0.5f, 0f, 0f);
            }

            portalA = transform.GetChild(0);
            portalB = transform.GetChild(1);

            portalA.name = "Portal_A";
            portalB.name = "Portal_B";
        }

        /// <summary>
        /// Configura el Collider Trigger, el script de activación y la visual cilíndrica ovalada del portal.
        /// </summary>
        private void SetupPortalChild(Transform portalTransform, bool isPortalA, Color color)
        {
            Collider col = portalTransform.GetComponent<Collider>();
            if (col == null)
            {
                BoxCollider box = portalTransform.gameObject.AddComponent<BoxCollider>();
                col = box;
            }
            if (col is BoxCollider boxCol)
            {
                boxCol.size = new Vector3(portalScale.x, portalScale.y, Mathf.Max(portalScale.z * 2f, 1.0f));
                boxCol.center = Vector3.zero;
            }
            col.isTrigger = true;
            var scripts = portalTransform.GetComponents<MonoBehaviour>();
            foreach (var s in scripts)
            {
                if (s == null)
                {
                    if (Application.isPlaying) Destroy(s);
                    else DestroyImmediate(s);
                }
            }
            SetupPortalVisual(portalTransform, color);
            SetupPortalLight(portalTransform, color);
        }

        /// <summary>
        /// Genera o actualiza la malla cilíndrica ovalada con material con emisión de color.
        /// </summary>
        private void SetupPortalVisual(Transform portalTransform, Color color)
        {
            Transform visualTransform = portalTransform.Find("PortalVisual");
            GameObject visualObj;

            if (visualTransform == null)
            {
                visualObj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                visualObj.name = "PortalVisual";
                visualObj.transform.SetParent(portalTransform, false);

                // Remover el collider predeterminado del cilindro para no interferir con el trigger
                Collider primitiveCollider = visualObj.GetComponent<Collider>();
                if (primitiveCollider != null)
                {
                    if (Application.isPlaying) Destroy(primitiveCollider);
                    else DestroyImmediate(primitiveCollider);
                }
            }
            else
            {
                visualObj = visualTransform.gameObject;
            }

            // Aplicar transformación para forma cilíndrica ovalada
            visualObj.transform.localPosition = Vector3.zero;
            visualObj.transform.localRotation = Quaternion.identity;
            visualObj.transform.localScale = portalScale;

            // Aplicar material coloreado con emisión
            Renderer renderer = visualObj.GetComponent<Renderer>();
            if (renderer != null)
            {
                Material portalMat = CreateOrUpdatePortalMaterial(color);
                renderer.sharedMaterial = portalMat;
            }
        }

        /// <summary>
        /// Crea un material luminoso adecuado para el portal.
        /// </summary>
        private Material CreateOrUpdatePortalMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                         ?? Shader.Find("Standard")
                         ?? Shader.Find("Sprites/Default")
                         ?? Shader.Find("Unlit/Color");

            Material mat = new Material(shader)
            {
                name = $"PortalMat_{ColorUtility.ToHtmlStringRGB(color)}",
                hideFlags = HideFlags.DontSave
            };

            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);

            if (mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", color * 1.8f);
            }

            return mat;
        }

        /// <summary>
        /// Agrega o ajusta una luz puntual en el portal para mejorar el aspecto visual.
        /// </summary>
        private void SetupPortalLight(Transform portalTransform, Color color)
        {
            Transform lightTransform = portalTransform.Find("PortalLight");
            Light portalLight;

            if (lightTransform == null)
            {
                if (!addPortalLight) return;
                GameObject lightObj = new GameObject("PortalLight");
                lightObj.transform.SetParent(portalTransform, false);
                lightObj.transform.localPosition = Vector3.forward * 0.2f;
                portalLight = lightObj.AddComponent<Light>();
            }
            else
            {
                if (!addPortalLight)
                {
                    if (Application.isPlaying) Destroy(lightTransform.gameObject);
                    else DestroyImmediate(lightTransform.gameObject);
                    return;
                }
                portalLight = lightTransform.GetComponent<Light>();
            }

            if (portalLight != null)
            {
                portalLight.type = LightType.Point;
                portalLight.color = color;
                portalLight.intensity = 2.5f;
                portalLight.range = 5.0f;
            }
        }

        public string PlayerTag => playerTag;

        public void OnPortalTriggerEntered(GameObject playerObj, bool cameFromA)
        {
            if (isOnCooldown) return;
            PlayerController pc = playerObj.GetComponent<PlayerController>() ?? playerObj.GetComponentInParent<PlayerController>();
            GameObject targetPlayer = pc != null ? pc.gameObject : (playerObj.transform.root != null ? playerObj.transform.root.gameObject : playerObj);
            if (pc == null && !targetPlayer.CompareTag(playerTag) && !playerObj.CompareTag(playerTag)) return;
            Transform destination = cameFromA ? portalB : portalA;
            if (destination == null) return;
            TeleportPlayer(targetPlayer, destination);
        }

        private void TeleportPlayer(GameObject player, Transform destination)
        {
            StartCoroutine(CooldownRoutine());
            CharacterController cc = player.GetComponent<CharacterController>() ?? player.GetComponentInChildren<CharacterController>();
            if (cc != null) cc.enabled = false;
            Rigidbody rb = player.GetComponent<Rigidbody>() ?? player.GetComponentInChildren<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            Vector3 exitPosition = destination.position + (destination.forward * spawnOffset);
            Quaternion exitRotation = destination.rotation;
            player.transform.position = exitPosition;
            player.transform.rotation = exitRotation;
            Physics.SyncTransforms();
            if (rotateCamera) RotatePlayerCamera(player, destination);
            if (cc != null) cc.enabled = true;
        }

        private void RotatePlayerCamera(GameObject player, Transform destination)
        {
            Camera playerCamera = player.GetComponentInChildren<Camera>();
            if (playerCamera != null)
            {
                Vector3 targetEuler = destination.eulerAngles;
                playerCamera.transform.rotation = Quaternion.Euler(0f, targetEuler.y, 0f);
            }
        }

        private IEnumerator CooldownRoutine()
        {
            isOnCooldown = true;
            yield return new WaitForSeconds(cooldownTime);
            isOnCooldown = false;
        }

        private void OnDrawGizmos()
        {
            if (portalA == null || portalB == null)
            {
                if (transform.childCount >= 2)
                {
                    portalA = transform.GetChild(0);
                    portalB = transform.GetChild(1);
                }
                else
                {
                    return;
                }
            }

            DrawPortalGizmo(portalA, portalColorA);
            DrawPortalGizmo(portalB, portalColorB);

            // Línea visual de interconexión entre ambos portales
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(portalA.position, portalB.position);
        }

        private void DrawPortalGizmo(Transform portal, Color portalColor)
        {
            if (portal == null) return;

            Gizmos.color = portalColor;

            // Dibujar estructura del portal ovalado
            Matrix4x4 oldMatrix = Gizmos.matrix;
            Gizmos.matrix = portal.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, portalScale);
            Gizmos.matrix = oldMatrix;

            // Punto y dirección de salida
            Vector3 exitPoint = portal.position + (portal.forward * spawnOffset);
            Gizmos.color = Color.green;
            Gizmos.DrawSphere(exitPoint, 0.25f);
            Gizmos.DrawLine(portal.position, exitPoint);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (isOnCooldown || portalA == null || portalB == null) return;
            Collider colA = portalA.GetComponent<Collider>();
            Collider colB = portalB.GetComponent<Collider>();
            bool isA = colA != null && colA.bounds.Intersects(other.bounds);
            bool isB = colB != null && colB.bounds.Intersects(other.bounds);
            if (!isA && !isB)
            {
                float distA = Vector3.SqrMagnitude(other.transform.position - portalA.position);
                float distB = Vector3.SqrMagnitude(other.transform.position - portalB.position);
                isA = distA <= distB;
            }
            OnPortalTriggerEntered(other.gameObject, isA);
        }
    }
}