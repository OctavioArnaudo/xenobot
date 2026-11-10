using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Runtime.Scripts.Gate;
using Runtime.Scripts.Hud;
using Runtime.Scripts.Items;
using static Runtime.Scripts.Gate.MenuGate;

namespace Runtime.Scripts.Inventory
{
    /// <summary>
    /// Panel multiple: inventario (izquierda), modelo del jugador (centro) y stats + skills (derecha).
    /// Se construye UNA vez al arrancar la partida (oculto) y despues solo se activa/desactiva:
    /// slots y filas en pool, sin instanciar ni destruir nada al abrir. El modelo es el propio jugador
    /// (con las mascaras equipadas) visto por una camara secundaria que solo ve la capa de preview.
    /// </summary>
    public sealed class CharacterPanel : MonoBehaviour
    {
        const int Slots = 24, Cols = 4, Cell = 84, Step = 89, IconPx = 256, IconLayer = 30, Rows = 12, Layer = 31;           // Layer: capa libre usada solo por la vista previa
        const float Fov = 28f, RotSpeed = 140f, Cw = 340f, Ch = 450f, SpinSpeed = 45f;
        const int LiveRt = 384;

        static CharacterPanel s_inst;

        static readonly Color Dk = new Color(0.02f, 0.06f, 0.04f, 1f);
        static readonly Color SlotOff = new Color(0.03f, 0.12f, 0.05f, 1f), SlotOn = new Color(0.05f, 0.24f, 0.1f, 1f);
        static readonly Color[] BarCol =
        {
            new Color(0.85f, 0.10f, 0.10f), new Color(0.10f, 0.80f, 0.95f), new Color(0.95f, 0.75f, 0.10f), new Color(0.65f, 0.25f, 0.90f)
        };
        static readonly string[] StatName = { "HP", "JET", "AMMO", "SHIELD", "ATK", "DEF", "LVL", "EXP", "TIME" };
        static readonly Color SkillBase = new Color(0.1f, 0.85f, 1f), SkillMask = new Color(1f, 0.25f, 0.8f);

        sealed class Slot
        {
            public RectTransform root;
            public Image bg;
            public RawImage icon;
            public TextMeshProUGUI qty;
            public ItemsData def;
            public string qtyStr;
            public int state = -1;
        }

        sealed class Row { public GameObject go; public Image dot; public TextMeshProUGUI name, key; }

        InventoryController _inv;
        HudController _hud;
        Slot[] _slot = new Slot[Slots];
        Row[] _row = new Row[Rows];
        GameObject _empty;
        readonly RectTransform[] _fill = new RectTransform[4];
        readonly float[] _lastF = { -1f, -1f, -1f, -1f };
        readonly TextMeshProUGUI[] _val = new TextMeshProUGUI[9];

        Camera _cam;
        RenderTexture _rt;
        Hold _left, _right;
        float _angle, _dist = 5f, _tick, _syncT;
        Vector3 _pivotOff = Vector3.up;

        // seleccion: UNA sola tarjeta en pool que crece desde el slot elegido
        RectTransform _grid, _card, _useRt, _dropRt;
        Image _cardBg;
        RawImage _cardIcon;
        TextMeshProUGUI _cardName, _useTxt;
        CanvasGroup _body;
        Button _useBtn, _dropBtn;
        ItemsData _selDef;
        int _sel = -1, _cst = -2;
        string _cq;
        float _a, _tgt;
        Vector2 _from, _to;

        // iconos 3D: cada prefab se hornea UNA vez en un RenderTexture y se reutiliza
        readonly Dictionary<ItemsData, Texture> _icons = new Dictionary<ItemsData, Texture>();
        readonly List<RenderTexture> _rts = new List<RenderTexture>();
        GameObject _rig, _box, _liveRoot, _live; // _live: modelo del objeto seleccionado, girando
        Bounds _liveB;
        RenderTexture _liveRt;
        Camera _ic;
        Light _il;

        readonly List<Renderer> _rends = new List<Renderer>(64);
        readonly Dictionary<GameObject, int> _saved = new Dictionary<GameObject, int>();
        readonly List<string> _names = new List<string>(16), _keys = new List<string>(16);

        // ---------------- API ----------------

        /// <summary>Construye el panel (oculto). Llamar una vez al aparecer el jugador local.</summary>
        public static void Prebuild(InventoryController inv)
        {
            if (s_inst != null) return;
            var go = new GameObject("CharacterPanel", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var c = go.GetComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = 1000;
            var cs = go.GetComponent<CanvasScaler>();
            cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            cs.referenceResolution = new Vector2(1920f, 1080f);
            cs.matchWidthOrHeight = 0.5f;
            s_inst = go.AddComponent<CharacterPanel>();
            s_inst._inv = inv;
            s_inst.Build();
            go.SetActive(false);
        }

        /// <summary>Esc (via MenuGate): si hay un objeto seleccionado lo deselecciona y devuelve true, para que Esc no cierre el panel.</summary>
        public static bool TryDeselect()
        {
            if (s_inst == null || s_inst._sel < 0) return false;
            s_inst.Unpick();
            return true;
        }

        public static void Set(InventoryController inv, bool open)
        {
            if (open) Prebuild(inv);
            if (s_inst != null) s_inst.Apply(inv, open);
        }

        // ---------------- construccion ----------------

        void Build()
        {
            var dim = Img(transform, new Color(0f, 0f, 0f, 0.6f));
            Fit(dim, 0f, 0f, 1f, 1f);
            dim.GetComponent<Image>().raycastTarget = true; // bloquea clics al mundo
            dim.gameObject.AddComponent<Click>().Fn = Unpick;

            var lay = Blank(transform);
            lay.anchorMin = lay.anchorMax = lay.pivot = new Vector2(0.5f, 0.5f);
            lay.sizeDelta = new Vector2(1400f, 800f);
            lay.gameObject.AddComponent<Click>().Fn = Unpick; // clic en cualquier zona vacia = deseleccionar

            BuildInventory(lay);
            BuildViewer(lay);
            BuildStats(lay);
            BuildSkills(lay);

            var x = Btn(lay, "X", 26, new Vector2(44f, 44f), () => InventoryController.Toggle());
            x.anchorMin = x.anchorMax = x.pivot = new Vector2(1f, 0f);
            x.anchoredPosition = new Vector2(0f, 8f);
        }

        void BuildInventory(RectTransform lay)
        {
            var f = Frame(lay, PanelSkin.Kind.Inventory, new Vector2(0f, 0.5f), new Vector2(470f, 720f));
            Title(f, "INVENTARIO", 0.885f);

            var grid = Blank(f);
            Fit(grid, 0.11f, 0.09f, 0.89f, 0.87f, 8f);
            for (int i = 0; i < Slots; i++) _slot[i] = MakeSlot(grid, i);

            var e = Txt(grid, 22, new Color(0.2f, 1f, 0.35f, 0.5f), TextAlignmentOptions.Center, FontStyles.Normal);
            e.text = "Inventario vacio";
            Fit(e.rectTransform, 0f, 0.4f, 1f, 0.6f);
            _empty = e.gameObject;
            BuildCard(grid);
        }

        Slot MakeSlot(RectTransform grid, int i)
        {
            var s = new Slot();
            s.root = Blank(grid);
            s.root.anchorMin = s.root.anchorMax = s.root.pivot = new Vector2(0f, 1f);
            s.root.sizeDelta = new Vector2(Cell, Cell);
            s.root.anchoredPosition = new Vector2((i % Cols) * Step, -(i / Cols) * Step);

            var bg = Rnd(Img(s.root, SlotOff));
            Fit(bg, 0f, 0f, 1f, 1f);
            s.bg = bg.GetComponent<Image>();
            s.bg.raycastTarget = true; // Img lo deja apagado: sin esto el Button no recibe clics
            var b = bg.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            int k = i;
            b.onClick.AddListener(() => Pick(k));

            s.icon = RawIcon(bg);
            Fit(s.icon.rectTransform, 0f, 0f, 1f, 1f, 3f);

            s.qty = Txt(bg, 16, Green, TextAlignmentOptions.BottomRight, FontStyles.Bold);
            s.qty.raycastTarget = false;
            Fit(s.qty.rectTransform, 0f, 0f, 1f, 1f, 4f);
            return s;
        }

        static RawImage RawIcon(RectTransform p)
        {
            var g = new GameObject("i", typeof(RawImage));
            g.transform.SetParent(p, false);
            var r = g.GetComponent<RawImage>();
            r.raycastTarget = false;
            return r;
        }

        // ---------------- tarjeta de seleccion ----------------

        void BuildCard(RectTransform grid)
        {
            _grid = grid;
            _card = Frame(grid, PanelSkin.Kind.Card, new Vector2(0f, 1f), new Vector2(Cw, Ch)); // chapa oxidada + hueco negro
            _cardBg = _card.GetComponent<Image>();
            _cardBg.raycastTarget = true;
            _card.gameObject.AddComponent<Button>().transition = Selectable.Transition.None; // absorbe clics

            _cardIcon = RawIcon(_card);
            Fit(_cardIcon.rectTransform, 0.09f, 0.29f, 0.91f, 0.91f); // cuadrado dentro del hueco

            var body = Blank(_card);
            Fit(body, 0.09f, 0.075f, 0.91f, 0.29f);
            _body = body.gameObject.AddComponent<CanvasGroup>();

            _cardName = Txt(body, 26, Amber, TextAlignmentOptions.Center, FontStyles.Bold);
            Fit(_cardName.rectTransform, 0f, 0.52f, 1f, 1f);

            _useRt = Mini(body, "USE", out _useTxt, out _useBtn);
            _dropRt = Mini(body, "DROP", out var dt, out _dropBtn);
            _useTxt.fontSize = dt.fontSize = 24;
            _useBtn.onClick.AddListener(() => { if (_selDef != null) { _inv.UseItem(_selDef); _tick = 1f; } });
            _dropBtn.onClick.AddListener(() => { if (_selDef != null) { _inv.DropItem(_selDef); Unpick(); } });
            _card.gameObject.SetActive(false);
        }

        void Pick(int i)
        {
            var s = _slot[i];
            if (s.def == null) return;
            if (_sel == i) { Unpick(); return; }
            _sel = i;
            _selDef = s.def;
            _cst = -2;
            var g = _grid.rect;
            _from = s.root.anchoredPosition;
            _to = new Vector2((g.width - Cw) * 0.5f, -(g.height - Ch) * 0.5f);
            _a = 0f;
            _tgt = 1f;
            _cardIcon.texture = s.icon.texture;
            LiveStart(s.def);
            if (_live != null) _cardIcon.texture = _liveRt;
            _cardIcon.enabled = _cardIcon.texture != null;
            _card.gameObject.SetActive(true);
            FillCard(s);
            Anim();
        }

        void Unpick() { if (_sel < 0) return; _sel = -1; _selDef = null; _tgt = 0f; } // se encoge de vuelta al slot

        void Shut() { _sel = -1; _selDef = null; _a = _tgt = 0f; LiveStop(); if (_card != null) _card.gameObject.SetActive(false); }

        void FillCard(Slot s)
        {
            _cst = s.state;
            _cq = s.qtyStr;
            _cardName.text = s.def.itemName + "  " + s.qtyStr;
            bool eq = (s.state & 2) != 0, use = (s.state & 1) != 0, drop = (s.state & 4) != 0;
            _useBtn.gameObject.SetActive(use);
            _dropBtn.gameObject.SetActive(drop);
            _useTxt.text = eq ? "QUIT" : "USE";
            Fit(_useRt, 0f, 0.04f, drop ? 0.48f : 1f, 0.48f);
            Fit(_dropRt, use ? 0.52f : 0f, 0.04f, 1f, 0.48f);
        }

        void Anim()
        {
            float e = _a * _a * (3f - 2f * _a);
            _card.anchoredPosition = Vector2.Lerp(_from, _to, e);
            _card.localScale = Vector3.one * Mathf.Lerp(Cell / Cw, 1f, e);
            _body.alpha = Mathf.Clamp01((_a - 0.5f) * 2f);
            var c = _cardBg.color;
            c.a = Mathf.Clamp01((_a - 0.2f) * 2f);
            _cardBg.color = c;
        }

        // ---------------- iconos 3D (modelo del objeto, horneado una vez) ----------------

        Texture Icon(ItemsData d)
        {
            if (_icons.TryGetValue(d, out var t)) return t;
            t = Bake(d);
            if (t == null && d.itemSprite != null) t = d.itemSprite.texture;
            _icons[d] = t;
            return t;
        }

        void MakeRig()
        {
            _rig = new GameObject("IconRig");
            _rig.transform.position = new Vector3(5000f, 5000f, 5000f);
            _box = new GameObject("Box");
            _box.transform.SetParent(_rig.transform, false);
            _box.SetActive(false);

            var cg = new GameObject("Cam");
            cg.transform.SetParent(_rig.transform, false);
            _ic = cg.AddComponent<Camera>();
            _ic.enabled = false;
            _ic.clearFlags = CameraClearFlags.SolidColor;
            _ic.backgroundColor = Color.clear;
            _ic.cullingMask = 1 << IconLayer;
            _ic.orthographic = true;
            _ic.allowHDR = false;

            var lg = new GameObject("Light");
            lg.transform.SetParent(_rig.transform, false);
            _il = lg.AddComponent<Light>();
            _il.type = LightType.Directional;
            _il.cullingMask = 1 << IconLayer;
            _il.intensity = 1.3f;
            _il.shadows = LightShadows.None;
            _il.enabled = false; // solo se enciende durante el render del icono
        }

        // Instancia el prefab sin scripts, sin fisica y solo en la capa de iconos. La caja esta inactiva: no corre Awake/OnEnable.
        GameObject Spawn(ItemsData d)
        {
            if (_ic == null) MakeRig();
            var go = Instantiate(d.itemPrefab, _box.transform);
            go.SetActive(true);
            var mb = go.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = mb.Length - 1; i >= 0; i--) if (mb[i] != null) DestroyImmediate(mb[i]);
            foreach (var col in go.GetComponentsInChildren<Collider>(true)) DestroyImmediate(col);
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(rb);
            foreach (var tr in go.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = IconLayer;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                if (!(r is MeshRenderer || r is SkinnedMeshRenderer)) r.enabled = false;
            return go;
        }

        static bool Measure(GameObject go, out Bounds b)
        {
            bool any = false;
            b = default;
            foreach (var r in go.GetComponentsInChildren<Renderer>(false))
            {
                if (!r.enabled) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return any;
        }

        static RenderTexture NewRt(int px) => new RenderTexture(px, px, 16, RenderTextureFormat.ARGB32) { antiAliasing = 4, filterMode = FilterMode.Bilinear };

        // Encuadra la esfera que envuelve al objeto y renderiza una vez en rt (la luz solo se enciende durante el render).
        void Shot(Bounds b, RenderTexture rt)
        {
            float rad = Mathf.Max(b.extents.magnitude, 0.01f);
            Vector3 dir = new Vector3(0.7f, 0.5f, -0.9f).normalized;
            var ct = _ic.transform;
            ct.SetPositionAndRotation(b.center + dir * rad * 4f, Quaternion.LookRotation(-dir));
            _ic.orthographicSize = rad * 1.05f;
            _ic.nearClipPlane = 0.01f;
            _ic.farClipPlane = rad * 8f;
            _il.transform.rotation = ct.rotation * Quaternion.Euler(30f, -25f, 0f);
            _ic.targetTexture = rt;
            _il.enabled = true;
            _ic.Render();
            _il.enabled = false;
            _ic.targetTexture = null;
        }

        Texture Bake(ItemsData d)
        {
            if (d.itemPrefab == null) return null;
            var go = Spawn(d);
            _box.SetActive(true);
            RenderTexture rt = null;
            if (Measure(go, out var b))
            {
                rt = NewRt(IconPx);
                _rts.Add(rt);
                Shot(b, rt);
            }
            DestroyImmediate(go);
            _box.SetActive(false);
            return rt;
        }

        // Modelo en vivo del objeto seleccionado: una sola instancia y un solo RenderTexture reutilizado; se destruye al cerrar la tarjeta.
        void LiveStart(ItemsData d)
        {
            LiveStop();
            if (d.itemPrefab == null) return;
            var go = Spawn(d);
            if (_liveRoot == null)
            {
                _liveRoot = new GameObject("Live");
                _liveRoot.transform.SetParent(_rig.transform, false);
                _liveRoot.transform.localPosition = new Vector3(100f, 0f, 0f); // lejos de la caja de iconos
            }
            go.transform.SetParent(_liveRoot.transform, false);
            if (!Measure(go, out _liveB)) { DestroyImmediate(go); return; }
            if (_liveRt == null) { _liveRt = NewRt(LiveRt); _rts.Add(_liveRt); }
            _live = go;
            Shot(_liveB, _liveRt);
        }

        void LiveStop()
        {
            if (_live == null) return;
            DestroyImmediate(_live);
            _live = null;
        }

        void BuildViewer(RectTransform lay)
        {
            var f = Frame(lay, PanelSkin.Kind.Viewer, new Vector2(0.5f, 0.5f), new Vector2(400f, 800f));

            _rt = new RenderTexture(384, 944, 16, RenderTextureFormat.ARGB32) { name = "CharacterPreviewRT" };
            _rt.Create();

            var camGo = new GameObject("CharacterPreviewCam");
            _cam = camGo.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.02f, 0.03f, 0.05f, 1f);
            _cam.cullingMask = 1 << Layer;
            _cam.fieldOfView = Fov;
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 40f;
            _cam.allowHDR = false;
            _cam.allowMSAA = false;
            _cam.depth = -50f;
            _cam.targetTexture = _rt;
            _cam.enabled = false;

            var v = new GameObject("View", typeof(RawImage));
            v.transform.SetParent(f, false);
            var raw = v.GetComponent<RawImage>();
            raw.texture = _rt;
            Fit(raw.rectTransform, 0.2575f, 0.281f, 0.7425f, 0.879f);
            v.AddComponent<Drag>().OnDelta = dx => _angle -= dx * 0.4f;

            _left = Arrow(f, 0.33f, true);
            _right = Arrow(f, 0.67f, false);
        }

        Hold Arrow(RectTransform f, float ax, bool flip)
        {
            var a = Img(f, Color.white);
            var im = a.GetComponent<Image>();
            im.sprite = PanelSkin.Get(PanelSkin.Kind.Arrow);
            im.raycastTarget = true;
            a.anchorMin = a.anchorMax = a.pivot = new Vector2(ax, 0.12f);
            a.sizeDelta = new Vector2(84f, 48f);
            if (flip) a.localScale = new Vector3(-1f, 1f, 1f);
            var h = a.gameObject.AddComponent<Hold>();
            h.img = im;
            im.color = Hold.Idle;
            return h;
        }

        void BuildStats(RectTransform lay)
        {
            var f = Frame(lay, PanelSkin.Kind.Stats, new Vector2(1f, 1f), new Vector2(470f, 390f));
            Title(f, "STATS", 0.84f);
            var area = Blank(f);
            Fit(area, 0.09f, 0.09f, 0.91f, 0.80f, 6f);
            for (int i = 0; i < 9; i++)
            {
                var row = RowRect(area, i, 30f);
                var l = Txt(row, 18, Amber, TextAlignmentOptions.Left, FontStyles.Bold);
                l.text = StatName[i];
                Fit(l.rectTransform, 0f, 0f, 0.27f, 1f);

                if (i < 4)
                {
                    var bg = Rnd(Img(row, new Color(0.02f, 0.03f, 0.04f)));
                    Fit(bg, 0.29f, 0.08f, 1f, 0.92f);
                    var fill = Img(bg, BarCol[i]);
                    Fit(fill, 0f, 0f, 1f, 1f, 2f);
                    _fill[i] = fill;
                    _val[i] = Txt(bg, 16, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
                    Fit(_val[i].rectTransform, 0f, 0f, 1f, 1f);
                }
                else
                {
                    _val[i] = Txt(row, 18, Color.white, TextAlignmentOptions.Left, FontStyles.Bold);
                    Fit(_val[i].rectTransform, 0.29f, 0f, 1f, 1f, 2f);
                }
            }
        }

        void BuildSkills(RectTransform lay)
        {
            var f = Frame(lay, PanelSkin.Kind.Skills, new Vector2(1f, 0f), new Vector2(470f, 390f));
            Title(f, "SKILLS", 0.84f);
            var area = Blank(f);
            Fit(area, 0.09f, 0.08f, 0.91f, 0.80f, 6f);
            for (int i = 0; i < Rows; i++)
            {
                var row = RowRect(area, i, 22f);
                var r = new Row { go = row.gameObject };
                var dot = Img(row, SkillBase);
                dot.anchorMin = dot.anchorMax = dot.pivot = new Vector2(0f, 0.5f);
                dot.sizeDelta = new Vector2(11f, 11f);
                dot.anchoredPosition = new Vector2(10f, 0f);
                dot.localRotation = Quaternion.Euler(0f, 0f, 45f);
                r.dot = dot.GetComponent<Image>();
                r.name = Txt(row, 16, Green, TextAlignmentOptions.Left, FontStyles.Bold);
                Fit(r.name.rectTransform, 0.08f, 0f, 0.7f, 1f);
                r.key = Txt(row, 15, Amber, TextAlignmentOptions.Right, FontStyles.Bold);
                Fit(r.key.rectTransform, 0.7f, 0f, 1f, 1f);
                row.gameObject.SetActive(false);
                _row[i] = r;
            }
        }

        // ---------------- helpers de UI ----------------

        static RectTransform Blank(Transform p)
        {
            var g = new GameObject("r", typeof(RectTransform));
            g.transform.SetParent(p, false);
            return g.GetComponent<RectTransform>();
        }

        static RectTransform Frame(Transform p, PanelSkin.Kind k, Vector2 anchor, Vector2 size)
        {
            var r = Img(p, Color.white);
            r.GetComponent<Image>().sprite = PanelSkin.Get(k);
            r.GetComponent<Image>().raycastTarget = false; // el clic en el marco deselecciona
            r.anchorMin = r.anchorMax = r.pivot = anchor;
            r.anchoredPosition = Vector2.zero;
            r.sizeDelta = size;
            return r;
        }

        static void Title(RectTransform frame, string text, float y0)
        {
            var t = Txt(frame, 34, Amber, TextAlignmentOptions.Center, FontStyles.Bold);
            t.text = text;
            Fit(t.rectTransform, 0.1f, y0, 0.9f, 0.995f);
        }

        static RectTransform RowRect(RectTransform area, int i, float h)
        {
            var r = Blank(area);
            r.anchorMin = new Vector2(0f, 1f);
            r.anchorMax = new Vector2(1f, 1f);
            r.pivot = new Vector2(0.5f, 1f);
            r.anchoredPosition = new Vector2(0f, -i * h);
            r.sizeDelta = new Vector2(0f, h - 2f);
            return r;
        }

        // Boton con pantalla oscura que se enciende al pasar / pulsar.
        static RectTransform Mini(RectTransform p, string label, out TextMeshProUGUI txt, out Button btn)
        {
            var r = Rnd(Img(p, Color.white), 6);
            var im = r.GetComponent<Image>();
            im.raycastTarget = true;
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0f, 1f);
            btn = r.gameObject.AddComponent<Button>();
            Hot(btn, im);
            txt = Txt(r, 14, Green, TextAlignmentOptions.Center, FontStyles.Bold);
            txt.text = label;
            Fit(txt.rectTransform, 0f, 0f, 1f, 1f);
            return r;
        }

        static RectTransform Btn(Transform p, string label, int size, Vector2 wh, UnityEngine.Events.UnityAction click)
        {
            var r = Mini(p as RectTransform, label, out var t, out var b);
            t.fontSize = size;
            r.sizeDelta = wh;
            b.onClick.AddListener(click);
            return r;
        }

        static void Hot(Button b, Image g)
        {
            b.targetGraphic = g;
            b.transition = Selectable.Transition.ColorTint;
            var cb = b.colors;
            cb.normalColor = cb.selectedColor = Dk;
            cb.highlightedColor = new Color(0.06f, 0.32f, 0.13f, 1f);
            cb.pressedColor = new Color(0.16f, 0.62f, 0.28f, 1f);
            cb.fadeDuration = 0.06f;
            b.colors = cb;
        }

        // ---------------- apertura / cierre ----------------

        void Apply(InventoryController inv, bool open)
        {
            _inv = inv;
            Shut();
            gameObject.SetActive(open);
            if (open)
            {
                _hud = inv != null ? inv.GetComponent<HudController>() : null;
                _angle = 0f;
                _tick = 0f;
                _syncT = 0f;
                for (int i = 0; i < 4; i++) _lastF[i] = -1f;
                SyncPreview();
                RefreshSlots();
                RefreshStats();
                RefreshSkills();
                _cam.enabled = true;
            }
            else
            {
                _cam.enabled = false;
                RestoreLayers();
            }
        }

        void OnDisable() { if (_cam != null) _cam.enabled = false; RestoreLayers(); }

        void OnDestroy()
        {
            RestoreLayers();
            for (int i = 0; i < _rts.Count; i++) if (_rts[i] != null) { _rts[i].Release(); Destroy(_rts[i]); }
            if (_rig != null) Destroy(_rig);
            if (_cam != null) Destroy(_cam.gameObject);
            if (_rt != null) { _rt.Release(); Destroy(_rt); }
        }

        // ---------------- bucle (solo mientras esta abierto) ----------------

        void Update()
        {
            if (_inv == null) { gameObject.SetActive(false); return; }
            float dt = Time.unscaledDeltaTime;
            if (_sel >= 0 && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) Unpick();
            if (_a != _tgt)
            {
                _a = Mathf.MoveTowards(_a, _tgt, dt * 8f);
                Anim();
                if (_a <= 0f) { _card.gameObject.SetActive(false); LiveStop(); }
            }
            if (_live != null)
            {
                _live.transform.RotateAround(_liveB.center, Vector3.up, SpinSpeed * dt);
                Shot(_liveB, _liveRt);
            }
            if (_left != null && _left.Down) _angle += RotSpeed * dt;
            if (_right != null && _right.Down) _angle -= RotSpeed * dt;

            _tick += dt;
            if (_tick >= 0.25f) { _tick = 0f; RefreshSlots(); RefreshStats(); }
            if (Time.unscaledTime >= _syncT) { _syncT = Time.unscaledTime + 0.5f; SyncPreview(); RefreshSkills(); }
            PlaceCam();
        }

        // ---------------- inventario ----------------

        void RefreshSlots()
        {
            int n = 0;
            for (int i = 0; i < Slots; i++)
            {
                var s = _slot[i];
                if (_inv.TryGetSlot(i, out var def, out int qty, out bool eq))
                {
                    n++;
                    if (!s.root.gameObject.activeSelf) s.root.gameObject.SetActive(true);
                    if (s.def != def)
                    {
                        s.def = def;
                        var tex = Icon(def);
                        s.icon.texture = tex;
                        s.icon.enabled = tex != null;
                    }
                    string q = InventoryController.QtyStr(qty);
                    if (!ReferenceEquals(s.qtyStr, q)) { s.qtyStr = q; s.qty.text = q; }

                    bool canUse = eq ? def.isQuitable : def.isUsable;
                    int st = (canUse ? 1 : 0) | (eq ? 2 : 0) | (def.isDropable ? 4 : 0);
                    if (st != s.state) { s.state = st; s.bg.color = eq ? SlotOn : SlotOff; }
                }
                else if (s.root.gameObject.activeSelf)
                {
                    s.root.gameObject.SetActive(false);
                    s.def = null;
                    s.state = -1;
                }
            }
            if (_empty.activeSelf != (n == 0)) _empty.SetActive(n == 0);

            if (_sel >= 0)
            {
                var s = _slot[_sel];
                if (s.def != _selDef) Shut();                                           // el objeto ya no esta (usado / tirado)
                else if (s.state != _cst || !ReferenceEquals(s.qtyStr, _cq)) FillCard(s); // QUIT/USE o cantidad cambiaron
            }
        }

        // ---------------- stats / skills ----------------

        void RefreshStats()
        {
            if (_hud == null) return;
            for (int i = 0; i < 4; i++)
            {
                float f = _hud.BarFraction(i);
                if (!Mathf.Approximately(f, _lastF[i])) { _lastF[i] = f; _fill[i].anchorMax = new Vector2(f, 1f); }
                Set(_val[i], _hud.BarText(i));
            }
            Set(_val[4], _hud.Attack.ToString("F0"));
            Set(_val[5], _hud.Defense.ToString("F0"));
            Set(_val[6], _hud.Level.ToString());
            Set(_val[7], _hud.Exp.ToString("F0") + " / " + _hud.ExpToLevelUp.ToString("F0"));
            Set(_val[8], _hud.TimeStr);
        }

        static void Set(TextMeshProUGUI t, string s) { if (s != null && t.text != s) t.text = s; }

        void RefreshSkills()
        {
            _names.Clear();
            _keys.Clear();
            if (_hud != null) _hud.CollectSkills(_names, _keys);
            int baseCount = _names.Count;
            _inv.CollectEquipSkills(_names, _keys);

            for (int i = 0; i < Rows; i++)
            {
                var r = _row[i];
                bool on = i < _names.Count;
                if (r.go.activeSelf != on) r.go.SetActive(on);
                if (!on) continue;
                Set(r.name, _names[i]);
                Set(r.key, _keys[i]);
                r.dot.color = i < baseCount ? SkillBase : SkillMask;
            }
        }

        // ---------------- vista previa del jugador ----------------

        // Mueve las mallas del jugador a la capa de preview (solo mientras el panel esta abierto) para que la
        // camara secundaria vea unicamente al personaje con sus mascaras. Se repite cada 0.5 s para captar equipamiento nuevo.
        void SyncPreview()
        {
            if (_inv == null) return;
            var root = _inv.transform;
            root.GetComponentsInChildren(true, _rends);
            bool any = false;
            Bounds b = default;
            for (int i = 0; i < _rends.Count; i++)
            {
                var r = _rends[i];
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (!(r is MeshRenderer || r is SkinnedMeshRenderer) || r.TryGetComponent<TMP_Text>(out _)) continue;
                var go = r.gameObject;
                if (!_saved.ContainsKey(go)) _saved[go] = go.layer;
                go.layer = Layer;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            if (!any) return;

            _pivotOff = b.center - root.position;
            float tanV = Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad);
            float aspect = (float)_rt.width / _rt.height;
            float distH = b.size.y * 0.5f * 1.25f / tanV;
            float distW = Mathf.Max(b.size.x, b.size.z) * 0.5f * 1.1f / (tanV * aspect);
            _dist = Mathf.Max(2f, distH, distW * 0.85f);
        }

        void RestoreLayers()
        {
            foreach (var kv in _saved) if (kv.Key != null) kv.Key.layer = kv.Value;
            _saved.Clear();
        }

        void PlaceCam()
        {
            var t = _inv.transform;
            Vector3 fwd = Vector3.ProjectOnPlane(t.forward, Vector3.up);
            fwd = fwd.sqrMagnitude < 0.001f ? Vector3.forward : fwd.normalized;
            Vector3 pivot = t.position + _pivotOff;
            Vector3 pos = pivot + Quaternion.AngleAxis(_angle, Vector3.up) * fwd * _dist + Vector3.up * (_dist * 0.06f);
            _cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(pivot - pos, Vector3.up));
        }

        // ---------------- componentes de entrada ----------------

        sealed class Hold : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler, IPointerExitHandler
        {
            public static readonly Color Idle = new Color(0.8f, 0.8f, 0.8f), Over = Color.white, Press = new Color(0.55f, 0.55f, 0.55f);
            public Image img;
            public bool Down;
            bool _over;
            void Paint() => img.color = Down ? Press : _over ? Over : Idle;
            public void OnPointerDown(PointerEventData e) { Down = true; Paint(); }
            public void OnPointerUp(PointerEventData e) { Down = false; Paint(); }
            public void OnPointerEnter(PointerEventData e) { _over = true; Paint(); }
            public void OnPointerExit(PointerEventData e) { _over = false; Down = false; Paint(); }
        }

        sealed class Drag : MonoBehaviour, IDragHandler
        {
            public System.Action<float> OnDelta;
            public void OnDrag(PointerEventData e) => OnDelta?.Invoke(e.delta.x);
        }

        sealed class Click : MonoBehaviour, IPointerClickHandler
        {
            public System.Action Fn;
            public void OnPointerClick(PointerEventData e) => Fn?.Invoke();
        }
    }
}