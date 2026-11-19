using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

using System.Linq;
using Runtime.Scripts.Items;
using Runtime.Scripts.Optional;
using Runtime.Scripts.Effective;
using Runtime.Scripts.Health;
using Runtime.Scripts.Pickup;
using Runtime.Scripts.Costume;
using Runtime.Scripts.Skills;
using Runtime.Scripts.Gate;

namespace Runtime.Scripts.Mask
{
    /// <summary>
    /// Controlador de Máscaras y Visores tácticos.
    /// Superpone el visor/máscara sobre la cabeza del personaje sin reemplazar su cuerpo base.
    /// Aplica efectos visuales HUD en cámara al ser usado desde el inventario.
    /// </summary>
    public class MaskController : NetworkBehaviour, IItemUseAction, IItemQuitAction, IItemDropAction
    {
        private const MaskKind DEFAULT_KIND = MaskKind.Range;
        private static readonly Vector3 DEFAULT_HEAD_BONE_OFFSET = new Vector3(0f, 0.05f, 0.08f);
        private static readonly Vector3 DEFAULT_ROOT_HEAD_OFFSET = new Vector3(0f, 1.40f, 0.15f);
        private static readonly Vector3 DEFAULT_HEAD_SCALE = new Vector3(0.35f, 0.35f, 0.35f);

        [Header("Settings de Máscara")]
        public string renderTag = "Render";
        public Key toggleKey = Key.Digit4; // activa / desactiva el visor
        [Range(5f, 150f)] public float visorRange = 30f; // alcance del visor (m)

        public MaskKind Kind;
        public Optional<MaskKind> kind;
        public Vector3 HeadOffset;
        public Optional<Vector3> headOffset;
        public Vector3 HeadScale;
        public Optional<Vector3> headScale;

        public MaskKind EffectiveKind => kind.Get(Kind, DEFAULT_KIND);
        public Vector3 EffectiveHeadOffset => headOffset.Get(HeadOffset, DEFAULT_HEAD_BONE_OFFSET);
        public Vector3 EffectiveHeadScale => headScale.Get(HeadScale, DEFAULT_HEAD_SCALE);

        private void UpdateInspectorValues()
        {
            Kind = EffectiveKind;
            HeadOffset = EffectiveHeadOffset;
            HeadScale = EffectiveHeadScale;
        }

        private void OnValidate()
        {
            UpdateInspectorValues();
        }

        private void Update()
        {
#if UNITY_EDITOR
            UpdateInspectorValues();
#endif
            if (!_isEquipped || MenuGate.Busy || Keyboard.current == null) return;
            if (Keyboard.current[toggleKey].wasPressedThisFrame) VisorOn = _visorOn = !_visorOn;
        }

        public static bool VisorOn; // lo lee el HUD para encender la placa del visor
        private bool _visorOn;
        private bool _isEquipped = false;
        private GameObject _playerRoot;
        private Camera _mainCamCache;

        private SkillProgression _sp;
        private HealthController[] _healths;
        private PickupController[] _pickups;
        private float _scanT;
        private GUIStyle _tagEnemy, _tagObj;
        private Texture2D _texTint, _texFrame;

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
            VisorOn = false;
            ClearMask();

            if (gameObject != null)
            {
                Destroy(gameObject);
            }
        }

        public void OnDropItem(GameObject player) => OnQuitItem(player);

        // Solo limpia si la mascara activa sigue siendo la de este objeto (evita que una mascara vieja borre a la nueva).
        private void ClearMask() { if (_sp != null && _sp.Active == EffectiveKind) _sp.SetMask(MaskKind.None); }

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

            // 6. Destruir componentes NetworkObject INMEDIATAMENTE ANTES de reemparentar para evitar que Netcode intercepte OnTransformParentChanged
            var netObjs = GetComponentsInChildren<Unity.Netcode.NetworkObject>(true);
            foreach (var no in netObjs)
            {
                if (Application.isPlaying) DestroyImmediate(no);
            }

            // 7. Adjuntar la máscara a la cabeza (o arriba del cuerpo si es el root)
            Vector3 finalOffset = (headTransform == _playerRoot.transform || headTransform == taggedRender)
                ? headOffset.GetValue(DEFAULT_ROOT_HEAD_OFFSET)
                : headOffset.GetValue(DEFAULT_HEAD_BONE_OFFSET);

            transform.SetParent(headTransform, false);
            transform.localPosition = finalOffset;
            transform.localRotation = Quaternion.identity;
            transform.localScale = EffectiveHeadScale;

            gameObject.SetActive(true);
            _isEquipped = true;

            _sp = _playerRoot.GetComponent<SkillProgression>() ?? _playerRoot.GetComponentInChildren<SkillProgression>(true);
            if (_sp != null) _sp.SetMask(EffectiveKind);

            // 8. Limpiar componentes de mundo
            if (TryGetComponent<PickupController>(out var p)) Destroy(p);
            if (TryGetComponent<Rigidbody>(out var rb)) Destroy(rb);
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

        // Mismos colores que la progresion del panel: cian = Rango, magenta = Melee
        private Color GetMaskColor() => EffectiveKind == MaskKind.Melee ? new Color(1f, 0.25f, 0.8f, 0.85f) : new Color(0.1f, 0.85f, 1f, 0.85f);

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
                Color col = GetMaskColor();
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

        public override void OnDestroy()
        {
            if (_isEquipped) ClearMask();
            if (_isEquipped) VisorOn = false;
            _isEquipped = false;
            base.OnDestroy();
        }

        // --- VISOR (mascara Rango, nivel 2): carteles con solo el nombre de enemigos y objetos cercanos ---

        private static GUIStyle Style(Color c)
        {
            var s = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            s.normal.textColor = c;
            return s;
        }

        private void OnGUI()
        {
            // Apagado, o con cualquier menu (TAB / pausa) abierto: no se dibuja nada encima de la UI.
            if (!_isEquipped || !_visorOn || MenuGate.Busy || Cursor.visible || Event.current.type != EventType.Repaint) return;
            if (_sp == null || !_sp.Has(Skill.Visor)) return;

            if (_mainCamCache == null) _mainCamCache = Camera.main;
            if (_mainCamCache == null) return;

            if (_texTint == null)
            {
                _texTint = Solid(new Color(0.1f, 0.035f, 0f, 0.25f));
                _texFrame = Solid(new Color(1f, 0.35f, 0f, 0.7f));
            }
            DrawFrame();

            if (_tagEnemy == null) { _tagEnemy = Style(new Color(1f, 0.35f, 0f)); _tagObj = Style(new Color(1f, 0.85f, 0f)); }

            if (Time.unscaledTime >= _scanT)
            {
                _scanT = Time.unscaledTime + 0.25f;
                _healths = Object.FindObjectsByType<HealthController>(FindObjectsSortMode.None);
                _pickups = Object.FindObjectsByType<PickupController>(FindObjectsSortMode.None);
            }

            Vector3 center = _playerRoot != null ? _playerRoot.transform.position : transform.position;

            if (_healths != null)
                foreach (var h in _healths)
                {
                    if (h != null && h.EffectiveCurrentHealth > 0 && !h.CompareTag("Player") && !(_playerRoot != null && h.transform.IsChildOf(_playerRoot.transform)))
                        Tag(center, h.transform.position + Vector3.up * 1.2f, h.name, _tagEnemy);
                }

            if (_pickups != null)
                foreach (var p in _pickups)
                {
                    if (p != null && p.gameObject.activeInHierarchy)
                        Tag(center, p.transform.position + Vector3.up * 0.5f, p.item != null ? p.item.itemName : p.name, _tagObj);
                }
        }

        private static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        // Tinte suave + cuatro esquinas: indica que el visor esta abierto.
        private void DrawFrame()
        {
            const float t = 3f, c = 40f, m = 20f;
            float w = Screen.width, h = Screen.height;
            GUI.DrawTexture(new Rect(0, 0, w, h), _texTint);
            foreach (float x in new[] { m, w - m - c })
                foreach (float y in new[] { m, h - m - t })
                    GUI.DrawTexture(new Rect(x, y, c, t), _texFrame);
            foreach (float x in new[] { m, w - m - t })
                foreach (float y in new[] { m, h - m - c })
                    GUI.DrawTexture(new Rect(x, y, t, c), _texFrame);
        }

        // Cartel minimalista: solo el nombre, y solo si esta cerca del player.
        private void Tag(Vector3 center, Vector3 worldPos, string label, GUIStyle style)
        {
            if ((worldPos - center).sqrMagnitude > visorRange * visorRange) return;
            Vector3 sp = _mainCamCache.WorldToScreenPoint(worldPos);
            if (sp.z < 0f) return;
            GUI.Label(new Rect(sp.x - 60f, Screen.height - sp.y - 10f, 120f, 20f), label.Replace("(Clone)", "").Trim(), style);
        }
    }
}