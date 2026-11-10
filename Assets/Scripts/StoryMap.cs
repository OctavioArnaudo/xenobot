using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Runtime.Scripts.Inventory;
using Runtime.Scripts.Gate;

namespace Runtime.Scripts.Narrative
{
    /// <summary>
    /// RADAR. Panel anexado a la derecha del panel de misiones (J): se abre y se cierra con el.
    /// Solo dos marcas: el objetivo del StoryManager (parpadea) y el jugador (flecha con su direccion).
    /// Sprites dibujados por codigo una sola vez y compartidos; la UI se construye una vez y no hay Canvas propio.
    /// </summary>
    public class StoryMap : MonoBehaviour
    {
        public const float PanelW = 440f, PanelH = 600f;

        [Tooltip("Vacio = se calcula solo (terreno o escena) la primera vez que se abre.")] public Bounds worldBounds;

        const int S = 64;
        const float Side = 392f;
        static readonly Color Me = new Color(0.45f, 0.95f, 1f), Goal = new Color(0.25f, 1f, 0.4f);
        static Sprite s_disc, s_arrow;

        RectTransform _me, _goal, _halo;
        Image _goalImg, _haloImg;
        bool _built, _was, _shown;
        float _sc;
        Vector3 _c;

        void Update()
        {
            var sm = StoryManager.Instance;
            var inv = InventoryController.LocalInstance;
            if (sm == null || inv == null || !sm.MissionOpen) { _was = false; return; }
            if (!_built)
            {
                var host = sm.MissionPanel;
                if (host == null) return;
                Build(host);
            }
            if (!_was)
            {
                _was = true;
                _shown = false; // fuerza refrescar la marca del objetivo al abrir
                _goal.gameObject.SetActive(false);
                _halo.gameObject.SetActive(false);
                Fit();
            }

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

        // Area total del radar: terreno(s) activo(s); si no hay, todos los renderers de la escena. Se calcula una sola vez.
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
            _sc = (Side - 28f) / Mathf.Max(1f, Mathf.Max(worldBounds.size.x, worldBounds.size.z));
        }

        void Place(RectTransform rt, Vector3 w)
        {
            float lim = Side * 0.5f - 16f;
            rt.anchoredPosition = new Vector2(Mathf.Clamp((w.x - _c.x) * _sc, -lim, lim), Mathf.Clamp((w.z - _c.z) * _sc, -lim, lim));
        }

        // ---------- UI (una sola vez, hija del panel de misiones) ----------

        void Build(Transform host)
        {
            _built = true;
            var rt = MenuGate.Panel(host, new Vector2(PanelW, PanelH), 0.6f).GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(-12f, 0f); // se solapa con el marco: se lee como una sola pieza

            var scr = Img(rt, new Color(0.02f, 0.05f, 0.04f), new Vector2(PanelW - 32f, PanelH - 24f));

            Top(Label(scr, 30, "RADAR", TextAlignmentOptions.Left), 16f, 44f);
            Top(Label(scr, 20, "<color=#73F2FF>TU</color>      <color=#40FF66>OBJETIVO</color>", TextAlignmentOptions.Center), 514f, 36f);

            var map = Img(scr, new Color(0f, 0.025f, 0.015f), new Vector2(Side, Side));
            map.anchoredPosition = new Vector2(0f, -8f);

            _haloImg = Mark(map, Disc(), 40f, Goal);
            _goalImg = Mark(map, Disc(), 18f, Goal);
            var me = Mark(map, Arrow(), 44f, Me);
            _halo = _haloImg.rectTransform;
            _goal = _goalImg.rectTransform;
            _me = me.rectTransform;
        }

        static RectTransform Img(Transform parent, Color c, Vector2 size)
        {
            var g = new GameObject("b", typeof(Image));
            g.transform.SetParent(parent, false);
            var im = g.GetComponent<Image>();
            im.color = c;
            im.raycastTarget = false;
            im.rectTransform.sizeDelta = size;
            return im.rectTransform;
        }

        static TextMeshProUGUI Label(Transform parent, int size, string text, TextAlignmentOptions a)
        {
            var g = new GameObject("t", typeof(TextMeshProUGUI));
            g.transform.SetParent(parent, false);
            var t = g.GetComponent<TextMeshProUGUI>();
            t.fontSize = size;
            t.color = MenuGate.Green;
            t.alignment = a;
            t.fontStyle = FontStyles.Bold;
            t.raycastTarget = false;
            t.text = text;
            return t;
        }

        // Franja horizontal anclada arriba (margen lateral 20).
        static void Top(TextMeshProUGUI t, float y, float h)
        {
            var r = t.rectTransform;
            r.anchorMin = new Vector2(0f, 1f);
            r.anchorMax = new Vector2(1f, 1f);
            r.pivot = new Vector2(0.5f, 1f);
            r.anchoredPosition = new Vector2(0f, -y);
            r.sizeDelta = new Vector2(-40f, h);
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

        // ---------- Texturas dibujadas por codigo (se generan una vez y se comparten) ----------

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