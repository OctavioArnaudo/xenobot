using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Runtime.Scripts.Inventory;
using Runtime.Scripts.Gate;

namespace Runtime.Scripts.Narrative
{
    /// <summary>
    /// Mapa (M). Solo dos marcas: el objetivo del StoryManager (parpadea) y el jugador (flecha con su direcci�n).
    /// Texturas y sprites se dibujan por c�digo una sola vez y se comparten (pool est�tico); la UI se crea una vez.
    /// </summary>
    public class StoryMap : MonoBehaviour
    {
        [Tooltip("Vac�o = se calcula solo (terreno o escena) la primera vez que se abre.")] public Bounds worldBounds;
        public float size = 640f;

        const int S = 64;
        const float Pad = 28f;
        static readonly Color Me = new Color(0.45f, 0.95f, 1f), Goal = new Color(0.25f, 1f, 0.4f);
        static Sprite s_disc, s_arrow;

        GameObject _root;
        RectTransform _me, _goal, _halo;
        Image _goalImg, _haloImg;
        bool _open, _shown;
        float _sc;
        Vector3 _c;

        void Awake() => Build();

        void Update()
        {
            var kb = Keyboard.current;
            var sm = StoryManager.Instance;
            var inv = InventoryController.LocalInstance;
            if (kb != null && kb.mKey.wasPressedThisFrame && (_open || (sm != null && !sm.IsBusy && inv != null && MenuGate.TryOpen("map")))) Toggle(!_open);
            if (!_open) return;
            if (sm == null || inv == null) { Toggle(false); return; }

            var pt = inv.transform;
            Place(_me, pt.position);
            _me.localEulerAngles = new Vector3(0f, 0f, -pt.eulerAngles.y);

            var g = sm.Objective;
            bool has = g != null;
            if (has != _shown) { _shown = has; _goal.gameObject.SetActive(has); _halo.gameObject.SetActive(has); }
            if (!has) return;
            Place(_goal, g.position);
            Place(_halo, g.position);
            float p = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f);
            _haloImg.color = new Color(Goal.r, Goal.g, Goal.b, 0.15f + 0.55f * p);
            _halo.localScale = Vector3.one * (1f + 0.9f * p);
            _goalImg.color = new Color(Goal.r, Goal.g, Goal.b, 0.6f + 0.4f * p);
        }

        void OnGate(string k) { if (k == "map" && _open) Toggle(false); }

        void Toggle(bool on)
        {
            _open = on;
            if (!on) MenuGate.Close("map");
            MenuGate.CloseRequested -= OnGate;
            if (on) MenuGate.CloseRequested += OnGate;
            if (on) Fit();
            _root.SetActive(on);
            _shown = false; // fuerza refrescar la marca del objetivo al abrir
            _goal.gameObject.SetActive(false);
            _halo.gameObject.SetActive(false);
        }

        // Tama�o total del mapa: terreno(s) activo(s); si no hay, todos los renderers de la escena. Se calcula una sola vez.
        void Fit()
        {
            if (worldBounds.size == Vector3.zero)
            {
                bool any = false;
                Bounds b = default;
                foreach (var t in Terrain.activeTerrains)
                {
                    var tb = new Bounds(t.transform.position + t.terrainData.size * 0.5f, t.terrainData.size);
                    if (any) b.Encapsulate(tb); else { b = tb; any = true; }
                }
                if (!any)
                    foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                        if (any) b.Encapsulate(r.bounds); else { b = r.bounds; any = true; }
                worldBounds = any ? b : new Bounds(Vector3.zero, new Vector3(1000f, 0f, 1000f));
            }
            _c = worldBounds.center;
            _sc = (size - 2f * Pad) / Mathf.Max(1f, Mathf.Max(worldBounds.size.x, worldBounds.size.z));
        }

        void Place(RectTransform rt, Vector3 w)
        {
            float lim = size * 0.5f - 16f;
            rt.anchoredPosition = new Vector2(Mathf.Clamp((w.x - _c.x) * _sc, -lim, lim), Mathf.Clamp((w.z - _c.z) * _sc, -lim, lim));
        }

        // ---------- UI (una sola vez) ----------

        void Build()
        {
            _root = new GameObject("StoryMapUI", typeof(Canvas), typeof(CanvasScaler));
            _root.transform.SetParent(transform, false);
            var cv = _root.GetComponent<Canvas>();
            cv.renderMode = RenderMode.ScreenSpaceOverlay;
            cv.sortingOrder = 440;
            var cs = _root.GetComponent<CanvasScaler>();
            cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            cs.referenceResolution = new Vector2(1920, 1080);

            var pg = MenuGate.Panel(_root.transform, new Vector2(size, size), 0.6f).gameObject;

            _haloImg = Mark(pg.transform, Disc(), 40f, Goal);
            _goalImg = Mark(pg.transform, Disc(), 18f, Goal);
            var me = Mark(pg.transform, Arrow(), 44f, Me);
            _halo = _haloImg.rectTransform;
            _goal = _goalImg.rectTransform;
            _me = me.rectTransform;
            _root.SetActive(false);
        }

        static Image Mark(Transform parent, Sprite s, float sz, Color c)
        {
            var g = new GameObject("m", typeof(Image));
            g.transform.SetParent(parent, false);
            var im = g.GetComponent<Image>();
            im.sprite = s;
            im.color = c;
            im.raycastTarget = false;
            im.rectTransform.sizeDelta = new Vector2(sz, sz);
            return im;
        }

        // ---------- Texturas dibujadas por c�digo (se generan una vez y se comparten) ----------

        static Sprite Disc()
        {
            if (s_disc != null) return s_disc;
            var px = new Color32[S * S];
            float c = (S - 1) * 0.5f, r = S * 0.5f;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    px[y * S + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(r - d) * 255f));
                }
            return s_disc = Sprite.Create(Tex(px), new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
        }

        // Flecha apuntando hacia arriba (+Z del mundo).
        static Sprite Arrow()
        {
            if (s_arrow != null) return s_arrow;
            Vector2 a = new Vector2(32f, 60f), l = new Vector2(12f, 6f), n = new Vector2(32f, 20f), r = new Vector2(52f, 6f);
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    int cov = 0;
                    for (int i = 0; i < 4; i++)
                    {
                        var p = new Vector2(x + 0.25f + 0.5f * (i & 1), y + 0.25f + 0.5f * (i >> 1));
                        if (In(p, a, l, n) || In(p, a, n, r)) cov++;
                    }
                    px[y * S + x] = new Color32(255, 255, 255, (byte)(cov * 63.75f));
                }
            return s_arrow = Sprite.Create(Tex(px), new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
        }

        static Texture2D Tex(Color32[] px)
        {
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            t.SetPixels32(px);
            t.Apply(false, true);
            return t;
        }

        static bool In(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Sg(p, a, b), d2 = Sg(p, b, c), d3 = Sg(p, c, a);
            return !((d1 < 0f || d2 < 0f || d3 < 0f) && (d1 > 0f || d2 > 0f || d3 > 0f));
        }

        static float Sg(Vector2 p1, Vector2 p2, Vector2 p3) => (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);

    }
}
