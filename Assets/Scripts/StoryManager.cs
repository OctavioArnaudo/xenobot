using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using Runtime.Scripts.Crafting;
using Runtime.Scripts.Inventory;
using Runtime.Scripts.Items;
using Runtime.Scripts.Missions;
using Runtime.Scripts.Gate;

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
        public bool IsBusy => _dlgOpen || _logging || _misOpen;

        /// <summary>Lo que el jugador debe alcanzar ahora: la llave (si ya está en escena) o la terminal. Null al terminar.</summary>
        public Transform Objective
        {
            get
            {
                if (!_ready || pairs.Count == 0 || _seen >= Last) return null;
                var p = pairs[_seen / 2];
                if (_seen % 2 == 0 && p.key != null && p.key.activeInHierarchy) return p.key.transform;
                return p.terminal;
            }
        }

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
        static readonly Color Dark = new Color(0.01f, 0.03f, 0.02f, 1f);
        static readonly Color Gold = new Color(1f, 0.85f, 0.2f);
        static readonly Color Dim = new Color(0.08f, 0.3f, 0.12f);
        static readonly string IntroText = string.Join("\n", Intro), FinalText = string.Join("\n", Final);

        NetworkVariable<int> _net = new NetworkVariable<int>();
        int _local, _seen, _i, _sel;
        bool[] _had;
        bool _ready, _logging, _blocked, _fin, _dlgOpen, _misOpen, _drag;
        float _t0 = -1f, _poll, _tut, _wheel, _scroll, _tgt, _max;
        string _want, _who;
        string[] _lines;
        Dialog _talk;
        readonly Queue<(string who, string[] lines)> _q = new Queue<(string, string[])>();
        readonly List<Tutorial> _active = new List<Tutorial>();
        readonly StringBuilder _sb = new StringBuilder(256);
        GameObject _logBox, _dlg, _mis;
        TextMeshProUGUI _txt, _dlgTxt, _detail, _foot, _counter, _prompt;
        RectTransform _track, _fill, _view, _detailRt, _barTrack, _bar;
        readonly List<Node> _nodes = new List<Node>(); // pool de círculos: se crean una vez y se reutilizan
        List<string>[] _hist;                          // historial narrativo por capítulo
        static Sprite s_disc;

        class Node { public RectTransform rt; public Image halo, disc; public TextMeshProUGUI num; public float scale = 1f; }

        // Teclas/botones que el panel lee directamente de los eventos de entrada (el resto del juego no las ve mientras está abierto).
        const int KJ = 1, KLeft = 2, KA = 4, KRight = 8, KD = 16, KEsc = 32, KBack = 64, BLeft = 128, BRight = 256, KUp = 512, KDown = 1024, KW = 2048, KS = 4096;
        const float ViewH = 270f, ScrollSpeed = 900f, WheelStep = 170f, BarW = 30f;
        int _prev, _hit;
        Vector2 _mpos;

        int Step => IsSpawned ? _net.Value : _local;
        int Last => pairs.Count * 2;

        void Awake()
        {
            Instance = this;
            InputSystem.onEvent += OnInput;
        }

        public override void OnDestroy()
        {
            InputSystem.onEvent -= OnInput;
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
            bool busy = _dlgOpen || _logging || _misOpen; // la E que cierra un diálogo no abre nada

            if (_dlgOpen && kb != null && (e || kb.spaceKey.wasPressedThisFrame)) Next();
            if (!_logging && !_dlgOpen && !_misOpen && _q.Count > 0 && MenuGate.TryOpen("dialog")) { var d = _q.Dequeue(); Open(d.who, d.lines); }
            if (!_misOpen && kb != null && kb.jKey.wasPressedThisFrame && !_logging && !_dlgOpen && pairs.Count > 0) Panel(true);
            if (_misOpen) PanelInput();

            var inv = InventoryController.LocalInstance;
            if (e && !busy && !_dlgOpen && !_logging && !_misOpen && inv != null) Interact(inv.transform.position);

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
            _hist = new List<string>[pairs.Count];
            for (int i = 0; i < _hist.Length; i++) _hist[i] = new List<string>();
            _had = new bool[pairs.Count];
            for (int i = 0; i < _had.Length; i++) _had[i] = pairs[i].key != null && pairs[i].key.activeInHierarchy;
            _seen = Step;
            if (debug) Debug.Log($"[Story] Listo. Paso {_seen} de {Last}.");
            for (int i = 1; i <= _seen; i++) Apply(i, false);
            Counter();
            if (Last > 0 && _seen >= Last) Finale();
            else if (_seen == 0)
            {
                Note(0, IntroText);
                StartCoroutine(Log(Intro, () => Line(0, "Sistemas mínimos operativos. Sigo la firma de datos más cercana.", true), 0.02f, 0.35f, 1.5f));
            }
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

            string t = key ? p.keyText : p.openText;
            if (string.IsNullOrEmpty(t)) t = s - 1 < Texts.Length ? Texts[s - 1] : "";
            int ch = (s - 1) / 2;
            Line(ch, t, announce); // siempre al historial; al diálogo solo si announce
            if (s == 5) Line(ch, "FASE II: la memoria vuelve, pero con lagunas. Debo reunir lo que falta.", announce);
            if (s == 15) Line(ch, "FASE III: tengo lo necesario. Es hora de huir.", announce);
            if (announce && s >= Last) Finale();
        }

        void Line(int ch, string t, bool say)
        {
            if (t.Length == 0) return;
            Note(ch, t);
            if (say) Enq(t);
        }

        void Note(int ch, string t)
        {
            if (_hist != null && ch >= 0 && ch < _hist.Length) _hist[ch].Add(t);
        }

        int Cur => Mathf.Min(_seen / 2, pairs.Count - 1);

        void Counter() => _counter.text = $"OBJETOS CLAVE  {(_seen + 1) / 2}/{pairs.Count}";

        void Finale()
        {
            if (_fin) return;
            _fin = true;
            StartCoroutine(FinalRun());
        }

        // El final espera a que se lea el último texto pendiente.
        IEnumerator FinalRun()
        {
            while (_q.Count > 0 || _dlgOpen) yield return null;
            Note(pairs.Count - 1, FinalText);
            yield return Log(Final, () => onEnd?.Invoke(), 0.03f, 0.6f, 3f);
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
            Prompt(_logging || _dlgOpen || _misOpen ? null : term ? PTerm : _talk != null ? PTalk : null);
        }

        // Llave recogida: te acercas, o su propio script la ocultó/destruyó antes de que llegaras.
        bool Taken(int k, Vector3 p)
        {
            var g = pairs[k].key;
            if (g != null && g.activeInHierarchy) { _had[k] = true; return false; }
            return _had[k]; // solo cuenta cuando el pickup la ocultó, o sea después de sumarla al inventario
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
            foreach (var l in d.lines) Note(Cur, string.IsNullOrEmpty(d.speaker) ? l : "<b>" + d.speaker + "</b>: " + l);
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
            Free(true);
            Render();
        }

        void Next()
        {
            if (++_i < _lines.Length) { Render(); return; }
            _dlgOpen = false;
            _dlg.SetActive(false);
            MenuGate.Close("dialog");
            Free(false);
        }

        void Render() => _dlgTxt.text = string.IsNullOrEmpty(_who) ? _lines[_i] : _who + "\n" + _lines[_i];

        static void Free(bool free)
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

        // ---------- Panel de misión (J): línea de tiempo de capítulos ----------

        public void ToggleMissions()
        {
            if (_misOpen) Panel(false);
            else if (!_logging && !_dlgOpen && pairs.Count > 0) Panel(true);
        }

        void Panel(bool on)
        {
            if (_mis == null || on == _misOpen) return;
            if (on && !MenuGate.TryOpen("mission")) return; // otro menú abierto
            if (!on) MenuGate.Close("mission");
            _misOpen = on;
            _mis.SetActive(on);
            Free(on);
            _hit = 0;
            if (on)
            {
                _sel = -1;
                _scroll = _tgt = _wheel = 0f;
                _drag = false;
                _prev = ~0; // lo que ya estaba pulsado al abrir no cuenta como pulsación nueva
                if (Mouse.current != null) _mpos = Mouse.current.position.ReadValue();
                // Suelta teclas/botones que estuvieran pulsados: el jugador no sigue andando ni disparando.
                if (Keyboard.current != null) InputSystem.ResetDevice(Keyboard.current);
                if (Mouse.current != null) InputSystem.ResetDevice(Mouse.current);
                foreach (var nd in _nodes) { nd.scale = 1f; nd.rt.localScale = Vector3.one; }
                _detail.alpha = 0f;
                _detail.enabled = false;
            }
            Goal();
        }

        // Mientras el panel está abierto, teclado y ratón se descartan para TODO el juego (combate, inventario, paneles)
        // y solo se leen aquí. No toca Time.timeScale: el juego (y la red) siguen corriendo.
        void OnInput(InputEventPtr e, InputDevice d)
        {
            if (!_misOpen || !(e.IsA<StateEvent>() || e.IsA<DeltaStateEvent>())) return;
            if (d is Keyboard k)
            {
                Edge(k.jKey, e, KJ); Edge(k.leftArrowKey, e, KLeft); Edge(k.aKey, e, KA);
                Edge(k.rightArrowKey, e, KRight); Edge(k.dKey, e, KD);
                Edge(k.escapeKey, e, KEsc); Edge(k.backspaceKey, e, KBack);
                Edge(k.upArrowKey, e, KUp); Edge(k.downArrowKey, e, KDown);
                Edge(k.wKey, e, KW); Edge(k.sKey, e, KS);
            }
            else if (d is Mouse m)
            {
                if (m.position.ReadValueFromEvent(e, out Vector2 pos)) _mpos = pos;
                if (m.scroll.ReadValueFromEvent(e, out Vector2 sc)) _wheel += sc.y;
                Edge(m.leftButton, e, BLeft); Edge(m.rightButton, e, BRight);
            }
            else return;
            e.handled = true;
        }

        void Edge(InputControl<float> c, InputEventPtr e, int bit)
        {
            if (!c.ReadValueFromEvent(e, out float v)) return;
            bool down = v > 0.5f;
            if (down && (_prev & bit) == 0) _hit |= bit;
            _prev = down ? _prev | bit : _prev & ~bit;
        }

        void PanelInput()
        {
            int h = _hit;
            _hit = 0;
            int now = Mathf.Min(_seen / 2, pairs.Count - 1);
            if ((h & KJ) != 0) { Panel(false); return; }
            if ((h & (KEsc | KBack | BRight)) != 0) { if (_sel >= 0) Pick(-1); else { Panel(false); return; } }
            if ((h & (KLeft | KA)) != 0) Pick(_sel < 0 ? now : _sel - 1);
            if ((h & (KRight | KD)) != 0) Pick(_sel < 0 ? now : _sel + 1);
            if ((h & BLeft) != 0)
            {
                if (_sel >= 0 && _barTrack.gameObject.activeSelf && RectTransformUtility.RectangleContainsScreenPoint(_barTrack, _mpos, null)) _drag = true;
                else
                {
                    int i = Hit(_mpos);
                    if (i >= 0) Pick(i == _sel ? -1 : i); // clic en el elegido = volver
                }
            }
            Scroll(h);
            Animate();
        }

        // Rueda, flechas/W/S y arrastre de la barra.
        void Scroll(int h)
        {
            if (_sel < 0 || _max <= 0f) { _wheel = 0f; return; }
            float dt = Time.unscaledDeltaTime;
            if (_wheel != 0f)
            {
                _tgt -= Mathf.Sign(_wheel) * Mathf.Max(1f, Mathf.Abs(_wheel) / 120f) * WheelStep;
                _wheel = 0f;
            }
            if ((_prev & (KUp | KW)) != 0) _tgt -= ScrollSpeed * dt;
            if ((_prev & (KDown | KS)) != 0) _tgt += ScrollSpeed * dt;
            if ((_prev & BLeft) == 0) _drag = false;
            if (_drag && RectTransformUtility.ScreenPointToLocalPointInRectangle(_barTrack, _mpos, null, out Vector2 lp))
            {
                var r = _barTrack.rect;
                float hh = _bar.sizeDelta.y;
                _scroll = _tgt = Mathf.InverseLerp(r.yMax - hh * 0.5f, r.yMin + hh * 0.5f, lp.y) * _max;
            }
            _tgt = Mathf.Clamp(_tgt, 0f, _max);
            float k = 1f - Mathf.Exp(-16f * dt); // desplazamiento suave hacia el objetivo
            float n = Mathf.Abs(_tgt - _scroll) < 0.5f ? _tgt : Mathf.Lerp(_scroll, _tgt, k);
            Fit(false, n - _scroll);
        }

        // Círculo bajo el puntero (el de centro más cercano si se solapan al agrandarse).
        int Hit(Vector2 pos)
        {
            int best = -1;
            float bd = float.MaxValue;
            for (int i = 0; i < pairs.Count && i < _nodes.Count; i++)
            {
                var rt = _nodes[i].rt;
                if (!RectTransformUtility.RectangleContainsScreenPoint(rt, pos, null)) continue;
                float d = (RectTransformUtility.WorldToScreenPoint(null, rt.position) - pos).sqrMagnitude;
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        void Pick(int i)
        {
            int s = i < 0 ? -1 : Mathf.Clamp(i, 0, pairs.Count - 1);
            if (s != _sel) _scroll = _tgt = 0f;
            _sel = s;
            Goal();
        }

        // Movimiento suave: el círculo elegido crece, el resto vuelve a su tamaño; el historial aparece con fundido.
        // Solo toca la UI mientras algo se está moviendo.
        void Animate()
        {
            float k = 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime);
            for (int i = 0; i < pairs.Count && i < _nodes.Count; i++)
            {
                var nd = _nodes[i];
                float t = i == _sel ? 1.6f : RectTransformUtility.RectangleContainsScreenPoint(nd.rt, _mpos, null) ? 1.2f : 1f;
                if (nd.scale == t) continue;
                nd.scale = Mathf.Abs(nd.scale - t) < 0.002f ? t : Mathf.Lerp(nd.scale, t, k);
                nd.rt.localScale = new Vector3(nd.scale, nd.scale, 1f);
            }
            float a = _sel >= 0 ? 1f : 0f;
            if (_detail.alpha != a)
            {
                _detail.alpha = Mathf.Abs(_detail.alpha - a) < 0.004f ? a : Mathf.Lerp(_detail.alpha, a, k);
                bool vis = _detail.alpha > 0.01f;
                if (_detail.enabled != vis) _detail.enabled = vis;
            }
        }

        // Ajusta el alto del texto, limita el scroll y coloca el tirador. remeasure = el texto cambió.
        void Fit(bool remeasure, float delta = 0f)
        {
            if (_sel < 0) { if (_barTrack.gameObject.activeSelf) _barTrack.gameObject.SetActive(false); return; }
            if (remeasure)
            {
                _detail.ForceMeshUpdate();
                float h = _detail.preferredHeight;
                _detailRt.sizeDelta = new Vector2(0f, h);
                _max = Mathf.Max(0f, h - ViewH);
            }
            _scroll = Mathf.Clamp(_scroll + delta, 0f, _max);
            _detailRt.anchoredPosition = new Vector2(0f, _scroll);
            bool on = _max > 0f;
            if (_barTrack.gameObject.activeSelf != on) _barTrack.gameObject.SetActive(on);
            if (!on) return;
            float hh = Mathf.Max(32f, ViewH * ViewH / (ViewH + _max));
            _bar.sizeDelta = new Vector2(0f, hh);
            _bar.anchoredPosition = new Vector2(0f, -(_scroll / _max) * (ViewH - hh));
        }

        // Se redibuja solo si el panel está abierto y algo cambia. Sin objetos nuevos: reutiliza el pool y un StringBuilder.
        void Goal()
        {
            if (!_misOpen || _hist == null) return;
            int n = pairs.Count, now = _seen / 2;
            Nodes(n);
            for (int i = 0; i < n; i++)
            {
                var nd = _nodes[i];
                bool done = _seen >= 2 * i + 2, cur = now == i;
                nd.rt.anchorMin = nd.rt.anchorMax = new Vector2(n > 1 ? i / (float)(n - 1) : 0.5f, 0.5f);
                nd.rt.anchoredPosition = Vector2.zero;
                nd.halo.enabled = i == _sel;
                nd.disc.color = done ? Green : cur ? Gold : Dim;
                nd.num.color = done || cur ? Dark : Green;
            }
            _fill.anchorMax = new Vector2(_seen >= Last ? 1f : n > 1 ? Mathf.Min(now, n - 1) / (float)(n - 1) : 0f, 0.5f);

            // Historial completo del capítulo elegido (se recorre con scroll)
            if (_sel >= 0)
            {
                _sb.Clear().Append("<b>CAPÍTULO ").Append(_sel + 1).Append("</b>\n");
                if (_sel > now) _sb.Append("\nCapítulo bloqueado.");
                else
                {
                    var h = _hist[_sel];
                    if (h.Count == 0) _sb.Append("\nSin registros.");
                    for (int i = 0; i < h.Count; i++) _sb.Append("\n• ").Append(h[i]);
                }
                _detail.SetText(_sb);
            }
            Fit(true);

            // Objetivo actual + secundarias
            _sb.Clear().Append("<b>OBJETIVO</b>  ");
            if (_seen >= Last) _sb.Append('—');
            else
            {
                var p = pairs[now];
                string g = _seen % 2 == 0 ? p.keyGoal : p.openGoal;
                if (!string.IsNullOrEmpty(g)) _sb.Append(g);
                else _sb.Append(_seen % 2 == 0 ? "Recuperar la llave " : "Abrir la puerta ").Append(now + 1);
            }
            int c = 0;
            foreach (var t in _active)
            {
                if (t.done || c == 2) continue;
                _sb.Append(c++ == 0 ? "\n<b>SECUNDARIAS</b>  " : "   ·   ").Append(t.mission.title);
            }
            _foot.SetText(_sb);
        }

        // Pool de círculos: crece solo si faltan; los sobrantes se desactivan, nunca se destruyen.
        void Nodes(int n)
        {
            while (_nodes.Count < n)
            {
                var halo = Box(_track, Color.white, new Vector2(64, 64)).GetComponent<Image>();
                halo.sprite = Disc();
                var disc = Box(halo.transform, Green, Vector2.zero).GetComponent<Image>();
                disc.sprite = Disc();
                Fill(disc.gameObject, 6);
                var num = Txt(disc.transform, 26, TextAlignmentOptions.Center);
                Fill(num.gameObject, 0);
                num.text = (_nodes.Count + 1).ToString();
                _nodes.Add(new Node { rt = halo.rectTransform, halo = halo, disc = disc, num = num });
            }
            for (int i = 0; i < _nodes.Count; i++)
            {
                var g = _nodes[i].rt.gameObject;
                if (g.activeSelf != i < n) g.SetActive(i < n);
            }
        }

        // Un único sprite circular generado por código, compartido por todos los círculos (un solo batch).
        static Sprite Disc()
        {
            if (s_disc != null) return s_disc;
            const int N = 64;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[N * N];
            float c = (N - 1) * 0.5f, r = N * 0.5f;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    px[y * N + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(r - d) * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return s_disc = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
        }

        // ---------- Input / consola ----------

        // Bloquea el input del jugador mientras hay consola, diálogo o espera inicial.
        void Block()
        {
            var inv = InventoryController.LocalInstance;
            if (inv == null) return;
            bool want = (!_ready && _t0 >= 0f) || _logging || _dlgOpen || _misOpen;
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
            Panel(false);
            while (!MenuGate.TryOpen("story")) yield return null; // espera a que se cierre cualquier menú
            _logging = true;
            Free(true);
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
            MenuGate.Close("story");
            Free(false);
            done?.Invoke();
        }

        // ---------- UI: se construye una sola vez y se reutiliza ----------

        void Build()
        {
            // Overlay: consola (inicio/final) y diálogo
            var ui = NewCanvas("StoryUI", 500).transform;
            _logBox = MenuGate.Panel(ui, new Vector2(800, 380), 0.5f).gameObject;
            _txt = Txt(MenuGate.Rnd(Box(_logBox.transform, Dark, new Vector2(760, 340))).transform, 22, TextAlignmentOptions.TopLeft);
            Fill(_txt.gameObject, 24);
            _logBox.SetActive(false);

            _dlg = MenuGate.Panel(ui, new Vector2(720, 200), 0.5f).gameObject;
            Anchor(_dlg, new Vector2(0.5f, 0f), new Vector2(0f, 190f));
            var di = MenuGate.Rnd(Box(_dlg.transform, Dark, new Vector2(688, 168))).transform;
            _dlgTxt = Txt(di, 24, TextAlignmentOptions.TopLeft);
            _dlgTxt.color = Color.white;
            Fill(_dlgTxt.gameObject, 18);
            _dlgTxt.rectTransform.offsetMin = new Vector2(18f, 30f); // deja sitio al aviso [E]
            _dlgTxt.enableAutoSizing = true;
            _dlgTxt.fontSizeMin = 12;
            _dlgTxt.fontSizeMax = 24;
            _dlgTxt.overflowMode = TextOverflowModes.Truncate;
            var hint = Txt(di, 14, TextAlignmentOptions.BottomRight);
            hint.text = "[E] Continuar";
            hint.color = new Color(Green.r, Green.g, Green.b, 0.55f);
            Fill(hint.gameObject, 8);
            _dlg.SetActive(false);

            // HUD: contador (abajo-izquierda), objetivos (arriba-izquierda), aviso de E
            var hud = NewCanvas("StoryHud", 91).transform;
            var cb = MenuGate.Rnd(Box(hud, Dark, new Vector2(270, 42)));
            Anchor(cb, Vector2.zero, new Vector2(30f, 116f));
            _counter = Txt(cb.transform, 22, TextAlignmentOptions.Center);
            Fill(_counter.gameObject, 4);
            _counter.text = $"OBJETOS CLAVE  0/{pairs.Count}";

            _prompt = Txt(hud, 30, TextAlignmentOptions.Center);
            Anchor(_prompt.gameObject, new Vector2(0.5f, 0f), new Vector2(0f, 110f));
            _prompt.rectTransform.sizeDelta = new Vector2(600f, 50f);
            _prompt.gameObject.SetActive(false);

            // Panel de misión (se abre con J): canvas propio para no recalcular el resto de la UI
            var mc = NewCanvas("StoryMission", 450).transform;
            _mis = MenuGate.Panel(mc, new Vector2(1100, 600), 0.5f).gameObject;
            var mi = MenuGate.Rnd(Box(_mis.transform, Dark, new Vector2(1068, 576))).transform;

            var title = Txt(mi, 30, TextAlignmentOptions.Left);
            title.text = "REGISTRO DE MISIÓN   <size=50%>[J] cerrar  ·  clic o ←/→ capítulo  ·  rueda o ↑/↓ leer  ·  Esc o clic derecho: volver</size>";
            Band(title.gameObject, 14, 44, 40);

            var tg = new GameObject("track", typeof(RectTransform));
            tg.transform.SetParent(mi, false);
            _track = Band(tg, 70, 140, 70);
            Bar(_track, Dim, 1f);
            _fill = Bar(_track, Green, 0f);
            Nodes(pairs.Count);

            // Visor del historial: texto grande a tamaño fijo dentro de una máscara + scrollbar
            var vg = new GameObject("view", typeof(RectTransform), typeof(RectMask2D));
            vg.transform.SetParent(mi, false);
            _view = Band(vg, 225, ViewH, 40);
            _view.sizeDelta = new Vector2(-128f, ViewH);
            _view.anchoredPosition = new Vector2(-24f, -225f);

            _detail = Txt(_view, 30, TextAlignmentOptions.TopLeft);
            _detail.color = Color.white;
            _detail.alpha = 0f;
            _detail.enabled = false;
            _detailRt = _detail.rectTransform;
            _detailRt.anchorMin = new Vector2(0f, 1f);
            _detailRt.anchorMax = new Vector2(1f, 1f);
            _detailRt.pivot = new Vector2(0f, 1f);
            _detailRt.anchoredPosition = Vector2.zero;
            _detailRt.sizeDelta = Vector2.zero;

            _barTrack = Box(mi, Dim, new Vector2(BarW, ViewH)).GetComponent<RectTransform>();
            _barTrack.anchorMin = _barTrack.anchorMax = _barTrack.pivot = new Vector2(1f, 1f);
            _barTrack.anchoredPosition = new Vector2(-40f, -225f);
            _bar = Box(_barTrack, Green, new Vector2(0f, 40f)).GetComponent<RectTransform>();
            _bar.anchorMin = new Vector2(0f, 1f);
            _bar.anchorMax = new Vector2(1f, 1f);
            _bar.pivot = new Vector2(0.5f, 1f);
            _bar.anchoredPosition = Vector2.zero;
            _barTrack.gameObject.SetActive(false);

            _foot = Txt(mi, 20, TextAlignmentOptions.TopLeft);
            _foot.enableAutoSizing = true;
            _foot.fontSizeMin = 12;
            _foot.fontSizeMax = 20;
            _foot.overflowMode = TextOverflowModes.Truncate;
            Band(_foot.gameObject, 500, 70, 40);
            _mis.SetActive(false);
        }

        static RectTransform Band(GameObject g, float top, float h, float pad)
        {
            var r = g.GetComponent<RectTransform>();
            r.anchorMin = new Vector2(0f, 1f);
            r.anchorMax = new Vector2(1f, 1f);
            r.pivot = new Vector2(0.5f, 1f);
            r.anchoredPosition = new Vector2(0f, -top);
            r.sizeDelta = new Vector2(-2f * pad, h);
            return r;
        }

        static RectTransform Bar(Transform parent, Color c, float x1)
        {
            var r = Box(parent, c, new Vector2(0f, 6f)).GetComponent<RectTransform>();
            r.anchorMin = new Vector2(0f, 0.5f);
            r.anchorMax = new Vector2(x1, 0.5f);
            r.anchoredPosition = Vector2.zero;
            return r;
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