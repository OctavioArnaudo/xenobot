using System.Collections;
using UnityEngine;

namespace Runtime.Scripts.Gate
{
    /// <summary>
    /// Marcos low-poly con la skin de metal oxidado. Poligonos facetados (triangulos con luz plana),
    /// biseles iluminados desde arriba-izquierda y hueco oscuro interior. Se generan una sola vez
    /// (MenuGate los precarga al arrancar, un sprite por frame) y se comparten.
    /// </summary>
    public static class PanelSkin
    {
        public enum Kind { Inventory, Viewer, Stats, Skills, Arrow, Card }

        struct Def { public int w, h; public float cell; public int seed; public Vector2[] outer, inner; }

        static readonly Sprite[] s_sprites = new Sprite[6];
        static readonly Vector3 L = new Vector3(-0.5f, 0.55f, 0.67f).normalized;
        static readonly Vector2 L2 = new Vector2(-0.67f, 0.74f);

        // Poligonos en coordenadas normalizadas (x derecha, y ARRIBA).
        static Def GetDef(Kind k)
        {
            switch (k)
            {
                case Kind.Inventory:
                    return new Def
                    {
                        w = 282,
                        h = 432,
                        cell = 40f,
                        seed = 11,
                        outer = P(0.02f, 0.97f, 0.55f, 1f, 0.98f, 0.98f, 1f, 0.5f, 0.97f, 0.02f, 0.45f, 0f, 0.02f, 0.03f, 0f, 0.5f),
                        inner = P(0.11f, 0.875f, 0.89f, 0.885f, 0.9f, 0.085f, 0.1f, 0.075f)
                    };
                case Kind.Viewer:
                    {
                        var o = new Vector2[16];
                        for (int i = 0; i < 16; i++)
                        {
                            float a = i * Mathf.PI * 2f / 16f, r = 0.94f + 0.06f * Hash(i, 3, 5);
                            o[i] = new Vector2(0.5f + 0.5f * r * Mathf.Cos(a), 0.5f + 0.5f * r * Mathf.Sin(a));
                        }
                        return new Def
                        {
                            w = 240,
                            h = 480,
                            cell = 42f,
                            seed = 23,
                            outer = o,
                            // hueco: rectangulo con las esquinas cortadas (se lee como ovalo facetado)
                            inner = P(0.33f, 0.92f, 0.67f, 0.92f, 0.83f, 0.845f, 0.83f, 0.315f, 0.67f, 0.24f, 0.33f, 0.24f, 0.17f, 0.315f, 0.17f, 0.845f)
                        };
                    }
                case Kind.Stats:
                    return new Def
                    {
                        w = 282,
                        h = 234,
                        cell = 38f,
                        seed = 37,
                        outer = P(0f, 0.96f, 0.5f, 1f, 1f, 0.97f, 0.99f, 0.5f, 1f, 0.03f, 0.5f, 0f, 0.01f, 0.04f, 0.02f, 0.5f),
                        inner = P(0.09f, 0.8f, 0.92f, 0.81f, 0.91f, 0.09f, 0.1f, 0.08f)
                    };
                case Kind.Skills:
                    return new Def
                    {
                        w = 282,
                        h = 234,
                        cell = 38f,
                        seed = 53,
                        outer = P(0.02f, 0.97f, 0.5f, 1f, 1f, 0.96f, 1f, 0.5f, 0.98f, 0.04f, 0.5f, 0f, 0f, 0.03f, 0.01f, 0.5f),
                        inner = P(0.1f, 0.8f, 0.91f, 0.79f, 0.92f, 0.08f, 0.09f, 0.09f)
                    };
                case Kind.Card: // tarjeta del objeto seleccionado: misma chapa oxidada y hueco negro que el inventario
                    return new Def
                    {
                        w = 204,
                        h = 270,
                        cell = 36f,
                        seed = 89,
                        outer = P(0.02f, 0.97f, 0.55f, 1f, 0.98f, 0.98f, 1f, 0.5f, 0.97f, 0.02f, 0.45f, 0f, 0.02f, 0.03f, 0f, 0.5f),
                        inner = P(0.075f, 0.925f, 0.925f, 0.93f, 0.93f, 0.07f, 0.07f, 0.065f)
                    };
                default: // flecha hacia la derecha (la izquierda es la misma con escala X negativa)
                    return new Def
                    {
                        w = 96,
                        h = 56,
                        cell = 14f,
                        seed = 71,
                        outer = P(0f, 0.7f, 0.55f, 0.7f, 0.55f, 0.95f, 1f, 0.5f, 0.55f, 0.05f, 0.55f, 0.3f, 0f, 0.3f)
                    };
            }
        }

        static Vector2[] P(params float[] v)
        {
            var p = new Vector2[v.Length / 2];
            for (int i = 0; i < p.Length; i++) p[i] = new Vector2(v[2 * i], v[2 * i + 1]);
            return p;
        }

        public static Sprite Get(Kind k)
        {
            int i = (int)k;
            if (s_sprites[i] != null) return s_sprites[i];
            var d = GetDef(k);
            var t = Gen(d);
            return s_sprites[i] = Sprite.Create(t, new Rect(0, 0, d.w, d.h), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>Precarga: un marco por frame, para no generar nada en mitad de la partida.</summary>
        public static IEnumerator WarmAll()
        {
            for (int i = 0; i < s_sprites.Length; i++)
            {
                yield return null;
                Get((Kind)i);
            }
        }

        // ---------- generacion ----------

        static Texture2D Gen(Def d)
        {
            int w = d.w, h = d.h;
            var px = new Color32[w * h];
            var po = Scale(d.outer, w, h);
            var pi = d.inner != null ? Scale(d.inner, w, h) : null;
            float so = Area(po) > 0f ? 1f : -1f, si = pi != null ? (Area(pi) > 0f ? 1f : -1f) : 1f;
            var rustA = new Color(0.26f, 0.09f, 0.03f);
            var rustB = new Color(0.80f, 0.36f, 0.08f);

            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    if (!Inside(po, p)) continue;

                    float dO = Edge(po, so, p, out Vector2 nO);
                    float fs = Facet(p, d.cell, d.seed, out float fe);
                    Color col;

                    bool rec = pi != null && Inside(pi, p);
                    if (rec)
                    {
                        float dI = Edge(pi, si, p, out Vector2 nI);                  // nI apunta fuera del hueco
                        float lit = Vector2.Dot(-nI, L2);                            // la pared interior mira hacia dentro
                        float wall = 1f + 1.1f * lit * (1f - Mathf.Clamp01(dI / 18f));
                        float depth = 0.35f + 0.65f * Smooth(Mathf.Clamp01(dI / 20f));
                        col = new Color(0.02f, 0.03f, 0.05f) * (0.55f + 0.45f * fs) * depth * Mathf.Max(0.3f, wall);
                    }
                    else
                    {
                        float shade = fs;
                        if (fe < 0.9f) shade *= 0.55f;                               // arista entre facetas
                        if (dO < 8f) shade *= Mathf.Max(0.15f, 1f + 1.5f * Vector2.Dot(nO, L2) * (1f - dO / 8f));
                        if (pi != null)
                        {
                            float dI = Edge(pi, si, p, out Vector2 nI);
                            if (dI < 8f) shade *= Mathf.Max(0.15f, 1f + 1.5f * Vector2.Dot(-nI, L2) * (1f - dI / 8f));
                        }
                        shade *= 0.5f + 0.7f * Smooth(Mathf.Clamp01(dO / 40f));      // vineta hacia los bordes

                        float m0 = 0.38f + 0.2f * (Noise(p.x / 38f, p.y / 38f, 1) - 0.5f) + 0.12f * (Noise(p.x * 0.02f, p.y * 0.9f, 5) - 0.5f);
                        float rm = Noise(p.x / 46f, p.y / 46f, 21) + 0.25f * (Noise(p.x / 10f, p.y / 10f, 33) - 0.5f) + 0.3f * (1f - Mathf.Clamp01(dO / 40f));
                        float mask = Smoothstep(0.52f, 0.72f, rm) * 0.95f;
                        var rust = Color.Lerp(rustA, rustB, Noise(p.x / 7f, p.y / 7f, 44)) * (0.7f + 0.6f * Hash((int)p.x, (int)p.y, 9));
                        col = Color.Lerp(new Color(m0, m0 * 1.02f, m0 * 1.06f), rust, mask) * shade;
                    }

                    px[y * w + x] = new Color32(Tone(col.r), Tone(col.g), Tone(col.b), (byte)(Mathf.Clamp01(dO + 0.5f) * 255f));
                }

            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            t.SetPixels32(px);
            t.Apply(false, true);
            return t;
        }

        // Triangulacion con puntos jitterizados: cada triangulo es una faceta con su propia inclinacion.
        static readonly int[] OX = { 0, -1, 1, 0, 0, -1, 1, -1, 1 };
        static readonly int[] OY = { 0, 0, 0, -1, 1, -1, -1, 1, 1 };

        static float Facet(Vector2 p, float c, int seed, out float edge)
        {
            int cx = Mathf.FloorToInt(p.x / c), cy = Mathf.FloorToInt(p.y / c);
            for (int k = 0; k < 9; k++)
            {
                int i = cx + OX[k], j = cy + OY[k];
                Vector2 a = Pt(i, j, c, seed), b = Pt(i + 1, j, c, seed), cc = Pt(i + 1, j + 1, c, seed), dd = Pt(i, j + 1, c, seed);
                bool diag = Hash(i, j, seed + 1) > 0.5f;
                for (int t = 0; t < 2; t++)
                {
                    Vector2 t0, t1, t2;
                    if (diag) { if (t == 0) { t0 = a; t1 = b; t2 = cc; } else { t0 = a; t1 = cc; t2 = dd; } }
                    else { if (t == 0) { t0 = a; t1 = b; t2 = dd; } else { t0 = b; t1 = cc; t2 = dd; } }
                    if (!Tri(t0, t1, t2, p, out edge)) continue;

                    float nx = (Hash(i, j, seed + 3 + t * 11) - 0.5f) * 1.6f, ny = (Hash(i, j, seed + 5 + t * 13) - 0.5f) * 1.6f;
                    var n = new Vector3(nx, ny, 1f).normalized;
                    float lit = Mathf.Max(0f, Vector3.Dot(n, L));
                    return 0.3f + 1.7f * Mathf.Pow(lit, 1.2f);                       // facetas muy contrastadas
                }
            }
            edge = 99f;
            return 1f;
        }

        static Vector2 Pt(int i, int j, float c, int seed) =>
            new Vector2(i * c + (Hash(i, j, seed) - 0.5f) * c * 0.7f, j * c + (Hash(i, j, seed + 7) - 0.5f) * c * 0.7f);

        static bool Tri(Vector2 a, Vector2 b, Vector2 c, Vector2 p, out float e)
        {
            float d1 = Cr(a, b, p), d2 = Cr(b, c, p), d3 = Cr(c, a, p);
            e = 0f;
            if ((d1 < 0f || d2 < 0f || d3 < 0f) && (d1 > 0f || d2 > 0f || d3 > 0f)) return false;
            e = Mathf.Min(Mathf.Min(Mathf.Abs(d1) / (b - a).magnitude, Mathf.Abs(d2) / (c - b).magnitude), Mathf.Abs(d3) / (a - c).magnitude);
            return true;
        }

        static float Cr(Vector2 a, Vector2 b, Vector2 p) => (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);

        static Vector2[] Scale(Vector2[] n, int w, int h)
        {
            var r = new Vector2[n.Length];
            for (int i = 0; i < n.Length; i++) r[i] = new Vector2(n[i].x * w, n[i].y * h);
            return r;
        }

        static float Area(Vector2[] p)
        {
            float a = 0f;
            for (int i = 0; i < p.Length; i++) { var u = p[i]; var v = p[(i + 1) % p.Length]; a += u.x * v.y - v.x * u.y; }
            return a * 0.5f;
        }

        static bool Inside(Vector2[] p, Vector2 q)
        {
            bool c = false;
            for (int i = 0, j = p.Length - 1; i < p.Length; j = i++)
                if ((p[i].y > q.y) != (p[j].y > q.y) && q.x < (p[j].x - p[i].x) * (q.y - p[i].y) / (p[j].y - p[i].y) + p[i].x) c = !c;
            return c;
        }

        // Distancia al borde del poligono y normal exterior de la arista mas cercana.
        static float Edge(Vector2[] p, float sgn, Vector2 q, out Vector2 n)
        {
            float best = float.MaxValue;
            n = Vector2.up;
            for (int i = 0; i < p.Length; i++)
            {
                Vector2 a = p[i], ab = p[(i + 1) % p.Length] - a;
                float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / Vector2.Dot(ab, ab));
                float d = (q - (a + ab * t)).magnitude;
                if (d < best) { best = d; n = new Vector2(ab.y, -ab.x).normalized * sgn; }
            }
            return best;
        }

        static float Hash(int x, int y, int s)
        {
            unchecked
            {
                int n = x * 374761393 + y * 668265263 + s * 1274126177;
                n = (n ^ (n >> 13)) * 1274126177;
                n ^= n >> 16;
                return (n & 0xffff) / 65535f;
            }
        }

        static float Noise(float x, float y, int s)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float tx = Smooth(x - xi), ty = Smooth(y - yi);
            return Mathf.Lerp(Mathf.Lerp(Hash(xi, yi, s), Hash(xi + 1, yi, s), tx), Mathf.Lerp(Hash(xi, yi + 1, s), Hash(xi + 1, yi + 1, s), tx), ty);
        }

        static float Smooth(float t) => t * t * (3f - 2f * t);
        static float Smoothstep(float a, float b, float v) => Smooth(Mathf.Clamp01((v - a) / (b - a)));

        // Curva en S: luces fuertes y sombras profundas.
        static byte Tone(float c)
        {
            c = Mathf.Clamp01(c);
            return (byte)(Mathf.Lerp(c, c * c * (3f - 2f * c), 0.7f) * 255f);
        }
    }
}