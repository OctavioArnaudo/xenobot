using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Events;
using UnityEngine.UI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

namespace Runtime.Scripts.Gate
{
    /// <summary>
    /// Un solo men� a la vez + Esc unificado.
    /// Cada men� pide paso con TryOpen("clave"), se suscribe a CloseRequested mientras est� abierto y libera con Close("clave").
    /// Tambien es el skin unico de UI (metal, lamparas de vidrio, visor verde): Panel, Term, Btn, Lamp, Img, Txt, Fit, Mid.
    /// Con inventario, crafting, stats o mapa abiertos, Esc se consume (el men� de pausa no lo ve) y cierra ese men�.
    /// </summary>
    public static class MenuGate
    {
        static string _owner;
        static bool _esc, _escDown;
        static int _escFrame = -1;

        /// <summary>Esc pulsado con un men� abierto: recibe la clave del men� que debe cerrarse.</summary>
        public static event Action<string> CloseRequested;

        public static bool Busy => _owner != null;

        /// <summary>Hay un men� que Esc debe cerrar (inventario, crafting, stats, mapa).</summary>
        public static bool HasClosableMenu => EscCloses;

        /// <summary>Esc ya se us� este frame para cerrar un men�.</summary>
        public static bool EscConsumed => _escFrame == Time.frameCount;

        /// <summary>Toma el control sin pedir permiso (men� de pausa: va por encima de todo).</summary>
        public static void Take(string key) => _owner = key;

        public static bool TryOpen(string key)
        {
            if (_owner != null && _owner != key) return false;
            _owner = key;
            return true;
        }

        public static void Close(string key)
        {
            if (_owner == key) _owner = null;
        }

        // Solo estos men�s se cierran con Esc. Pausa, di�logos, consolas y panel J gestionan su propio Esc.
        static bool EscCloses => _owner == "inventory" || _owner == "crafting" || _owner == "stats" || _owner == "map";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init()
        {
            _owner = null;
            _esc = _escDown = false;
            CloseRequested = null;
            SceneManager.sceneLoaded -= OnScene;
            SceneManager.sceneLoaded += OnScene;
            InputSystem.onEvent -= OnInput;
            InputSystem.onEvent += OnInput;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Spawn()
        {
            var g = new GameObject("MenuGate") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(g);
            g.AddComponent<Runner>();
        }

        static void OnScene(Scene s, LoadSceneMode m)
        {
            _owner = null;
            CloseRequested = null;
        }

        // Se descarta la pulsaci�n de Esc para todo el juego (incluido el men� de pausa) y se cierra el men� en Update.
        static void OnInput(InputEventPtr e, InputDevice d)
        {
            if (!(d is Keyboard k) || !(e.IsA<StateEvent>() || e.IsA<DeltaStateEvent>())) return;
            if (!k.escapeKey.ReadValueFromEvent(e, out float v)) return;
            if (v <= 0.5f) { _escDown = false; return; }
            if (_escDown) return;
            _escDown = true;
            if (!EscCloses) return;
            _esc = true;
            e.handled = true;
        }

        public static readonly Color Green = new Color(0.2f, 1f, 0.35f), Amber = new Color(0.85f, 0.75f, 0.55f);
        static readonly Dictionary<Vector2Int, Texture2D> s_metal = new Dictionary<Vector2Int, Texture2D>();
        static Sprite s_glass;

        // ---------- Piezas ----------

        /// <summary>Placa metalica. res = resolucion de la textura respecto al tamano.</summary>
        public static RawImage Panel(Transform p, Vector2 size, float res = 1f)
        {
            var g = new GameObject("p", typeof(RawImage));
            g.transform.SetParent(p, false);
            var r = g.GetComponent<RawImage>();
            r.texture = Metal(Mathf.Max(8, (int)(size.x * res)), Mathf.Max(8, (int)(size.y * res)));
            r.raycastTarget = false;
            r.rectTransform.sizeDelta = size;
            return r;
        }

        /// <summary>Visor: marco metalico + titulo + pantalla negra con texto verde (body).</summary>
        public static RectTransform Term(Transform p, Vector2 size, string title, out TextMeshProUGUI body)
        {
            var m = Panel(p, size, 0.5f).rectTransform;
            var t = Txt(m, 30, Amber, TextAlignmentOptions.Center, FontStyles.Bold);
            t.text = title;
            Fit(t.rectTransform, 0f, 1f, 1f, 1f);
            t.rectTransform.offsetMin = new Vector2(0f, -46f);
            t.rectTransform.offsetMax = new Vector2(0f, -4f);
            var s = Rnd(Img(m, new Color(0.01f, 0.03f, 0.02f)));
            Fit(s, 0f, 0f, 1f, 1f);
            s.offsetMin = new Vector2(22f, 22f);
            s.offsetMax = new Vector2(-22f, -52f);
            s.gameObject.AddComponent<Outline>().effectColor = Color.black;
            body = Txt(s, 34, Green, TextAlignmentOptions.TopLeft, FontStyles.Normal);
            Fit(body.rectTransform, 0f, 0f, 1f, 1f, 16f);
            return m;
        }

        /// <summary>Boton: placa metalica (se ilumina al pasar) con pantallita verde.</summary>
        public static RectTransform Btn(Transform p, string label, Vector2 size, UnityAction click)
        {
            var m = Panel(p, size, 0.5f);
            m.raycastTarget = true;
            var b = m.gameObject.AddComponent<Button>();
            b.targetGraphic = m;
            b.onClick.AddListener(click);
            var cb = b.colors;
            cb.normalColor = cb.selectedColor = new Color(0.75f, 0.75f, 0.75f);
            cb.highlightedColor = Color.white;
            cb.pressedColor = new Color(0.5f, 0.5f, 0.5f);
            b.colors = cb;
            var s = Rnd(Img(m.rectTransform, new Color(0.01f, 0.03f, 0.02f)));
            Fit(s, 0f, 0f, 1f, 1f, 8f);
            var t = Txt(s, 28, Green, TextAlignmentOptions.Center, FontStyles.Bold);
            Fit(t.rectTransform, 0f, 0f, 1f, 1f);
            t.text = label;
            return m.rectTransform;
        }

        /// <summary>Lampara de vidrio: ranura hundida + vidrio apagado + vidrio encendido (fill, se recorta con anchorMax.x) + brillo.</summary>
        public static RectTransform Lamp(Transform p, Color c, out RectTransform fill)
        {
            var slot = Rnd(Img(p, new Color(0.02f, 0.03f, 0.03f)), 6);
            slot.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.9f);
            var off = Img(slot, new Color(c.r * 0.18f, c.g * 0.18f, c.b * 0.18f, 1f));
            off.GetComponent<Image>().sprite = Glass();
            Fit(off, 0f, 0f, 1f, 1f, 3f);
            fill = Img(slot, c);
            fill.GetComponent<Image>().sprite = Glass();
            Fit(fill, 0f, 0f, 1f, 1f, 3f);
            Fit(Img(slot, new Color(1f, 1f, 1f, 0.14f)), 0f, 0.55f, 1f, 1f, 4f);
            return slot;
        }

        // ---------- Utilidades uGUI ----------

        public static RectTransform Img(Transform parent, Color col)
        {
            var g = new GameObject("i", typeof(Image));
            g.transform.SetParent(parent, false);
            var im = g.GetComponent<Image>();
            im.color = col;
            im.raycastTarget = false;
            return g.GetComponent<RectTransform>();
        }

        public static TextMeshProUGUI Txt(Transform parent, int size, Color col, TextAlignmentOptions align, FontStyles style)
        {
            var g = new GameObject("t", typeof(TextMeshProUGUI));
            g.transform.SetParent(parent, false);
            var t = g.GetComponent<TextMeshProUGUI>();
            t.fontSize = size;
            t.color = col;
            t.alignment = align;
            t.fontStyle = style;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.raycastTarget = false;
            return t;
        }

        public static void Fit(RectTransform r, float x0, float y0, float x1, float y1, float pad = 0f)
        {
            r.anchorMin = new Vector2(x0, y0);
            r.anchorMax = new Vector2(x1, y1);
            r.offsetMin = new Vector2(pad, pad);
            r.offsetMax = new Vector2(-pad, -pad);
        }

        public static void Mid(RectTransform r, float s)
        {
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = Vector2.zero;
            r.sizeDelta = new Vector2(s, s);
        }

        static readonly Vector3 LightDir = new Vector3(-0.5f, 0.55f, 0.67f).normalized;

        // Curva en S: mas contraste, luces que casi queman y sombras profundas.
        static byte Tone(float c)
        {
            c = Mathf.Clamp01(c);
            return (byte)(Mathf.Lerp(c, c * c * (3f - 2f * c), 0.7f) * 255f);
        }

        // ---------- Skin para menus (una sola textura 9-slice, sin generar nada por tamano) ----------

        static Sprite s_plate;
        static readonly Color Dark = new Color(0.01f, 0.03f, 0.02f, 0.97f);

        static Sprite PlateSprite() => s_plate != null ? s_plate : (s_plate = Sprite.Create(Gen(128, 128, false), new Rect(0, 0, 128, 128), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(40, 40, 40, 40)));

        /// <summary>Placa metalica escalable.</summary>
        public static RectTransform Plate(Transform p, Color? tint = null)
        {
            var r = Img(p, tint ?? Color.white);
            var im = r.GetComponent<Image>();
            im.sprite = PlateSprite();
            im.type = Image.Type.Sliced;
            return r;
        }

        static void SkinImage(Image im, Color tint)
        {
            im.sprite = PlateSprite();
            im.type = Image.Type.Sliced;
            im.color = tint;
        }

        static Image Inset(Transform p, float pad)
        {
            var s = Rnd(Img(p, Dark));
            Fit(s, 0f, 0f, 1f, 1f, pad);
            s.SetAsFirstSibling();
            return s.GetComponent<Image>();
        }

        // Feedback de clic: la pantalla oscura (casi todo el boton) es la que reacciona: apagada -> verde al pasar -> verde intenso al pulsar.
        static void ClickFx(Button b, Image screen)
        {
            screen.color = Color.white; // el ColorBlock multiplica: asi los colores salen tal cual
            b.targetGraphic = screen;
            b.transition = Selectable.Transition.ColorTint;
            var cb = b.colors;
            cb.normalColor = cb.selectedColor = Dark;
            cb.highlightedColor = new Color(0.06f, 0.32f, 0.13f, 1f);
            cb.pressedColor = new Color(0.16f, 0.62f, 0.28f, 1f);
            cb.disabledColor = new Color(0.08f, 0.08f, 0.08f, 0.6f);
            cb.colorMultiplier = 1f;
            cb.fadeDuration = 0.06f;
            b.colors = cb;
        }

        /// <summary>Convierte un menu construido con Images planas al skin metalico: paneles y botones = placa + pantalla oscura, cajas = pantalla, filetes de neon fuera, texto neon = verde/ambar.</summary>
        public static void Reskin(Transform root)
        {
            Canvas.ForceUpdateCanvases();
            var grey = new Color(0.75f, 0.75f, 0.75f);
            foreach (var o in root.GetComponentsInChildren<Outline>(true)) UnityEngine.Object.Destroy(o);
            foreach (var im in root.GetComponentsInChildren<Image>(true))
            {
                if (im == null || im.sprite != null) continue; // portadas, iconos y piezas ya skineadas
                string n = im.name;
                if (n == "T" || n == "B" || n == "L" || n == "R") { UnityEngine.Object.Destroy(im.gameObject); continue; } // filetes de borde
                if (n == "Fill" || n == "Checkmark") { im.color = Green; continue; }
                if (n == "Background" && im.transform.parent == root || n == "DarkOverlay") continue; // fondos
                var sz = im.rectTransform.rect.size;
                if (sz.x > 1700f || sz.y > 950f) continue; // fondos a pantalla completa
                var tr = im.transform;
                if (im.GetComponent<Button>() != null)
                {
                    var c = im.color;
                    SkinImage(im, c.a < 0.05f ? new Color(0.45f, 0.45f, 0.45f) : Color.Lerp(grey, new Color(c.r, c.g, c.b), 0.55f));
                    ClickFx(im.GetComponent<Button>(), Inset(tr, 8f));
                    foreach (var t in tr.GetComponentsInChildren<TMP_Text>(true)) t.color = Green;
                }
                else if (im.GetComponent<TMP_InputField>() != null || Mathf.Min(sz.x, sz.y) < 150f)
                {
                    Rnd(im.rectTransform, 8);
                    im.color = Dark;
                }
                else
                {
                    SkinImage(im, Color.white);
                    Inset(tr, 16f);
                }
            }
            foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
            {
                var c = t.color;
                if (Mathf.Max(c.r, c.g, c.b) - Mathf.Min(c.r, c.g, c.b) > 0.35f) t.color = c.g >= c.r ? Green : Amber;
            }
        }

        // ---------- Texturas por codigo (una vez, compartidas) ----------

        static Sprite Glass()
        {
            if (s_glass != null) return s_glass;
            var t = new Texture2D(2, 32, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 32; y++)
            {
                float u = (y + 0.5f) / 32f, k = (u - 0.7f) / 0.08f;
                float v = Mathf.Clamp01(0.45f + 0.55f * Mathf.Sin(u * Mathf.PI) + 0.3f * Mathf.Exp(-k * k));
                t.SetPixel(0, y, new Color(v, v, v, 1f));
                t.SetPixel(1, y, new Color(v, v, v, 1f));
            }
            t.Apply(false, true);
            return s_glass = Sprite.Create(t, new Rect(0, 0, 2, 32), new Vector2(0.5f, 0.5f), 100f);
        }

        public const int Radius = 14; // esquinas redondeadas (px de textura; en pantalla ~2x)

        static readonly Dictionary<int, Sprite> s_round = new Dictionary<int, Sprite>();

        /// <summary>Sprite 9-slice redondeado (usar con Image.Type.Sliced).</summary>
        public static Sprite Round(int R = 12)
        {
            if (s_round.TryGetValue(R, out var sp) && sp != null) return sp;
            int N = R * 2 + 4;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float cx = Mathf.Clamp(x, R, N - 1 - R), cy = Mathf.Clamp(y, R, N - 1 - R);
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(R - d + 0.5f)));
                }
            t.Apply(false, true);
            return s_round[R] = Sprite.Create(t, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(R, R, R, R));
        }

        public static RectTransform Rnd(RectTransform r, int rad = 12)
        {
            var im = r.GetComponent<Image>();
            im.sprite = Round(rad);
            im.type = Image.Type.Sliced;
            return r;
        }

        public static GameObject Rnd(GameObject g) { Rnd(g.GetComponent<RectTransform>()); return g; }

        /// <summary>Tablero inferior: borde superior curvo (grueso en los costados, estrecho al centro). 1920x224 en el centro 104 de alto.</summary>
        public static RectTransform Dash(Transform p)
        {
            var g = new GameObject("p", typeof(RawImage));
            g.transform.SetParent(p, false);
            var r = g.GetComponent<RawImage>();
            r.texture = Gen(960, 112, true);
            r.raycastTarget = false;
            var rt = r.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(0f, 224f);
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        public static Texture2D Metal(int w, int h) => Gen(w, h, false);

        static float Top(int x, int w) => 52f + 60f * Smooth(0.35f, 0.85f, Mathf.Abs(2f * x / (w - 1) - 1f));

        static Texture2D Gen(int w, int h, bool dash)
        {
            var key = new Vector2Int(w, dash ? -h : h);
            if (s_metal.TryGetValue(key, out var c) && c != null) return c;
            const int Cell = 96;
            int rr = Mathf.Min(Radius, Mathf.Min(w, h) / 4);
            var px = new Color32[w * h];
            var rustA = new Color(0.26f, 0.09f, 0.03f);
            var rustB = new Color(0.80f, 0.36f, 0.08f);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float top = dash ? Top(x, w) : h;
                    float qx = Mathf.Clamp(x, rr, w - 1 - rr);
                    float qy = (dash ? Top((int)qx, w) : h) - 1f - rr;
                    float ey = y > qy ? y - qy : (!dash && y < rr ? rr - y : 0f);
                    float dl = x, dr = w - 1 - x, db = y, dt = top - 1f - y;
                    float e = Mathf.Min(Mathf.Min(Mathf.Min(dl, dr), Mathf.Min(db, dt)), rr - Mathf.Sqrt((x - qx) * (x - qx) + ey * ey));
                    if (e < -0.5f) continue;

                    float fx = x, fy = y;
                    // Metal base: grano, cepillado y ruido (mas amplitud = mas contraste)
                    float m0 = 0.36f + 0.18f * (F(fx / 60f, fy / 60f, 1) - 0.5f) + 0.16f * (V(fx * 0.015f, fy * 1.2f, 5) - 0.5f) + 0.07f * (H(x, y, 3) - 0.5f);

                    // Luz arriba-izquierda: normal exterior del borde mas cercano
                    float mn = Mathf.Min(Mathf.Min(dl, dr), Mathf.Min(db, dt));
                    float ox = mn == dl ? -1f : mn == dr ? 1f : 0f, oy = mn == db ? -1f : mn == dt ? 1f : 0f;
                    float lit = ox * LightDir.x + oy * LightDir.y;

                    float shade = 1f;
                    if (e < 11f) shade *= Mathf.Max(0.15f, 1f + 1.5f * lit * (1f - e / 11f)); // bisel: cara iluminada brilla, la opuesta cae en sombra
                    else if (e < 14.5f) shade *= 0.3f;                                         // ranura interior oscura

                    int lx = x % Cell, ly = y % Cell;
                    bool seam = lx < 4 || ly < 4;
                    if (lx < 2 || ly < 2) shade *= 0.3f; else if (seam) shade *= 1.6f;          // junta: sombra + canto brillante

                    for (int k = 0; k < 4; k++)
                    {
                        float cx = (k & 1) == 0 ? 12f : Cell - 12f, cy = (k & 2) == 0 ? 12f : Cell - 12f;
                        float dx = lx - cx, dy = ly - cy, d = Mathf.Sqrt(dx * dx + dy * dy);
                        if (d < 4.5f)
                        {
                            float nz = Mathf.Sqrt(1f - d * d / 20.25f);
                            float l = Mathf.Max(0f, (dx / 4.5f) * LightDir.x + (dy / 4.5f) * LightDir.y + nz * LightDir.z);
                            shade = 0.3f + 2.1f * l * l;                                        // remache: domo con brillo especular
                        }
                        else if (d < 8f && dx - dy > 0f) shade *= 0.4f;                         // sombra proyectada del remache
                    }

                    shade *= 0.62f + 0.76f * Mathf.Clamp01(y / Mathf.Max(1f, top - 1f));       // degradado vertical: arriba luz, abajo sombra
                    shade *= 0.5f + 0.7f * Smooth(0f, 1f, Mathf.Clamp01(e / 70f));            // vineta: bordes mas oscuros

                    float edge = 1f - Mathf.Clamp01(e / 70f);
                    float rr2 = F(fx / 55f, fy / 55f, 21) + 0.2f * (F(fx / 12f, fy / 12f, 33) - 0.5f) + 0.3f * edge + (seam ? 0.08f : 0f);
                    float mask = Smooth(0.52f, 0.70f, rr2) * 0.95f;
                    var rust = Color.Lerp(rustA, rustB, F(fx / 7f, fy / 7f, 44)) * (0.7f + 0.6f * H(x, y, 9));
                    var col = Color.Lerp(new Color(m0, m0 * 1.02f, m0 * 1.06f), rust, mask) * shade;

                    px[y * w + x] = new Color32(Tone(col.r), Tone(col.g), Tone(col.b), (byte)(Mathf.Clamp01(e + 0.5f) * 255f));
                }
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            t.SetPixels32(px);
            t.Apply(false, true);
            return s_metal[key] = t;
        }

        static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        static float H(int x, int y, int s)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + s * 1274126177);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
            }
        }

        static float V(float x, float y, int s)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = x - xi, fy = y - yi;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            return Mathf.Lerp(Mathf.Lerp(H(xi, yi, s), H(xi + 1, yi, s), fx), Mathf.Lerp(H(xi, yi + 1, s), H(xi + 1, yi + 1, s), fx), fy);
        }

        static float F(float x, float y, int s) =>
            (V(x, y, s) * 0.5f + V(x * 2f, y * 2f, s + 1) * 0.25f + V(x * 4f, y * 4f, s + 2) * 0.125f + V(x * 8f, y * 8f, s + 3) * 0.0625f) / 0.9375f;

        // Texturas que usa la UI: se generan una por frame al arrancar (detras del menu principal), nunca en mitad de la partida.
        static readonly Vector2Int[] Warm =
        {
            new Vector2Int(128, 128),  // placa 9-slice compartida por todos los menus
            new Vector2Int(512, 400),  // inventario / crafteo (IMGUI)
            new Vector2Int(960, -112), // tablero HUD (negativo = dash)
            new Vector2Int(380, 210),  // panel stats
            new Vector2Int(400, 190), new Vector2Int(360, 100), new Vector2Int(550, 300) // historia: log, dialogo, misiones
        };

        [DefaultExecutionOrder(-1000)]
        sealed class Runner : MonoBehaviour
        {
            IEnumerator Start()
            {
                foreach (var v in Warm)
                {
                    yield return null;
                    Gen(v.x, Mathf.Abs(v.y), v.y < 0);
                }
                yield return PanelSkin.WarmAll(); // marcos low-poly del panel de personaje
            }

            void Update()
            {
                if (!_esc) return;
                _esc = false;
                _escFrame = Time.frameCount;
                if (_owner != null) CloseRequested?.Invoke(_owner);
            }
        }
    }
}