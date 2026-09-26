using UnityEngine;
using Combating.Scripts;
using System.Linq;

namespace Crafting.Scripts
{
    public enum VisorType
    {
        VisorTermico,      // Visor Térmico (Detección de calor de personajes/enemigos en rojo/naranja)
        VisorEstadisticas, // Visor de Estadísticas (HUD táctico cían con HP, Nivel, Atk/Def sobre objetivos)
        VisorObjetos       // Visor de Objetos (Escáner de loot, pickups y objetos en amarillo/oro)
    }

    /// <summary>
    /// Controlador de Máscaras y Visores tácticos.
    /// Superpone el visor/máscara sobre la cabeza del personaje sin reemplazar su cuerpo base.
    /// Aplica efectos visuales HUD en cámara al ser usado desde el inventario.
    /// Compliant con AGENTS.md (Clean Prefabs con Optional<T>).
    /// </summary>
    public class MaskController : MonoBehaviour, IItemUseAction, IItemQuitAction, IItemDropAction
    {
        // --- Hardcoded Internal Defaults (AGENTS.md) ---
        private const VisorType DEFAULT_VISOR_TYPE = VisorType.VisorTermico;
        private static readonly Vector3 DEFAULT_HEAD_BONE_OFFSET = new Vector3(0f, 0.05f, 0.08f);
        private static readonly Vector3 DEFAULT_ROOT_HEAD_OFFSET = new Vector3(0f, 1.40f, 0.15f);
        private static readonly Vector3 DEFAULT_HEAD_SCALE = new Vector3(0.35f, 0.35f, 0.35f);

        [Header("Settings de Máscara")]
        public string renderTag = "Render";

        [Header("Sobrescrituras Opcionales del Inspector")]
        public Optional<VisorType> visorTypeOverride;
        public Optional<Vector3> headOffsetOverride;
        public Optional<Vector3> headScaleOverride;

        public VisorType EffectiveVisorType => visorTypeOverride.GetValue(DEFAULT_VISOR_TYPE);
        public Vector3 EffectiveHeadOffset => headOffsetOverride.GetValue(DEFAULT_HEAD_BONE_OFFSET);
        public Vector3 EffectiveHeadScale => headScaleOverride.GetValue(DEFAULT_HEAD_SCALE);

        private bool _isEquipped = false;
        private GameObject _playerRoot;
        private Camera _mainCamCache;

        // Visual textures & styles for HUD Overlay
        private Texture2D _texOverlay, _texFrame, _texTargetBox;
        private GUIStyle _hudHeaderStyle, _hudValueStyle, _hudTargetStyle;
        private bool _stylesReady;

        private void Awake()
        {
            if (transform.Find("MaskRender") == null)
            {
                GenerateMaskMesh();
            }
        }

        public void OnUseItem(GameObject player) => ApplyEffect(player);

        public void OnQuitItem(GameObject player)
        {
            if (!_isEquipped) return;

            _isEquipped = false;

            if (gameObject != null)
            {
                Destroy(gameObject);
            }
        }

        public void OnDropItem(GameObject player) => OnQuitItem(player);

        public void ApplyEffect(GameObject player)
        {
            if (_isEquipped) return;

            _playerRoot = player != null ? player.transform.root.gameObject : gameObject;

            // 1. Si existe un CostumeController en esta instancia o prefab de máscara, desactivarlo para evitar que oculte el cuerpo
            var costumeControllers = GetComponents<CostumeController>().Concat(GetComponentsInChildren<CostumeController>(true));
            foreach (var c in costumeControllers)
            {
                c.enabled = false;
            }

            // 2. Retaggear cualquier tag "Render" dentro de la máscara a "Untagged"
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t != transform && t.CompareTag("Render"))
                {
                    t.tag = "Untagged";
                }
            }

            // 3. Garantizar que TODOS los renderizadores del cuerpo original del personaje permanezcan 100% ACTIVOS
            Renderer[] allRenders = _playerRoot.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer r in allRenders)
            {
                if (r == null || r.transform.IsChildOf(transform)) continue;
                r.enabled = true;
                if (!r.gameObject.activeSelf)
                {
                    r.gameObject.SetActive(true);
                }
            }

            // 4. Asegurar que los GameObjects padres del renderRoot del personaje están activos
            Transform taggedRender = _playerRoot.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t != _playerRoot.transform && t != transform && t.CompareTag("Render"));
            if (taggedRender != null)
            {
                taggedRender.gameObject.SetActive(true);
                if (taggedRender.parent != null) taggedRender.parent.gameObject.SetActive(true);
            }

            // 5. Localizar el hueso de la cabeza
            Transform headTransform = FindHeadTransform(_playerRoot);

            // 6. Deshabilitar NetworkObject en caso de existir en el accesorio
            if (TryGetComponent<Unity.Netcode.NetworkObject>(out var netObj))
            {
                netObj.enabled = false;
            }

            // 7. Adjuntar la máscara a la cabeza (o arriba del cuerpo si es el root)
            Vector3 finalOffset = (headTransform == _playerRoot.transform || headTransform == taggedRender)
                ? headOffsetOverride.GetValue(DEFAULT_ROOT_HEAD_OFFSET)
                : headOffsetOverride.GetValue(DEFAULT_HEAD_BONE_OFFSET);

            transform.SetParent(headTransform, false);
            transform.localPosition = finalOffset;
            transform.localRotation = Quaternion.identity;
            transform.localScale = EffectiveHeadScale;

            gameObject.SetActive(true);
            _isEquipped = true;

            // 8. Limpiar componentes de mundo
            if (TryGetComponent<PickupController>(out var p)) Destroy(p);
            if (TryGetComponent<Rigidbody>(out var rb)) Destroy(rb);
            if (netObj != null) Destroy(netObj);
            foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = false;
        }

        private Transform FindHeadTransform(GameObject player)
        {
            if (player == null) return transform;

            // 1. Check Humanoid Animator bone
            Animator anim = player.GetComponentInChildren<Animator>();
            if (anim != null && anim.isHuman)
            {
                Transform headBone = anim.GetBoneTransform(HumanBodyBones.Head);
                if (headBone != null) return headBone;
            }

            // 2. Comprehensive Search across all child transforms for head/neck/face bone keywords
            Transform[] allTransforms = player.GetComponentsInChildren<Transform>(true);

            string[] headKeywords = new string[] {
                "head", "cabeza", "face", "cara", "helmet", "casco",
                "bip001 head", "mixamorig:head", "joint_head", "bone_head", "rig_head"
            };

            foreach (var kw in headKeywords)
            {
                foreach (Transform t in allTransforms)
                {
                    if (t != null && t.name.ToLower().Contains(kw))
                    {
                        return t;
                    }
                }
            }

            // 3. Neck / Chest bone fallback
            foreach (Transform t in allTransforms)
            {
                if (t != null && (t.name.ToLower().Contains("neck") || t.name.ToLower().Contains("cuello")))
                {
                    return t;
                }
            }

            // 4. Fallback to Player Render Root or Chest
            Transform renderRoot = player.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t != player.transform && t != transform && t.CompareTag("Render"));

            if (renderRoot != null)
            {
                return renderRoot;
            }

            return player.transform;
        }

        private Color GetColorForVisor(VisorType visor)
        {
            switch (visor)
            {
                case VisorType.VisorTermico: return new Color(1f, 0.35f, 0f, 0.85f);
                case VisorType.VisorEstadisticas: return new Color(0f, 0.9f, 1f, 0.85f);
                case VisorType.VisorObjetos: return new Color(1f, 0.85f, 0f, 0.85f);
                default: return Color.cyan;
            }
        }

        [ContextMenu("Re-Generate Mask Mesh")]
        public void GenerateMaskMesh()
        {
            Transform existing = transform.Find("MaskRender");
            GameObject maskMesh;

            if (existing != null)
            {
                maskMesh = existing.gameObject;
            }
            else
            {
                maskMesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
                maskMesh.name = "MaskRender";
                maskMesh.transform.SetParent(transform, false);

                var col = maskMesh.GetComponent<Collider>();
                if (col != null)
                {
                    if (Application.isPlaying) Destroy(col);
                    else DestroyImmediate(col);
                }
            }

            maskMesh.transform.localPosition = EffectiveHeadOffset;
            maskMesh.transform.localRotation = Quaternion.identity;
            maskMesh.transform.localScale = EffectiveHeadScale;

            var mr = maskMesh.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
                Material mat = new Material(shader);
                Color col = GetColorForVisor(EffectiveVisorType);
                mat.color = col;
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
                if (mat.HasProperty("_EmissionColor"))
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", col * 3.0f);
                }
                mr.sharedMaterial = mat;
            }
        }

        private void OnDestroy()
        {
            _isEquipped = false;
        }

        // --- VISOR HUD OVERLAY RENDERING (OnGUI) ---

        private void EnsureGUIAssets()
        {
            if (_stylesReady) return;

            Color visorCol = GetColorForVisor(EffectiveVisorType);

            _texOverlay = MakeTex(new Color(visorCol.r * 0.1f, visorCol.g * 0.1f, visorCol.b * 0.1f, 0.25f));
            _texFrame = MakeTex(new Color(visorCol.r, visorCol.g, visorCol.b, 0.7f));
            _texTargetBox = MakeTex(new Color(visorCol.r, visorCol.g, visorCol.b, 0.2f));

            _hudHeaderStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperLeft
            };
            _hudHeaderStyle.normal.textColor = visorCol;

            _hudValueStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.MiddleLeft
            };
            _hudValueStyle.normal.textColor = Color.white;

            _hudTargetStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            _hudTargetStyle.normal.textColor = visorCol;

            _stylesReady = true;
        }

        private Texture2D MakeTex(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        private void OnGUI()
        {
            if (!_isEquipped || Event.current.type != EventType.Repaint) return;

            if (_mainCamCache == null) _mainCamCache = Camera.main;
            if (_mainCamCache == null) return;

            EnsureGUIAssets();

            VisorType visor = EffectiveVisorType;

            // 1. Marco de Visor y Título HUD en Pantalla Completa
            DrawVisorScreenOverlay(visor);

            // 2. Renderizado de Objetivos según tipo de visor
            switch (visor)
            {
                case VisorType.VisorTermico:
                    DrawThermalOverlay();
                    break;
                case VisorType.VisorEstadisticas:
                    DrawStatsOverlay();
                    break;
                case VisorType.VisorObjetos:
                    DrawObjectsOverlay();
                    break;
            }
        }

        private void DrawVisorScreenOverlay(VisorType visor)
        {
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _texOverlay);

            int frameThickness = 3;
            int cornerSize = 40;

            GUI.DrawTexture(new Rect(20, 20, cornerSize, frameThickness), _texFrame);
            GUI.DrawTexture(new Rect(20, 20, frameThickness, cornerSize), _texFrame);

            GUI.DrawTexture(new Rect(Screen.width - 20 - cornerSize, 20, cornerSize, frameThickness), _texFrame);
            GUI.DrawTexture(new Rect(Screen.width - 20 - frameThickness, 20, frameThickness, cornerSize), _texFrame);

            GUI.DrawTexture(new Rect(20, Screen.height - 20 - frameThickness, cornerSize, frameThickness), _texFrame);
            GUI.DrawTexture(new Rect(20, Screen.height - 20 - cornerSize, frameThickness, cornerSize), _texFrame);

            GUI.DrawTexture(new Rect(Screen.width - 20 - cornerSize, Screen.height - 20 - frameThickness, cornerSize, frameThickness), _texFrame);
            GUI.DrawTexture(new Rect(Screen.width - 20 - frameThickness, Screen.height - 20 - cornerSize, frameThickness, cornerSize), _texFrame);

            string title = $"[ VISOR: {visor.ToString().ToUpper()} - SCANNER ACTIVO ]";
            GUI.Label(new Rect(30, 25, 400, 25), title, _hudHeaderStyle);
        }

        private void DrawThermalOverlay()
        {
            var healths = GameObject.FindObjectsOfType<HealthController>();
            foreach (var h in healths)
            {
                if (h == null || h.gameObject == _playerRoot || h.CurrentHP <= 0) continue;

                Vector3 worldPos = h.transform.position + Vector3.up * 1.0f;
                Vector3 screenPos = _mainCamCache.WorldToScreenPoint(worldPos);

                if (screenPos.z < 0) continue;

                float guiY = Screen.height - screenPos.y;
                float dist = Vector3.Distance(_mainCamCache.transform.position, h.transform.position);

                Rect box = new Rect(screenPos.x - 30, guiY - 30, 60, 60);
                GUI.DrawTexture(box, _texTargetBox);

                string heatLabel = $"[HEAT: {h.CurrentHP} HP]\n{dist:F1}m";
                GUI.Label(new Rect(screenPos.x - 60, guiY + 32, 120, 30), heatLabel, _hudTargetStyle);
            }
        }

        private void DrawStatsOverlay()
        {
            var healths = GameObject.FindObjectsOfType<HealthController>();
            foreach (var h in healths)
            {
                if (h == null || h.gameObject == _playerRoot || h.CurrentHP <= 0) continue;

                Vector3 worldPos = h.transform.position + Vector3.up * 1.2f;
                Vector3 screenPos = _mainCamCache.WorldToScreenPoint(worldPos);

                if (screenPos.z < 0) continue;

                float guiY = Screen.height - screenPos.y;
                float dist = Vector3.Distance(_mainCamCache.transform.position, h.transform.position);

                Rect box = new Rect(screenPos.x - 60, guiY - 35, 120, 70);
                GUI.DrawTexture(box, _texTargetBox);

                string statsLabel = $"{h.gameObject.name}\nHP: {h.CurrentHP}/{h.EffectiveMaxHealth}\nEQUIPO: {h.EffectiveTeam}\nDIST: {dist:F1}m";
                GUI.Label(box, statsLabel, _hudValueStyle);
            }
        }

        private void DrawObjectsOverlay()
        {
            var pickups = GameObject.FindObjectsOfType<PickupController>();
            foreach (var p in pickups)
            {
                if (p == null || !p.gameObject.activeInHierarchy) continue;

                Vector3 worldPos = p.transform.position + Vector3.up * 0.5f;
                Vector3 screenPos = _mainCamCache.WorldToScreenPoint(worldPos);

                if (screenPos.z < 0) continue;

                float guiY = Screen.height - screenPos.y;
                float dist = Vector3.Distance(_mainCamCache.transform.position, p.transform.position);

                string itemName = p.item != null ? p.item.itemName : p.gameObject.name.Replace("(Clone)", "");

                Rect box = new Rect(screenPos.x - 50, guiY - 20, 100, 40);
                GUI.DrawTexture(box, _texTargetBox);

                string itemLabel = $"📦 {itemName}\n{dist:F1}m";
                GUI.Label(box, itemLabel, _hudTargetStyle);
            }
        }
    }
}
