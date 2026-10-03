using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Crafting.Scripts;
using Dialogs.Scripts;
using Missions.Scripts;
using Trades.Data;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using Missions.Data;
#endif

namespace Narrative.Scripts
{
    /// <summary>
    /// Instancia única ejecutora de misiones y narrativa según sus ítems y requisitos.
    /// Funciona articulado con MissionController de forma completamente segura.
    /// </summary>
    [AddComponentMenu("Narrative/Narrative Manager")]
    public class NarrativeManager : MonoBehaviour
    {
        public static NarrativeManager Instance { get; private set; }

        [Serializable]
        public class Site
        {
            public Transform at;
            [Tooltip("Puerta (abre su paso) o punto de interés (da pistas).")]
            public bool door;
            [Tooltip("Puerta: 2,4,6,8,10,12,14,16. Punto: paso del lugar (donde está una llave) o -1.")]
            public int step = -1;
            public float radius = 3f;
            public float cooldown = 25f;
            [Tooltip("Solo puertas. Se invoca en todos los clientes al abrirse (también al entrar tarde).")]
            public UnityEvent onUnlocked;
            [NonSerialized] public bool inside, unlocked, fired;
            [NonSerialized] public float next;
        }

        public List<Site> sites = new List<Site>();
        [Min(1)] public int visitsPerLevel = 2;
        public UnityEvent onEnd;

        internal static readonly string[] Id =
        {
            "s00_log", "s01_k1", "s02_d1", "s03_k2", "s04_d2", "s05_k3", "s06_d3", "s07_k4", "s08_d4",
            "s09_k5", "s10_d5", "s11_k6", "s12_d6", "s13_k7", "s14_d7", "s15_k8", "s16_d8", "s17_end"
        };

        static readonly string[] Done =
        {
            "Sistemas mínimos operativos. Sigo la firma de datos más cercana.",
            "Chip de mapa integrado. La ruta marca una cerradura sellada: la puerta 1.",
            "Puerta 1 abierta. Dentro hay una máscara de acceso.",
            "Máscara acoplada. El mapa marca otra cerradura: la puerta 2.",
            "Puerta 2 abierta. Una consola guarda un chip de memoria.",
            "Fragmentos recuperados: 'restituir la especie'. Una tercera cerradura espera: la puerta 3.",
            "Puerta 3 abierta. El complejo se bifurca en más sectores.",
            "Segundo chip: fui construido para restituir a la humanidad tras el cataclismo atómico. La puerta 4 debe abrirse.",
            "Puerta 4 abierta. Hay más equipo de acceso guardado.",
            "Segunda máscara asegurada. El sector de embriones es la puerta 5.",
            "Puerta 5 abierta. Una cámara criogénica sigue activa.",
            "Embrión asegurado. Debo llevarlo al laboratorio: la puerta 6.",
            "Laboratorio operativo. Sus sectores internos exigen una llave propia.",
            "Llave del laboratorio obtenida. La puerta 7 responde a ella.",
            "Puerta 7 abierta. Detecto un depósito de combustible.",
            "Combustible cargado. La nave espera tras la puerta 8.",
            "", ""
        };

        static readonly string[][] H =
        {
            null,
            new[] { "Detecto una firma de datos cercana. Algo aquí almacena información cartográfica.", "Sin mapa no hay ruta. El chip de navegación no está tras ninguna puerta.", "Objetivo: recoger el chip de mapa. Está en el camino abierto, sin cerradura." },
            new[] { "El mapa marca una cerradura sellada con un '1' grabado.", "El chip de mapa encaja con la puerta 1. Las demás siguen bloqueadas.", "Objetivo: usar el chip de mapa en la puerta 1." },
            new[] { "El interior de la puerta 1 huele a metal y polvo: algo se guardó ahí.", "Los registros hablan de una máscara de acceso resguardada tras la puerta 1.", "Objetivo: recoger la máscara dentro de la zona de la puerta 1." },
            new[] { "La máscara tiene un '2' inscripto en el borde interno.", "La máscara es una credencial. La puerta 2 la reconoce.", "Objetivo: usar la máscara en la puerta 2." },
            new[] { "Un destello en la memoria corrupta: hay datos esperando al otro lado de la puerta 2.", "Un chip de memoria guarda fragmentos de mi pasado, tras la puerta 2.", "Objetivo: recoger el chip de memoria dentro de la zona de la puerta 2." },
            new[] { "Los fragmentos recuperados apuntan a una tercera cerradura.", "El chip de memoria contiene el código de la puerta 3.", "Objetivo: usar el chip de memoria en la puerta 3." },
            new[] { "La puerta 3 dejó abierto un tramo nuevo. Hay señales más adentro.", "Más allá de la puerta 3 hay otro chip de memoria.", "Objetivo: recoger el segundo chip de memoria tras la puerta 3." },
            new[] { "El segundo chip vibra: reconoce un cerrojo cercano marcado con un '4'.", "El segundo chip de memoria abre la puerta 4.", "Objetivo: usar el segundo chip en la puerta 4." },
            new[] { "Tras la puerta 4 hay más equipo de acceso.", "Otra máscara aguarda dentro de la zona de la puerta 4.", "Objetivo: recoger la segunda máscara tras la puerta 4." },
            new[] { "Esta máscara lleva grabado un '5'. Eso es una cerradura.", "La segunda máscara es la credencial de la puerta 5.", "Objetivo: usar la segunda máscara en la puerta 5." },
            new[] { "Sensores térmicos: hay algo vivo, o casi, tras la puerta 5.", "Un embrión en criopreservación resiste tras la puerta 5. Es mi razón de existir.", "Objetivo: recoger el embrión de la zona de la puerta 5." },
            new[] { "El embrión necesita incubación. El laboratorio tiene una puerta sellada, la 6.", "Llevar el embrión al laboratorio: la puerta 6.", "Objetivo: usar el embrión en la puerta 6 del laboratorio." },
            new[] { "El laboratorio guarda una llave propia.", "Dentro del laboratorio hay una llave que abre sus sectores internos.", "Objetivo: recoger la llave del laboratorio." },
            new[] { "Hay una puerta interna más, marcada con un '7'.", "La llave del laboratorio corresponde a la puerta 7.", "Objetivo: usar la llave del laboratorio en la puerta 7." },
            new[] { "Un olor a combustible viene desde dentro de la puerta 7.", "Para huir en la nave hace falta combustible, y está tras la puerta 7.", "Objetivo: recoger el combustible dentro de la zona de la puerta 7." },
            new[] { "La nave está lista, pero sin combustible no despega. Ahora sí.", "La puerta 8 lleva a la nave. Tengo lo que necesito.", "Objetivo: usar el combustible y abrir la puerta 8 hacia la nave." },
        };

        static readonly string[] Intro =
        {
            "> circuitos activados........ OK",
            "> integridad estructural: 41 %",
            "> ERROR: falla de memoria, sectores corruptos",
            "> sistema de emergencia iniciado",
            "> ERROR: mapa no encontrado",
            "> buscando módulo de navegación...",
            "> señal de datos detectada en las inmediaciones",
            "> restableciendo locomoción",
        };

        static readonly string[] Final =
        {
            "> ALERTA: falla de contención en incubadora",
            "> cascada de fallos en cámaras de gestación",
            "> embriones viables: 12... 7... 3... 1",
            "> protocolo de evacuación: despegue inmediato",
            "> cargando embrión superviviente",
            "> despegue confirmado",
            "> análisis genético del embrión... completo",
            "> coincidencia con genoma humano: 97,3 %",
            "> el 2,7 % restante no figura en ningún registro",
            "> reclasificando propósito...",
        };

        static readonly FieldInfo DlgField = typeof(DialogManager).GetField("dialogs", BindingFlags.NonPublic | BindingFlags.Instance);

        private readonly HashSet<string> _localNarrativeCompleted = new HashSet<string>();

        private void Awake()
        {
            if (Instance == null) Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        static void Show(string[] lines)
        {
            var list = DlgField?.GetValue(DialogManager.Instance) as List<DialogEntry>;
            if (list == null) { Debug.LogError("[Narrative] DialogManager.dialogs no encontrado."); return; }
            list.RemoveAll(d => d.id == "_narr");
            list.Add(new DialogEntry { id = "_narr", speaker = "SISTEMA", lines = lines });
            DialogManager.Instance.ShowDialog("_narr");
        }

        const string Blocked = "Sellada. Nada de lo que llevo encima la abre todavía.";

        int _last = -1;
        bool _ready, _logging, _finalDone, _blocked, _wasOpen;
        float _t0 = -1f, _poll;
        readonly Dictionary<int, int> _visits = new Dictionary<int, int>();
        readonly Dictionary<int, int> _lastVar = new Dictionary<int, int>();
        readonly Queue<string[]> _q = new Queue<string[]>();

        /// <summary>
        /// Comprueba si se cumplen todos los requisitos (ítems, recetas y prerrequisitos) de un MissionData para un jugador.
        /// Evalúa la bolsa del jugador comparando por referencia de asset, Hash de ítem y nombre de ítem.
        /// </summary>
        public bool CheckRequirements(Missions.Data.MissionData mData, InventoryController playerInv)
        {
            if (mData == null) return false;

            // 1. Prerrequisitos de misión
            if (mData.missionRequirements != null && mData.missionRequirements.Count > 0)
            {
                foreach (var req in mData.missionRequirements)
                {
                    if (req != null && !IsMissionCompleted(req))
                        return false;
                }
            }

            var bag = playerInv != null ? playerInv.GetMyBag() : InventoryController.GetBag();

            // 2. Requisitos de recolección en inventario (ItemData)
            if (mData.inventoryRequirements != null && mData.inventoryRequirements.Count > 0)
            {
                foreach (var req in mData.inventoryRequirements)
                {
                    if (req.item == null) continue;
                    int requiredQty = req.amount > 0 ? req.amount : 1;
                    int playerQty = MissionController.GetItemQuantityInBag(bag, req.item);
                    if (playerQty < requiredQty)
                    {
                        return false;
                    }
                }
            }

            // 3. Requisitos de crafteo/receta (TradeData)
            if (mData.craftingRequirements != null && mData.craftingRequirements.Count > 0)
            {
                foreach (var trade in mData.craftingRequirements)
                {
                    if (trade == null) continue;

                    // 1. Verificar si el crafteo/trade fue ejecutado
                    bool tradeDone = CraftingController.IsTradeCompletedAnywhere(trade);
                    if (!tradeDone)
                        return false;

                    // 2. Si produce productos de salida, verificar que el jugador posea las cantidades requeridas
                    if (trade.outputs != null && trade.outputs.Count > 0)
                    {
                        foreach (var outReq in trade.outputs)
                        {
                            if (outReq == null || outReq.item == null) continue;
                            int reqQty = outReq.GetMinRequiredAmount();
                            if (reqQty <= 0) continue;

                            int playerQty = MissionController.GetItemQuantityInBag(bag, outReq.item);
                            if (playerQty < reqQty)
                                return false;
                        }
                    }
                }
            }

            return true;
        }

        public void ExecuteMission(Missions.Data.MissionData mData, InventoryController playerInv)
        {
            if (mData == null) return;

            string id = !string.IsNullOrEmpty(mData.title) ? mData.title : mData.name;
            CompleteMission(id);
        }

        public bool IsMissionCompleted(Missions.Data.MissionData mData)
        {
            if (mData == null) return false;
            string id = !string.IsNullOrEmpty(mData.title) ? mData.title : mData.name;
            return IsMissionCompleted(id);
        }

        public bool IsMissionCompleted(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            if (MissionController.Instance != null)
                return MissionController.Instance.IsMissionCompleted(id);
            return _localNarrativeCompleted.Contains(id);
        }

        public void CompleteMission(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            _localNarrativeCompleted.Add(id);

            if (MissionController.Instance != null)
                MissionController.Instance.CompleteMission(id);
        }

        void Update()
        {
            Block();
            if (!_ready) { Boot(); return; }
            if (!_logging && _q.Count > 0 && !DialogManager.Instance.IsOpen)
                Show(_q.Dequeue());
            Interact();

            if (Time.unscaledTime < _poll) return;
            _poll = Time.unscaledTime + 0.2f;

            int w = Watermark();
            if (w > _last)
            {
                int from = _last + 1;
                _last = w;
                foreach (var s in sites) s.fired = false;
                for (int i = from; i <= w; i++) Completed(i);
            }
            Keys();
            Visits();
        }

        // Cada misión se identifica por su título (MissionController). Se busca por nombre de asset: Mission_<Id>.
        static Missions.Data.MissionData Mis(int i) =>
            MissionController.Instance.missions.Find(m => m != null && m.name == "Mission_" + Id[i]);

        static string Key(int i)
        {
            var m = Mis(i);
            return m != null ? MissionController.Instance.GetMissionIdentifier(m) : Id[i];
        }

        // Llaves (pasos impares): se completan solas cuando el jugador local tiene el ítem.
        void Keys()
        {
            int t = _last + 1;
            if (_logging || t % 2 == 0 || t >= Id.Length - 1) return;
            var m = Mis(t);
            if (m != null && CheckRequirements(m, InventoryController.LocalInstance)) CompleteMission(Key(t));
        }

        // E abre la puerta que toca si el jugador está en su radio. Se ignora la E que cierra un diálogo.
        void Interact()
        {
            bool open = DialogManager.Instance.IsOpen;
            bool ok = !open && !_wasOpen && !_logging;
            _wasOpen = open;
            var inv = InventoryController.LocalInstance;
            if (!ok || inv == null || Keyboard.current == null || !Keyboard.current.eKey.wasPressedThisFrame) return;
            int t = _last + 1;
            if (t <= 0 || t % 2 != 0 || t >= Id.Length - 1) return;
            Vector3 p = inv.transform.position;
            foreach (var s in sites)
                if (s.door && s.step == t && s.at != null && (s.at.position - p).sqrMagnitude <= s.radius * s.radius)
                {
                    CompleteMission(Key(t));
                    return;
                }
        }

        void Boot()
        {
            var mm = MissionController.Instance;
            if (mm == null || DialogManager.Instance == null || InventoryController.LocalInstance == null) return;
            if (_t0 < 0f) _t0 = Time.time;
            float waited = Time.time - _t0;
            var nm = NetworkManager.Singleton;
            bool netPending = nm != null && nm.IsListening && !mm.IsSpawned;
            if (waited < 0.75f || (netPending && waited < 5f)) return;

            _ready = true;
            _last = Watermark();
            foreach (var s in sites) if (s.door && s.step <= _last) Unlock(s);
            if (_last < 0)
            {
                StartCoroutine(Log(Intro, () => CompleteMission(Key(0)), 0.02f, 0.35f, 1.5f));
            }
            else if (_last == 16) Finale();
        }

        int Watermark()
        {
            int w = -1;
            for (int i = 0; i < Id.Length; i++)
            {
                if (IsMissionCompleted(Key(i))) w = i;
                else break;
            }
            return w;
        }

        void Completed(int i)
        {
            foreach (var s in sites) if (s.door && s.step == i) Unlock(s);
            if (i == 16) { Finale(); return; }
            var l = new List<string>();
            if (Done[i] != "") l.Add(Done[i]);
            if (i == 5) l.Add("FASE II: la memoria vuelve, pero con lagunas. Debo reunir lo que falta.");
            if (i == 15) l.Add("FASE III: tengo lo necesario. Es hora de huir.");
            if (l.Count > 0) _q.Enqueue(l.ToArray());
        }

        static void Unlock(Site s)
        {
            if (s.unlocked) return;
            s.unlocked = true;
            s.onUnlocked?.Invoke();
        }

        void Finale()
        {
            if (_finalDone) return;
            _finalDone = true;
            StartCoroutine(Log(Final, () => { CompleteMission(Key(17)); onEnd?.Invoke(); }, 0.03f, 0.6f, 3f));
        }

        void Visits()
        {
            var inv = InventoryController.LocalInstance;
            if (inv == null || _logging) return;
            Vector3 p = inv.transform.position;
            foreach (var s in sites)
            {
                if (s.at == null) continue;
                bool now = (s.at.position - p).sqrMagnitude <= s.radius * s.radius;
                if (now && !s.inside) Visit(s);
                s.inside = now;
            }
        }

        void Visit(Site s)
        {
            int t = _last + 1;
            if (t <= 0 || t >= Id.Length - 1 || Time.time < s.next) return;

            if (s.door)
            {
                if (s.step < t) return;
                s.next = Time.time + s.cooldown;
                _q.Enqueue(s.step == t ? new[] { "Cerradura compatible. Pulsa [E] para abrir." } : new[] { Blocked, Hint(t) });
                return;
            }

            if (s.step == t || s.fired) return;
            s.fired = true;
            s.next = Time.time + s.cooldown;
            _q.Enqueue(new[] { Hint(t) });
        }

        string Hint(int t)
        {
            _visits.TryGetValue(t, out int v);
            _visits[t] = v + 1;
            int lv = Mathf.Min(2, v / visitsPerLevel);
            var pool = H[t][lv].Split('|');
            int key = t * 3 + lv;
            int prev = _lastVar.TryGetValue(key, out int pv) ? pv : -1;
            int k = UnityEngine.Random.Range(0, pool.Length);
            if (pool.Length > 1 && k == prev) k = (k + 1) % pool.Length;
            _lastVar[key] = k;
            return pool[k];
        }

        void Block()
        {
            var inv = InventoryController.LocalInstance;
            if (inv == null) return;
            bool want = _logging || (DialogManager.Instance != null && DialogManager.Instance.IsOpen);
            if (want == _blocked) return;
            _blocked = want;
            var pi = inv.GetComponent<PlayerInput>();
            if (pi == null || !pi.enabled) return;
            if (want) pi.DeactivateInput(); else pi.ActivateInput();
        }

        static bool Fast() =>
            Keyboard.current != null && (Keyboard.current.eKey.isPressed || Keyboard.current.spaceKey.isPressed);

        static void Fill(GameObject g, float m)
        {
            var r = g.GetComponent<RectTransform>();
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(m, m);
            r.offsetMax = new Vector2(-m, -m);
        }

        IEnumerator Log(string[] lines, Action done, float charDelay, float lineDelay, float hold)
        {
            _logging = true;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            var go = new GameObject("NarrativeLog", typeof(Canvas));
            go.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            go.GetComponent<Canvas>().sortingOrder = 500;
            var bg = new GameObject("bg", typeof(Image));
            bg.transform.SetParent(go.transform, false);
            bg.GetComponent<Image>().color = Color.black;
            Fill(bg, 0);
            var tx = new GameObject("tx", typeof(TextMeshProUGUI));
            tx.transform.SetParent(go.transform, false);
            var t = tx.GetComponent<TextMeshProUGUI>();
            t.fontSize = 26;
            t.color = new Color(0.25f, 1f, 0.4f);
            t.alignment = TextAlignmentOptions.TopLeft;
            Fill(tx, 60);

            var sb = new StringBuilder();
            foreach (var line in lines)
            {
                foreach (char c in line)
                {
                    sb.Append(c);
                    t.text = sb.ToString() + "_";
                    yield return new WaitForSecondsRealtime(Fast() ? 0.001f : charDelay);
                }
                sb.Append('\n');
                t.text = sb.ToString();
                yield return new WaitForSecondsRealtime(Fast() ? 0.02f : lineDelay);
            }
            yield return new WaitForSecondsRealtime(hold);

            Destroy(go);
            _logging = false;
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
            done?.Invoke();
        }
    }

#if UNITY_EDITOR
    /// <summary>Menú Narrative > Setup: crea ítems y misiones y las asigna a MissionController.</summary>
    public static class NarrativeSetup
    {
        static readonly string[] Items =
            { "Chip Mapa", "Mascara I", "Chip Memoria I", "Chip Memoria II", "Mascara II", "Embrion", "Llave Lab", "Combustible" };

        static readonly string[] Titles =
        {
            "Reactivación", "Firma de datos", "Cerradura 1", "Credencial", "Cerradura 2", "Memoria", "Cerradura 3",
            "Origen", "Cerradura 4", "Segunda credencial", "Cerradura 5", "Semilla", "Laboratorio", "Acceso interno",
            "Cerradura 7", "Energía", "Nave", "Huida"
        };

        static readonly string[] Descs =
        {
            "Sistemas en reinicio.", "Algo cercano emite información.", "Una puerta sellada espera.",
            "Hay equipo de acceso guardado.", "Otra puerta sellada.", "Fragmentos de datos aguardan.",
            "El complejo sigue cerrado.", "Más memoria, más adentro.", "Un cerrojo reconoce lo recuperado.",
            "Hay más equipo de acceso.", "Una cámara sellada.", "Algo sigue con vida.", "El laboratorio espera.",
            "El laboratorio guarda una llave.", "Un último sello interno.", "Se necesita combustible.",
            "La nave espera.", "Despegar."
        };

        static void Dir(string path)
        {
            string[] p = path.Split('/');
            string cur = p[0];
            for (int i = 1; i < p.Length; i++)
            {
                if (!AssetDatabase.IsValidFolder(cur + "/" + p[i])) AssetDatabase.CreateFolder(cur, p[i]);
                cur += "/" + p[i];
            }
        }

        static T Asset<T>(string path) where T : ScriptableObject
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a == null)
            {
                a = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(a, path);
            }
            return a;
        }

        [MenuItem("Narrative/Setup")]
        static void Run()
        {
            var mm = UnityEngine.Object.FindFirstObjectByType<MissionController>();
            if (mm == null)
            {
                EditorUtility.DisplayDialog("Narrative", "No hay un MissionController en la escena abierta.", "OK");
                return;
            }
            if (!EditorUtility.DisplayDialog("Narrative",
                    "Se crearán 8 ítems y 18 misiones y se REEMPLAZARÁ MissionController.missions. ¿Continuar?",
                    "Continuar", "Cancelar")) return;

            Dir("Assets/Resources/Narrative/Items");
            Dir("Assets/Missions/Data");

            var items = new ItemData[Items.Length];
            for (int i = 0; i < Items.Length; i++)
            {
                var it = Asset<ItemData>("Assets/Resources/Narrative/Items/Item_" + Items[i].Replace(" ", "") + ".asset");
                it.itemName = Items[i];
                it.isPickable = true;
                it.isDropable = false;
                it.isUsable = false;
                it.isQuitable = false;
                EditorUtility.SetDirty(it);
                items[i] = it;
            }

            var so = new SerializedObject(mm);
            var list = so.FindProperty("missions");
            list.arraySize = NarrativeManager.Id.Length;
            for (int i = 0; i < NarrativeManager.Id.Length; i++)
            {
                var m = Asset<Missions.Data.MissionData>("Assets/Missions/Data/Mission_" + NarrativeManager.Id[i] + ".asset");
                m.title = Titles[i];
                m.description = Descs[i];
                m.inventoryRequirements = new List<ItemRequirement>();
                if (i % 2 == 1 && i < 17)
                    m.inventoryRequirements.Add(new ItemRequirement(items[i / 2], 1));
                EditorUtility.SetDirty(m);
                list.GetArrayElementAtIndex(i).objectReferenceValue = m;
            }
            so.ApplyModifiedProperties();

            if (mm.GetComponent<NarrativeManager>() == null) Undo.AddComponent<NarrativeManager>(mm.gameObject);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(mm.gameObject.scene);
            Debug.Log("[Narrative] Setup completo: 8 ítems, 18 misiones asignadas y NarrativeManager añadido.");
        }
    }
#endif
}