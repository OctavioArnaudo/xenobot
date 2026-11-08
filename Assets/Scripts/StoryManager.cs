using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Runtime.Scripts.Crafting;
using Runtime.Scripts.Inventory;
using Runtime.Scripts.Items;
using Runtime.Scripts.Missions;

namespace Runtime.Scripts.Narrative
{
    /// <summary>
    /// Historia + diálogos + tutoriales en un solo componente.
    /// Paso entero: 2i = falta la llave i, 2i+1 = falta abrir su terminal. Con red es una NetworkVariable.
    /// Todo se detecta por distancia al jugador local (sin colliders ni Find). UI creada una vez y reutilizada.
    /// </summary>
    public class StoryManager : NetworkBehaviour
    {
        [Serializable]
        public class Pair
        {
            [Tooltip("Objeto de la llave. Se recoge al acercarse y se oculta.")] public GameObject key;
            [Tooltip("Punto de la terminal donde se pulsa E.")] public Transform terminal;
            [Tooltip("Texto al recoger la llave. Vacío = por defecto.")][TextArea] public string keyText;
            [Tooltip("Texto al abrir la terminal. Vacío = por defecto.")][TextArea] public string openText;
            [Tooltip("Objetivo mostrado (J) mientras falta la llave. Vacío = por defecto.")] public string keyGoal;
            [Tooltip("Objetivo mostrado (J) mientras falta abrir la terminal. Vacío = por defecto.")] public string openGoal;
            [Tooltip("Al abrirse esta terminal, en todos los clientes (también al entrar tarde).")] public UnityEvent onOpen;
        }

        [Serializable]
        public class Dialog
        {
            public string id;
            public string speaker;
            [TextArea(2, 5)] public string[] lines;
            [Tooltip("Zona de activación. Vacío = solo se lanza con Say(id).")] public Transform at;
            public float radius = 3f;
            [Tooltip("Se abre al entrar en la zona. Si no, hay que pulsar E.")] public bool auto = true;
            public bool once = true;
            [NonSerialized] public bool used, inside;
        }

        [Serializable]
        public class Tutorial
        {
            public MissionData mission;
            [Tooltip("Aparece al llegar a este paso de la historia.")] public int fromStep;
            [Tooltip("Solo para misiones sin ítems ni recetas: se completan al llegar aquí.")] public Transform at;
            public float radius = 3f;
            [NonSerialized] public bool on, done;
        }

        public List<Pair> pairs = new List<Pair>();
        public List<Dialog> dialogs = new List<Dialog>();
        public List<Tutorial> tutorials = new List<Tutorial>();
        public float keyRadius = 1.5f;
        public float terminalRadius = 3f;
        [Tooltip("Al abrirse cualquier terminal, con su número (1, 2, 3...).")] public UnityEvent<int> onAnyOpen;
        public UnityEvent onEnd;
        public bool debug;

        public static StoryManager Instance { get; private set; }
        public bool IsDialogOpen => _dlgOpen;

        const string Sys = "SISTEMA", PTerm = "[E] Acceder a la terminal", PTalk = "[E] Hablar";

        static readonly string[] Texts =
        {
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
            ""
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

        static readonly string[] Denied = { "Acceso denegado. Falta algo por recuperar." };

        static readonly Color Green = new Color(0.25f, 1f, 0.4f);
        static readonly Color Dark = new Color(0.02f, 0.16f, 0.06f, 0.96f);

        NetworkVariable<int> _net = new NetworkVariable<int>();
        int _local, _seen, _i;
        bool[] _had;
        bool _ready, _logging, _blocked, _fin, _dlgOpen, _goalOn = true;
        float _t0 = -1f, _poll, _tut;
        string _want, _who;
        string[] _lines;
        Dialog _talk;
        readonly Queue<(string who, string[] lines)> _q = new Queue<(string, string[])>();
        readonly List<Tutorial> _active = new List<Tutorial>();
        readonly StringBuilder _sb = new StringBuilder(256);
        GameObject _logBox, _dlg, _goal;
        TextMeshProUGUI _txt, _dlgTxt, _goalTxt, _counter, _prompt;

        int Step => IsSpawned ? _net.Value : _local;
        int Last => pairs.Count * 2;

        void Awake() => Instance = this;

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        void Start() => Build();

        void Update()
        {
            Block();
            if (!_ready) { Boot(); return; }

            var kb = Keyboard.current;
            bool e = kb != null && kb.eKey.wasPressedThisFrame;
            bool busy = _dlgOpen || _logging; // la E que cierra un diálogo no abre nada

            if (_dlgOpen && kb != null && (e || kb.spaceKey.wasPressedThisFrame)) Next();
            if (!_logging && !_dlgOpen && _q.Count > 0) { var d = _q.Dequeue(); Open(d.who, d.lines); }
            if (kb != null && kb.jKey.wasPressedThisFrame) { _goalOn = !_goalOn; Goal(); }

            var inv = InventoryController.LocalInstance;
            if (e && !busy && !_dlgOpen && !_logging && inv != null) Interact(inv.transform.position);

            float now = Time.unscaledTime;
            if (now >= _poll)
            {
                _poll = now + 0.1f;
                Sync();
                if (inv != null) Poll(inv);
            }
            if (inv != null && now >= _tut)
            {
                _tut = now + 0.25f;
                Tutorials(inv);
            }
        }

        // Distancia a la superficie del objeto (collider o modelo), no a su pivote: el radio es "metros desde el borde".
        static bool Near(Transform t, Vector3 p, float r)
        {
            if (t == null) return false;
            return Bnd(t, out var b) ? b.SqrDistance(p) <= r * r : (t.position - p).sqrMagnitude <= r * r;
        }

        static bool Bnd(Transform t, out Bounds b)
        {
            var c = t.GetComponentInChildren<Collider>();
            if (c != null && c.enabled) { b = c.bounds; return true; }
            var m = t.GetComponentInChildren<Renderer>();
            if (m != null) { b = m.bounds; return true; }
            b = default;
            return false;
        }

        // ---------- Historia ----------

        void Boot()
        {
            if (InventoryController.LocalInstance == null) return;
            if (_t0 < 0f) _t0 = Time.time;
            float waited = Time.time - _t0;
            var nm = NetworkManager.Singleton;
            bool netPending = nm != null && nm.IsListening && !IsSpawned;
            if (waited < 0.75f || (netPending && waited < 5f)) return; // tope 5 s: si no spawnea, seguimos en local

            _ready = true;
            _had = new bool[pairs.Count];
            for (int i = 0; i < _had.Length; i++) _had[i] = pairs[i].key != null;
            _seen = Step;
            if (debug) Debug.Log($"[Story] Listo. Paso {_seen} de {Last}.");
            for (int i = 1; i <= _seen; i++) Apply(i, false);
            Counter();
            if (Last > 0 && _seen >= Last) Finale();
            else if (_seen == 0)
                StartCoroutine(Log(Intro, () => Enq("Sistemas mínimos operativos. Sigo la firma de datos más cercana."), 0.02f, 0.35f, 1.5f));
            Goal();
        }

        void Sync()
        {
            int s = Step;
            if (s <= _seen) return;
            int from = _seen + 1;
            _seen = s;
            for (int i = from; i <= s; i++) Apply(i, true);
            Counter();
            Goal();
        }

        // announce = false al entrar tarde: solo estado, sin textos.
        void Apply(int s, bool announce)
        {
            var p = pairs[(s - 1) / 2];
            bool key = s % 2 == 1;
            if (key) { if (p.key != null) p.key.SetActive(false); }
            else { p.onOpen?.Invoke(); onAnyOpen?.Invoke(s / 2); }
            if (!announce) return;
            if (s >= Last) { Finale(); return; }

            string t = key ? p.keyText : p.openText;
            if (string.IsNullOrEmpty(t)) t = s - 1 < Texts.Length ? Texts[s - 1] : "";
            if (t.Length > 0) Enq(t);
            if (s == 5) Enq("FASE II: la memoria vuelve, pero con lagunas. Debo reunir lo que falta.");
            if (s == 15) Enq("FASE III: tengo lo necesario. Es hora de huir.");
        }

        void Counter() => _counter.text = $"LLAVES  {(_seen + 1) / 2}/{pairs.Count}";

        void Finale()
        {
            if (_fin) return;
            _fin = true;
            StartCoroutine(Log(Final, () => onEnd?.Invoke(), 0.03f, 0.6f, 3f));
        }

        // Cualquier cliente puede avanzar; el servidor solo acepta si el paso sigue siendo el mismo.
        void Advance(int from)
        {
            if (IsSpawned) AdvanceRpc(from);
            else if (_local == from) _local++;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void AdvanceRpc(int from)
        {
            if (_net.Value == from) _net.Value++;
        }

        // 10 Hz: recoger llave, zonas de diálogo y aviso de E.
        void Poll(InventoryController inv)
        {
            Vector3 p = inv.transform.position;
            int s = Step;
            bool term = false;
            if (!_logging)
            {
                int k = s / 2;
                if (s % 2 == 0 && k < pairs.Count && Taken(k, p)) Advance(s);
                term = s % 2 == 1 && s < Last && Near(pairs[(s - 1) / 2].terminal, p, terminalRadius);
                Talks(p);
            }
            Prompt(_logging || _dlgOpen ? null : term ? PTerm : _talk != null ? PTalk : null);
        }

        // Llave recogida: te acercas, o su propio script la ocultó/destruyó antes de que llegaras.
        bool Taken(int k, Vector3 p)
        {
            var g = pairs[k].key;
            if (!_had[k]) return false;
            return g == null || !g.activeInHierarchy || Near(g.transform, p, keyRadius);
        }

        void Prompt(string w)
        {
            if (w == _want) return;
            _want = w;
            _prompt.gameObject.SetActive(w != null);
            if (w != null) _prompt.text = w;
        }

        // E: abre la terminal que toca, o el diálogo al alcance.
        void Interact(Vector3 p)
        {
            int s = Step;
            if (debug) Debug.Log($"[Story] E pulsada. Paso {s} de {Last}.");
            for (int i = 0; i < pairs.Count; i++)
            {
                if (!Near(pairs[i].terminal, p, terminalRadius)) continue;
                if (s == i * 2 + 1) Advance(s);
                else if (s < i * 2 + 1) _q.Enqueue((Sys, Denied));
                return;
            }
            if (_talk != null && !(_talk.once && _talk.used) && Near(_talk.at, p, _talk.radius)) { Say(_talk); _talk = null; }
        }

        // ---------- Diálogos ----------

        /// <summary>Encola un diálogo por id (usable desde UnityEvents).</summary>
        public void Say(string id)
        {
            foreach (var d in dialogs) if (d.id == id) { Say(d); return; }
            Debug.LogWarning($"[Story] Diálogo '{id}' no encontrado.");
        }

        void Say(Dialog d)
        {
            if (d.lines == null || d.lines.Length == 0) return;
            d.used = true;
            _q.Enqueue((d.speaker, d.lines));
        }

        void Enq(string line) => _q.Enqueue((Sys, new[] { line }));

        void Talks(Vector3 p)
        {
            Dialog talk = null;
            foreach (var d in dialogs)
            {
                if (d.at == null || (d.once && d.used)) continue;
                bool now = Near(d.at, p, d.radius);
                if (now && !d.inside && d.auto) Say(d);
                d.inside = now;
                if (now && !d.auto && talk == null) talk = d;
            }
            _talk = talk;
        }

        void Open(string who, string[] lines)
        {
            _who = who; _lines = lines; _i = 0; _dlgOpen = true;
            _dlg.SetActive(true);
            Mouse(true);
            Render();
        }

        void Next()
        {
            if (++_i < _lines.Length) { Render(); return; }
            _dlgOpen = false;
            _dlg.SetActive(false);
            Mouse(false);
        }

        void Render() => _dlgTxt.text = string.IsNullOrEmpty(_who) ? _lines[_i] : _who + "\n" + _lines[_i];

        static void Mouse(bool free)
        {
            Cursor.visible = free;
            Cursor.lockState = free ? CursorLockMode.None : CursorLockMode.Locked;
        }

        // ---------- Tutoriales / secundarias ----------

        /// <summary>Añade una misión al panel de objetivos en tiempo de ejecución.</summary>
        public void AddMission(MissionData m)
        {
            if (m == null) return;
            foreach (var t in _active) if (t.mission == m) return;
            _active.Add(new Tutorial { mission = m, on = true });
            Goal();
        }

        /// <summary>Marca como completada una misión ya añadida.</summary>
        public void Complete(MissionData m)
        {
            foreach (var t in _active) if (t.mission == m) { t.done = true; Goal(); return; }
        }

        void Tutorials(InventoryController inv)
        {
            foreach (var t in tutorials)
                if (!t.on && t.mission != null && _seen >= t.fromStep) { t.on = true; _active.Add(t); Goal(); }

            Vector3 p = inv.transform.position;
            for (int i = 0; i < _active.Count; i++)
            {
                var t = _active[i];
                var m = t.mission;
                if (t.done || !Prereqs(m)) continue;
                bool items = Any(m.inventoryRequirements) || Any(m.craftingRequirements);
                if (items ? Have(m, inv) : t.at != null && Near(t.at, p, t.radius)) { t.done = true; Goal(); }
            }
        }

        static bool Any<T>(List<T> l) => l != null && l.Count > 0;

        bool Prereqs(MissionData m)
        {
            if (m.missionRequirements != null)
                foreach (var r in m.missionRequirements)
                    if (r != null && !IsDone(r)) return false;
            return true;
        }

        bool IsDone(MissionData m)
        {
            foreach (var t in _active) if (t.mission == m) return t.done;
            return false;
        }

        static bool Have(MissionData m, InventoryController inv)
        {
            var bag = inv.GetMyBag();
            if (m.inventoryRequirements != null)
                foreach (var r in m.inventoryRequirements)
                    if (r.item != null && Qty(bag, r.item) < (r.amount > 0 ? r.amount : 1)) return false;

            if (m.craftingRequirements != null)
                foreach (var tr in m.craftingRequirements)
                {
                    if (tr == null) continue;
                    if (!CraftingController.IsTradeCompletedAnywhere(tr)) return false;
                    if (tr.outputs == null) continue;
                    foreach (var o in tr.outputs)
                    {
                        if (o == null || o.item == null) continue;
                        int q = o.GetMinRequiredAmount();
                        if (q > 0 && Qty(bag, o.item) < q) return false;
                    }
                }
            return true;
        }

        public static int Qty(Dictionary<string, (ItemsData def, int qty)> bag, ItemsData item)
        {
            int n = 0;
            if (bag == null || item == null) return 0;
            foreach (var kv in bag)
            {
                var d = kv.Value.def;
                if (d != null && (d == item || (!string.IsNullOrEmpty(d.itemName) && string.Equals(d.itemName, item.itemName, StringComparison.OrdinalIgnoreCase))))
                    n += kv.Value.qty;
            }
            return n;
        }

        // Panel de objetivos (J): se reescribe solo cuando algo cambia, sin crear strings.
        void Goal()
        {
            if (_goal == null) return;
            bool show = _goalOn && _ready && !_logging;
            if (_goal.activeSelf != show) _goal.SetActive(show);
            if (!show) return;

            _sb.Clear().Append("<b>OBJETIVO</b>\n");
            int s = _seen;
            if (s >= Last) _sb.Append('—');
            else
            {
                var p = pairs[s / 2];
                string g = s % 2 == 0 ? p.keyGoal : p.openGoal;
                if (!string.IsNullOrEmpty(g)) _sb.Append(g);
                else _sb.Append(s % 2 == 0 ? "Recuperar la llave " : "Abrir la puerta ").Append(s / 2 + 1);
            }

            int n = 0;
            foreach (var t in _active)
            {
                if (t.done || n == 3) continue;
                _sb.Append(n++ == 0 ? "\n\n<b>SECUNDARIAS</b>\n• " : "\n• ").Append(t.mission.title).Append(": ").Append(t.mission.description);
            }
            _goalTxt.SetText(_sb);
        }

        // ---------- Input / consola ----------

        // Bloquea el input del jugador mientras hay consola, diálogo o espera inicial.
        void Block()
        {
            var inv = InventoryController.LocalInstance;
            if (inv == null) return;
            bool want = (!_ready && _t0 >= 0f) || _logging || _dlgOpen;
            if (want == _blocked) return;
            _blocked = want;
            var pi = inv.GetComponent<PlayerInput>();
            if (pi == null || !pi.enabled) return;
            if (want) pi.DeactivateInput(); else pi.ActivateInput();
        }

        static bool Fast() =>
            Keyboard.current != null && (Keyboard.current.eKey.isPressed || Keyboard.current.spaceKey.isPressed);

        // Máquina de escribir sin asignaciones por carácter: un único texto y maxVisibleCharacters.
        IEnumerator Log(string[] lines, Action done, float charDelay, float lineDelay, float hold)
        {
            _logging = true;
            Goal();
            Mouse(true);
            _txt.text = string.Join("\n", lines);
            _txt.maxVisibleCharacters = 0;
            _logBox.SetActive(true);

            int shown = 0;
            foreach (var line in lines)
            {
                int end = shown + line.Length;
                float acc = 0f;
                while (shown < end)
                {
                    if (Fast()) shown = end; // E/Espacio: resto de la línea al instante
                    else
                    {
                        acc += Time.unscaledDeltaTime;
                        int n = (int)(acc / charDelay);
                        if (n > 0) { acc -= n * charDelay; shown = Mathf.Min(end, shown + n); }
                    }
                    _txt.maxVisibleCharacters = shown;
                    yield return null;
                }
                _txt.maxVisibleCharacters = ++shown; // salto de línea
                float wait = Fast() ? 0.04f : lineDelay;
                for (float w = 0f; w < wait; w += Time.unscaledDeltaTime) yield return null;
            }
            for (float w = 0f; w < hold && !Fast(); w += Time.unscaledDeltaTime) yield return null;

            _logBox.SetActive(false);
            _logging = false;
            Mouse(false);
            Goal();
            done?.Invoke();
        }

        // ---------- UI: se construye una sola vez y se reutiliza ----------

        void Build()
        {
            // Overlay: consola (inicio/final) y diálogo
            var ui = NewCanvas("StoryUI", 500).transform;
            _logBox = Box(ui, Green, new Vector2(800, 380));
            _txt = Txt(Box(_logBox.transform, Dark, new Vector2(792, 372)).transform, 22, TextAlignmentOptions.TopLeft);
            Fill(_txt.gameObject, 24);
            _logBox.SetActive(false);

            _dlg = Box(ui, Green, new Vector2(640, 170));
            Anchor(_dlg, new Vector2(0.5f, 0f), new Vector2(0f, 190f));
            var di = Box(_dlg.transform, Dark, new Vector2(632, 162)).transform;
            _dlgTxt = Txt(di, 24, TextAlignmentOptions.TopLeft);
            _dlgTxt.color = Color.white;
            Fill(_dlgTxt.gameObject, 16);
            var hint = Txt(di, 14, TextAlignmentOptions.BottomRight);
            hint.text = "[E] Continuar";
            hint.color = new Color(Green.r, Green.g, Green.b, 0.55f);
            Fill(hint.gameObject, 8);
            _dlg.SetActive(false);

            // HUD: contador (abajo-izquierda), objetivos (arriba-izquierda), aviso de E
            var hud = NewCanvas("StoryHud", 91).transform;
            var cb = Box(hud, Green, new Vector2(240, 56));
            Anchor(cb, Vector2.zero, new Vector2(8f, 104f));
            _counter = Txt(Box(cb.transform, Dark, new Vector2(232, 48)).transform, 30, TextAlignmentOptions.Center);
            Fill(_counter.gameObject, 4);
            _counter.text = $"LLAVES  0/{pairs.Count}";

            _goal = Box(hud, Green, new Vector2(460, 230));
            Anchor(_goal, new Vector2(0f, 1f), new Vector2(8f, -8f));
            _goalTxt = Txt(Box(_goal.transform, Dark, new Vector2(452, 222)).transform, 22, TextAlignmentOptions.TopLeft);
            Fill(_goalTxt.gameObject, 12);
            _goal.SetActive(false);

            _prompt = Txt(hud, 30, TextAlignmentOptions.Center);
            Anchor(_prompt.gameObject, new Vector2(0.5f, 0f), new Vector2(0f, 110f));
            _prompt.rectTransform.sizeDelta = new Vector2(600f, 50f);
            _prompt.gameObject.SetActive(false);
        }

        static GameObject NewCanvas(string name, int order)
        {
            var g = new GameObject(name, typeof(Canvas), typeof(CanvasScaler));
            var c = g.GetComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = order;
            var cs = g.GetComponent<CanvasScaler>();
            cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            cs.referenceResolution = new Vector2(1920, 1080);
            return g;
        }

        static GameObject Box(Transform parent, Color col, Vector2 size)
        {
            var g = new GameObject("box", typeof(Image));
            g.transform.SetParent(parent, false);
            var im = g.GetComponent<Image>();
            im.color = col;
            im.raycastTarget = false;
            g.GetComponent<RectTransform>().sizeDelta = size;
            return g;
        }

        static TextMeshProUGUI Txt(Transform parent, int size, TextAlignmentOptions align)
        {
            var g = new GameObject("t", typeof(TextMeshProUGUI));
            g.transform.SetParent(parent, false);
            var t = g.GetComponent<TextMeshProUGUI>();
            t.fontSize = size;
            t.color = Green;
            t.alignment = align;
            t.fontStyle = FontStyles.Bold;
            t.raycastTarget = false;
            return t;
        }

        static void Anchor(GameObject g, Vector2 a, Vector2 pos)
        {
            var r = g.GetComponent<RectTransform>();
            r.anchorMin = r.anchorMax = r.pivot = a;
            r.anchoredPosition = pos;
        }

        static void Fill(GameObject g, float m)
        {
            var r = g.GetComponent<RectTransform>();
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(m, m);
            r.offsetMax = new Vector2(-m, -m);
        }

        // Al seleccionar el objeto: verde = terminal, amarillo = llave, cian = diálogo, magenta = tutorial.
        void OnDrawGizmosSelected()
        {
            foreach (var p in pairs)
            {
                Gizmos.color = Color.green; Zone(p.terminal, terminalRadius);
                Gizmos.color = Color.yellow; if (p.key != null) Zone(p.key.transform, keyRadius);
            }
            Gizmos.color = Color.cyan;
            foreach (var d in dialogs) Zone(d.at, d.radius);
            Gizmos.color = Color.magenta;
            foreach (var t in tutorials) Zone(t.at, t.radius);
        }

        static void Zone(Transform t, float r)
        {
            if (t == null) return;
            if (Bnd(t, out var b)) Gizmos.DrawWireCube(b.center, b.size + Vector3.one * (2f * r));
            else Gizmos.DrawWireSphere(t.position, r);
        }
    }
}