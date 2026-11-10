// Poner en una carpeta "Editor" (Assets/Editor/TerrainTools.cs). Menú: Tools/Terrain
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

static class TerrainTools
{
    static Terrain[] Selected() => Selection.GetFiltered<Terrain>(SelectionMode.Deep);

    // 1) Todos los terrenos: distancias de dibujo + informe de por qué los árboles pueden no verse.
    [MenuItem("Tools/Terrain/1 Fix Tree Distance + Report")]
    static void Fix()
    {
        var sb = new StringBuilder($"[Terrain] lodBias={QualitySettings.lodBias}\n");
        foreach (var cam in Camera.allCameras) sb.AppendLine($"Camara '{cam.name}' far={cam.farClipPlane}");

        foreach (var t in Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None))
        {
            Undo.RecordObject(t, "Fix Tree Distance");
            t.drawTreesAndFoliage = true;
            t.treeDistance = 900f;
            t.treeBillboardDistance = 250f;
            t.treeCrossFadeLength = 20f;
            t.treeMaximumFullLODCount = 150;
            t.detailObjectDistance = 70f;
            t.detailObjectDensity = 0.5f;
            t.basemapDistance = 120f;
            t.heightmapPixelError = 8f;
            t.drawInstanced = true;
            t.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            EditorUtility.SetDirty(t);

            var td = t.terrainData;
            sb.AppendLine($"{t.name}: {td.treeInstanceCount} arboles");
            var counts = td.treeInstances.GroupBy(i => i.prototypeIndex).ToDictionary(g => g.Key, g => g.Count());
            for (int i = 0; i < td.treePrototypes.Length; i++)
            {
                var go = td.treePrototypes[i].prefab;
                if (go == null) { sb.AppendLine($"  [{i}] PREFAB NULO (invisible)"); continue; }
                var shaders = go.GetComponentsInChildren<Renderer>().SelectMany(r => r.sharedMaterials)
                    .Where(m => m != null).Select(m => m.shader.name).Distinct();
                sb.AppendLine($"  [{i}] {go.name} x{(counts.TryGetValue(i, out var c) ? c : 0)} Tree={go.GetComponent<Tree>() != null} LOD={go.GetComponent<LODGroup>() != null} shaders={string.Join(",", shaders)}");
            }
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
        Debug.Log(sb.ToString());
    }

    // 2) Borra TODOS los árboles de los terrenos seleccionados (seleccionar TerrainsPrefab sirve).
    [MenuItem("Tools/Terrain/2 Clear Trees (seleccion)")]
    static void ClearTrees()
    {
        var ts = Selected();
        if (ts.Length == 0) { Debug.LogWarning("[Terrain] Selecciona uno o varios terrenos."); return; }
        foreach (var t in ts)
        {
            Undo.RegisterCompleteObjectUndo(t.terrainData, "Clear Trees");
            t.terrainData.SetTreeInstances(new TreeInstance[0], true);
        }
        Debug.Log($"[Terrain] Arboles borrados en {ts.Length} terrenos.");
    }

    // 3) Pasto automático por pendiente y ruido (textura de pasto generada una sola vez).
    [MenuItem("Tools/Terrain/3 Fill Grass (seleccion)")]
    static void Grass()
    {
        var ts = Selected();
        if (ts.Length == 0) { Debug.LogWarning("[Terrain] Selecciona uno o varios terrenos."); return; }

        var proto = new DetailPrototype
        {
            prototypeTexture = GrassTex(),
            renderMode = DetailRenderMode.GrassBillboard,
            healthyColor = new Color(0.45f, 0.62f, 0.25f),
            dryColor = new Color(0.62f, 0.5f, 0.25f),
            minWidth = 1f,
            maxWidth = 1.6f,
            minHeight = 0.8f,
            maxHeight = 1.4f,
            noiseSpread = 12f
        };

        for (int k = 0; k < ts.Length; k++)
        {
            var t = ts[k];
            var td = t.terrainData;
            EditorUtility.DisplayProgressBar("Pasto", t.name, (float)k / ts.Length);
            Undo.RegisterCompleteObjectUndo(td, "Fill Grass");
            td.detailPrototypes = new[] { proto };

            int n = td.detailWidth;
            var layer = new int[n, n];
            Vector3 p = t.transform.position;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                    if (td.GetSteepness(u, v) > 35f) continue;
                    float noise = Mathf.PerlinNoise((p.x + u * td.size.x) * 0.02f, (p.z + v * td.size.z) * 0.02f);
                    layer[y, x] = Mathf.RoundToInt(Mathf.Clamp01((noise - 0.3f) * 3f) * 12f);
                }
            td.SetDetailLayer(0, 0, 0, layer);
        }
        EditorUtility.ClearProgressBar();
    }

    // 4) Une los bordes de TODOS los terrenos de la escena (no depende de la selección).
    //    Detecta vecinos por solapamiento (cualquier tamaño/resolución), alinea posiciones, iguala la altura
    //    mundial del borde y suaviza una franja. Escribe un informe completo en la Console.
    [MenuItem("Tools/Terrain/4 Stitch Edges (todos)")]
    static void Stitch()
    {
        const float MaxGap = 200f;       // hueco máximo entre terrenos que se sigue considerando vecino
        const int Band = 32, Passes = 3; // franja suavizada (muestras) y pasadas para esquinas

        var ts = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None);
        int n = ts.Length;
        var sb = new StringBuilder($"[Terrain] Stitch: {n} terrenos\n");
        var right = Fill(n); var up = Fill(n); var left = Fill(n); var down = Fill(n);

        for (int i = 0; i < n; i++)
        {
            Vector3 a = ts[i].transform.position, s = ts[i].terrainData.size;
            sb.AppendLine($"  {Id(ts[i])}: pos=({a.x:F1},{a.y:F1},{a.z:F1}) size=({s.x:F0},{s.y:F0},{s.z:F0}) res={ts[i].terrainData.heightmapResolution}");
            float bestR = MaxGap, bestU = MaxGap;
            for (int j = 0; j < n; j++)
            {
                if (i == j) continue;
                Vector3 b = ts[j].transform.position, t = ts[j].terrainData.size;
                float gx = b.x - (a.x + s.x), gz = b.z - (a.z + s.z);
                float ovZ = Mathf.Min(a.z + s.z, b.z + t.z) - Mathf.Max(a.z, b.z);
                float ovX = Mathf.Min(a.x + s.x, b.x + t.x) - Mathf.Max(a.x, b.x);
                if (ovZ > 0.5f * Mathf.Min(s.z, t.z) && Mathf.Abs(gx) <= bestR) { bestR = Mathf.Abs(gx); right[i] = j; }
                if (ovX > 0.5f * Mathf.Min(s.x, t.x) && Mathf.Abs(gz) <= bestU) { bestU = Mathf.Abs(gz); up[i] = j; }
            }
            if (right[i] >= 0) left[right[i]] = i;
            if (up[i] >= 0) down[up[i]] = i;
        }

        for (int i = 0; i < n; i++)
        {
            if (right[i] >= 0) sb.AppendLine($"  X: {Id(ts[i])} -> {Id(ts[right[i]])} hueco={ts[right[i]].transform.position.x - (ts[i].transform.position.x + ts[i].terrainData.size.x):F2}m");
            if (up[i] >= 0) sb.AppendLine($"  Z: {Id(ts[i])} -> {Id(ts[up[i]])} hueco={ts[up[i]].transform.position.z - (ts[i].transform.position.z + ts[i].terrainData.size.z):F2}m");
            if (right[i] < 0 && up[i] < 0 && left[i] < 0 && down[i] < 0) sb.AppendLine($"  SIN VECINOS: {Id(ts[i])} (a mas de {MaxGap}m o sin solape)");
        }

        // 1) Posiciones exactas (solo si los tamaños coinciden), en orden de grilla.
        foreach (int i in Enumerable.Range(0, n).OrderBy(k => ts[k].transform.position.z).ThenBy(k => ts[k].transform.position.x))
        {
            Vector3 a = ts[i].transform.position, s = ts[i].terrainData.size;
            if (right[i] >= 0 && SameSize(ts[i], ts[right[i]])) Snap(ts[right[i]], a.x + s.x, a.z);
            if (up[i] >= 0 && SameSize(ts[i], ts[up[i]])) Snap(ts[up[i]], a.x, a.z + s.z);
        }

        // 2) Alturas.
        var H = new float[n][,];
        for (int i = 0; i < n; i++)
        {
            var td = ts[i].terrainData;
            Undo.RegisterCompleteObjectUndo(td, "Stitch Terrains");
            H[i] = td.GetHeights(0, 0, td.heightmapResolution, td.heightmapResolution);
        }
        for (int pass = 0; pass < Passes; pass++)
            for (int i = 0; i < n; i++)
            {
                if (right[i] >= 0) { int j = right[i]; Pull(ts[i], H[i], ts[j], H[j], 0, Band); Pull(ts[j], H[j], ts[i], H[i], 1, Band); }
                if (up[i] >= 0) { int j = up[i]; Pull(ts[i], H[i], ts[j], H[j], 2, Band); Pull(ts[j], H[j], ts[i], H[i], 3, Band); }
            }
        for (int i = 0; i < n; i++)
        {
            ts[i].terrainData.SetHeights(0, 0, H[i]);
            ts[i].groupingID = 1;
            ts[i].allowAutoConnect = true;
        }

        // 3) Vecinos.
        for (int i = 0; i < n; i++)
            ts[i].SetNeighbors(left[i] >= 0 ? ts[left[i]] : null, up[i] >= 0 ? ts[up[i]] : null,
                               right[i] >= 0 ? ts[right[i]] : null, down[i] >= 0 ? ts[down[i]] : null);

        UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
        Debug.Log(sb.ToString());
    }

    static int[] Fill(int n) { var a = new int[n]; for (int i = 0; i < n; i++) a[i] = -1; return a; }
    static string Id(Terrain t) => $"Terrain#{t.transform.GetSiblingIndex()}";
    static bool SameSize(Terrain a, Terrain b) => (a.terrainData.size - b.terrainData.size).sqrMagnitude < 0.01f;

    static void Snap(Terrain t, float x, float z)
    {
        Undo.RecordObject(t.transform, "Stitch Terrains");
        t.transform.position = new Vector3(x, t.transform.position.y, z);
    }

    static float Sample(float[,] H, float u, float v)
    {
        int r = H.GetLength(0);
        float x = Mathf.Clamp01(u) * (r - 1), y = Mathf.Clamp01(v) * (r - 1);
        int x0 = (int)x, y0 = (int)y, x1 = Mathf.Min(x0 + 1, r - 1), y1 = Mathf.Min(y0 + 1, r - 1);
        float fx = x - x0, fy = y - y0;
        return Mathf.Lerp(Mathf.Lerp(H[y0, x0], H[y0, x1], fx), Mathf.Lerp(H[y1, x0], H[y1, x1], fx), fy);
    }

    // Lleva el borde de 'a' (side 0=derecha 1=izquierda 2=arriba 3=abajo) a la altura mundial media con 'b',
    // y reparte el ajuste en una franja con caída suave.
    static void Pull(Terrain a, float[,] A, Terrain b, float[,] B, int side, int band)
    {
        int ra = A.GetLength(0);
        Vector3 pa = a.transform.position, sa = a.terrainData.size, pb = b.transform.position, sb = b.terrainData.size;
        bool alongZ = side < 2;
        for (int k = 0; k < ra; k++)
        {
            float f = k / (ra - 1f);
            float u = alongZ ? (side == 0 ? 1f : 0f) : f;
            float v = alongZ ? f : (side == 2 ? 1f : 0f);
            float bu = (pa.x + u * sa.x - pb.x) / sb.x, bv = (pa.z + v * sa.z - pb.z) / sb.z;
            float along = alongZ ? bv : bu;
            if (along < -0.001f || along > 1.001f) continue;

            float ha = pa.y + Sample(A, u, v) * sa.y;
            float hb = pb.y + Sample(B, bu, bv) * sb.y;
            float delta = (hb - ha) * 0.5f / sa.y;
            for (int i = 0; i < band; i++)
            {
                float t = 1f - (float)i / band, w = t * t * (3f - 2f * t);
                int y = side == 2 ? ra - 1 - i : side == 3 ? i : k;
                int x = side == 0 ? ra - 1 - i : side == 1 ? i : k;
                A[y, x] = Mathf.Clamp01(A[y, x] + delta * w);
            }
        }
    }

    // 5) Pinta TODOS los terrenos con 4 capas (pasto, barro oscuro, tierra, tierra seca clara) según altura,
    //    pendiente y curvatura: valles/bajos oscuros, crestas claras, laderas de tierra. Continuo entre planchas.
    //    Las texturas se generan una vez en Assets/Textures/TerrainPaint (puedes reemplazarlas por las tuyas).
    const float CavGain = 10f;                    // sensibilidad a valles/crestas
    const float SlopeMin = 18f, SlopeMax = 42f;   // grados: de pasto a tierra de ladera

    [MenuItem("Tools/Terrain/5 Paint Terrain (todos)")]
    static void Paint()
    {
        var layers = Layers();
        var ts = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None);
        int n = ts.Length;
        var H = new float[n][,];
        float lo = float.MaxValue, hi = float.MinValue;
        for (int i = 0; i < n; i++)
        {
            var td = ts[i].terrainData;
            int r = td.heightmapResolution, st = Mathf.Max(1, r / 64);
            H[i] = td.GetHeights(0, 0, r, r);
            for (int y = 0; y < r; y += st)
                for (int x = 0; x < r; x += st)
                {
                    float wy = ts[i].transform.position.y + H[i][y, x] * td.size.y;
                    lo = Mathf.Min(lo, wy); hi = Mathf.Max(hi, wy);
                }
        }

        const float D = 2f;
        for (int i = 0; i < n; i++)
        {
            var t = ts[i]; var td = t.terrainData; var A = H[i];
            EditorUtility.DisplayProgressBar("Pintando terreno", t.name, (float)i / n);
            Undo.RegisterCompleteObjectUndo(td, "Paint Terrain");
            td.terrainLayers = layers;

            int res = td.alphamapResolution;
            var map = new float[res, res, 4];
            Vector3 p = t.transform.position, sz = td.size;
            float eu = D / sz.x, ev = D / sz.z;
            for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                {
                    float u = (x + 0.5f) / res, v = (y + 0.5f) / res;
                    float h = Sample(A, u, v), hl = Sample(A, u - eu, v), hr = Sample(A, u + eu, v),
                          hd = Sample(A, u, v - ev), hu = Sample(A, u, v + ev);
                    float gx = (hr - hl) * sz.y / (2f * D), gz = (hu - hd) * sz.y / (2f * D);
                    float slope = Mathf.Atan(Mathf.Sqrt(gx * gx + gz * gz)) * Mathf.Rad2Deg;
                    float cav = Mathf.Clamp((hl + hr + hd + hu - 4f * h) * sz.y / (D * D) * CavGain, -1f, 1f);

                    float wx = p.x + u * sz.x, wz = p.z + v * sz.z;
                    float hn = Mathf.InverseLerp(lo, hi, p.y + h * sz.y);
                    float n1 = Mathf.PerlinNoise(wx * 0.012f + 17f, wz * 0.012f), n2 = Mathf.PerlinNoise(wx * 0.06f, wz * 0.06f + 31f);
                    float sl = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(SlopeMin, SlopeMax, slope));
                    float valley = Mathf.Clamp01(cav), ridge = Mathf.Clamp01(-cav);

                    float grass = (1f - sl) * (0.25f + n1) * (1f - 0.8f * valley) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.7f, 1f, hn)));
                    float mud = valley * 1.4f + Mathf.Pow(1f - hn, 3f) * n2 * 1.2f;
                    float dirt = 0.15f + sl * 1.5f + n2 * 0.2f;
                    float dry = ridge * 1.4f + Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f, 1f, hn)) * (0.5f + n1);

                    grass = Mathf.Pow(grass, 1.5f); mud = Mathf.Pow(mud, 1.5f); dirt = Mathf.Pow(dirt, 1.5f); dry = Mathf.Pow(dry, 1.5f);
                    float sum = grass + mud + dirt + dry + 1e-5f;
                    map[y, x, 0] = grass / sum; map[y, x, 1] = mud / sum; map[y, x, 2] = dirt / sum; map[y, x, 3] = dry / sum;
                }
            td.SetAlphamaps(0, 0, map);
        }
        EditorUtility.ClearProgressBar();
        UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
        Debug.Log($"[Terrain] Pintados {n} terrenos. Altura mundial {lo:F0}..{hi:F0}.");
    }

    static TerrainLayer[] Layers()
    {
        const string dir = "Assets/Textures/TerrainPaint";
        Directory.CreateDirectory(dir);
        var defs = new (string name, Color dark, Color light, float crack, float streak, float grain)[]
        {
            ("Grass", new Color(.07f, .17f, .04f), new Color(.42f, .58f, .17f), 0f, .6f, .35f),
            ("Mud",   new Color(.05f, .03f, .02f), new Color(.20f, .12f, .08f), .7f, 0f, .30f),
            ("Dirt",  new Color(.30f, .16f, .08f), new Color(.60f, .36f, .19f), 0f, 0f, .55f),
            ("Dry",   new Color(.58f, .45f, .30f), new Color(.88f, .74f, .54f), .2f, 0f, .35f),
        };
        var res = new TerrainLayer[defs.Length];
        for (int i = 0; i < defs.Length; i++)
        {
            var d = defs[i];
            string lp = $"{dir}/{d.name}.terrainlayer";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(lp);
            if (layer == null)
            {
                Gen(dir, d.name, d.dark, d.light, d.crack, d.streak, d.grain);
                layer = new TerrainLayer
                {
                    diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/{d.name}_D.png"),
                    normalMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{dir}/{d.name}_N.png"),
                    tileSize = new Vector2(12f, 12f),
                    normalScale = 1.3f,
                    smoothness = 0f
                };
                AssetDatabase.CreateAsset(layer, lp);
            }
            res[i] = layer;
        }
        AssetDatabase.SaveAssets();
        return res;
    }

    // Textura tileable procedural (albedo + normal derivada del relieve).
    static void Gen(string dir, string name, Color c0, Color c1, float crack, float streak, float grain)
    {
        const int S = 512;
        var h = new float[S, S];
        var col = new Color[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = x / (float)S, v = y / (float)S;
                float t = Fbm(u, v, 3, 3, 4);
                float st = streak > 0f ? Fbm(u, v, 32, 2, 3) : 0.5f;
                float g = Fbm(u, v, 96, 96, 2);
                float cr = crack > 0f ? Mathf.Pow(1f - Mathf.Abs(2f * Fbm(u, v, 5, 5, 3) - 1f), 14f) : 0f;
                Color c = Color.Lerp(c0, c1, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 0.75f, t)));
                float shade = 1f + (g - 0.5f) * grain + (st - 0.5f) * streak - cr * crack;
                col[y * S + x] = new Color(c.r * shade, c.g * shade, c.b * shade, 0.08f);
                h[x, y] = t * 0.6f + g * 0.25f + (streak > 0f ? st * 0.15f : 0f) - cr * crack * 0.4f;
            }
        Save($"{dir}/{name}_D.png", col, S, false);

        var nm = new Color[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = h[(x + 1) % S, y] - h[(x - 1 + S) % S, y], dy = h[x, (y + 1) % S] - h[x, (y - 1 + S) % S];
                Vector3 nv = new Vector3(-dx * 6f, -dy * 6f, 1f).normalized;
                nm[y * S + x] = new Color(nv.x * 0.5f + 0.5f, nv.y * 0.5f + 0.5f, nv.z * 0.5f + 0.5f, 1f);
            }
        Save($"{dir}/{name}_N.png", nm, S, true);
    }

    static void Save(string path, Color[] px, int S, bool normal)
    {
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.SetPixels(px);
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.wrapMode = TextureWrapMode.Repeat;
        imp.anisoLevel = 8;
        if (normal) imp.textureType = TextureImporterType.NormalMap;
        imp.SaveAndReimport();
    }

    // Ruido de valor periódico (tileable); px/py = período por eje.
    static int Mod(int a, int m) => ((a % m) + m) % m;
    static float Hash(int x, int y)
    {
        unchecked { int h = x * 374761393 + y * 668265263; h = (h ^ (h >> 13)) * 1274126177; return ((h ^ (h >> 16)) & 0xffff) / 65535f; }
    }
    static float VN(float x, float y, int px, int py)
    {
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = x - x0, fy = y - y0;
        fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
        int xa = Mod(x0, px), xb = Mod(x0 + 1, px), ya = Mod(y0, py), yb = Mod(y0 + 1, py);
        return Mathf.Lerp(Mathf.Lerp(Hash(xa, ya), Hash(xb, ya), fx), Mathf.Lerp(Hash(xa, yb), Hash(xb, yb), fx), fy);
    }
    static float Fbm(float u, float v, int px, int py, int oct)
    {
        float sum = 0f, a = 0.5f, tot = 0f;
        for (int o = 0; o < oct; o++) { sum += a * VN(u * px, v * py, px, py); tot += a; a *= 0.5f; px *= 2; py *= 2; }
        return sum / tot;
    }

    // 6) Rellena TODOS los terrenos con pinos oscuros, deformes e intrincados (4 variantes, tamaño aleatorio, en
    //    manchas de bosque) y agrega un segundo pasto: yuyos finos. Reemplaza los árboles actuales.
    //    Mallas y prefabs se generan una vez en Assets/Prefabs/ForestFast (borra esa carpeta para regenerarlos).
    const float TreeSpacing = 10f;   // metros entre árboles (menos = más denso)
    const float MaxTreeSlope = 30f;  // no crecen en pendientes mayores
    const float ClearRadius = 2.5f;  // evita árboles dentro de edificios u objetos con collider

    [MenuItem("Tools/Terrain/6 Fill Forest + Weeds (todos)")]
    static void Forest()
    {
        var protos = Pines();
        var ts = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None);
        Physics.SyncTransforms();
        var buf = new Collider[8];
        int total = 0;
        WorldSpan(ts, out float lo, out float hi);

        for (int i = 0; i < ts.Length; i++)
        {
            var t = ts[i]; var td = t.terrainData;
            EditorUtility.DisplayProgressBar("Bosque", t.name, (float)i / ts.Length);
            Undo.RegisterCompleteObjectUndo(td, "Forest");
            td.treePrototypes = protos;

            var list = new List<TreeInstance>();
            var rnd = new System.Random(1234 + i);
            Vector3 p = t.transform.position, sz = td.size;
            for (float z = 0; z < sz.z; z += TreeSpacing)
                for (float x = 0; x < sz.x; x += TreeSpacing)
                {
                    float lx = x + ((float)rnd.NextDouble() - 0.5f) * TreeSpacing * 0.9f;
                    float lz = z + ((float)rnd.NextDouble() - 0.5f) * TreeSpacing * 0.9f;
                    if (lx < 0 || lz < 0 || lx > sz.x || lz > sz.z) continue;
                    float u = lx / sz.x, v = lz / sz.z, wx = p.x + lx, wz = p.z + lz;

                    float patch = Mathf.PerlinNoise(wx * 0.008f + 5f, wz * 0.008f + 9f);
                    if ((float)rnd.NextDouble() > Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.38f, 0.62f, patch))) continue;
                    if (td.GetSteepness(u, v) > MaxTreeSlope) continue;
                    if (Blocked(new Vector3(wx, p.y + td.GetInterpolatedHeight(u, v) + 2f, wz), buf)) continue;

                    float a = (float)rnd.NextDouble(), b = (float)rnd.NextDouble();
                    float size = Mathf.Lerp(0.6f, 1.7f, a * a);
                    list.Add(new TreeInstance
                    {
                        position = new Vector3(u, 0f, v),
                        prototypeIndex = rnd.Next(Normal),
                        heightScale = size,
                        widthScale = size * Mathf.Lerp(0.8f, 1.25f, b),
                        rotation = (float)rnd.NextDouble() * Mathf.PI * 2f,
                        color = new Color32(255, 255, 255, 255),
                        lightmapColor = new Color32(255, 255, 255, 255)
                    });
                }
            list.AddRange(Giants(t, i, lo, hi, buf));
            td.SetTreeInstances(list.ToArray(), true);
            total += list.Count;
        }

        AddWeeds(ts);
        EditorUtility.ClearProgressBar();
        UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
        Debug.Log($"[Terrain] Bosque: {total} pinos en {ts.Length} terrenos + yuyos finos.");
    }

    // 7) Cambia solo los prefabs de pino por la version liviana (LOD0/1/2, sin sombras lejanas) y aplica los
    //    ajustes de 1. Mantiene los arboles actuales y agrega pinos gigantes (5x) solo en lo bajo.
    [MenuItem("Tools/Terrain/7 Optimize Pines (mantiene arboles)")]
    static void Optimize()
    {
        var protos = Pines();
        var ts = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None);
        WorldSpan(ts, out float lo, out float hi);
        Physics.SyncTransforms();
        var buf = new Collider[8];
        int n = 0;
        for (int i = 0; i < ts.Length; i++)
        {
            var td = ts[i].terrainData;
            Undo.RegisterCompleteObjectUndo(td, "Optimize Pines");
            var keep = td.treeInstances.Where(x => x.prototypeIndex < Normal).ToList();
            td.treePrototypes = protos;
            var g = Giants(ts[i], i, lo, hi, buf);
            keep.AddRange(g); n += g.Count;
            td.SetTreeInstances(keep.ToArray(), true);
        }
        Debug.Log($"[Terrain] {n} pinos gigantes agregados.");
        Fix();
    }

    // ---- Pinos gigantes: prototipo 4, escala ~5x, solo en el tramo mas bajo del relieve. ----
    const int Normal = 4;                              // prototipos 0-3 = pinos, 4 = gigante (5x), 5 = coloso (15x)
    const float GiantSpacing = 35f, ColossusSpacing = 120f, GiantLow = 0.3f;   // GiantLow: fraccion de altura mundial donde nacen

    static void WorldSpan(Terrain[] ts, out float lo, out float hi)
    {
        lo = float.MaxValue; hi = float.MinValue;
        foreach (var t in ts)
            for (int y = 0; y <= 32; y++)
                for (int x = 0; x <= 32; x++)
                {
                    float h = t.transform.position.y + t.terrainData.GetInterpolatedHeight(x / 32f, y / 32f);
                    lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h);
                }
    }

    static List<TreeInstance> Giants(Terrain t, int seed, float lo, float hi, Collider[] buf)
    {
        var l = Tall(t, seed, lo, hi, buf, Normal, GiantSpacing, 5f, 0.7f);
        l.AddRange(Tall(t, seed, lo, hi, buf, Normal + 1, ColossusSpacing, 15f, 0.5f));
        return l;
    }

    static List<TreeInstance> Tall(Terrain t, int seed, float lo, float hi, Collider[] buf, int proto, float sp, float scale, float slim)
    {
        var td = t.terrainData; var list = new List<TreeInstance>();
        var rnd = new System.Random(proto * 1000 + seed);
        Vector3 p = t.transform.position, sz = td.size;
        for (float z = 0; z < sz.z; z += sp)
            for (float x = 0; x < sz.x; x += sp)
            {
                float lx = x + ((float)rnd.NextDouble() - 0.5f) * sp * 0.9f;
                float lz = z + ((float)rnd.NextDouble() - 0.5f) * sp * 0.9f;
                if (lx < 0 || lz < 0 || lx > sz.x || lz > sz.z) continue;
                float u = lx / sz.x, v = lz / sz.z, wx = p.x + lx, wz = p.z + lz, y = p.y + td.GetInterpolatedHeight(u, v);
                if (Mathf.InverseLerp(lo, hi, y) > GiantLow || td.GetSteepness(u, v) > MaxTreeSlope) continue;
                if (Mathf.PerlinNoise(wx * 0.01f + 40f, wz * 0.01f) < 0.4f) continue;
                if (Blocked(new Vector3(wx, y + 2f, wz), buf)) continue;
                float s = Rf(rnd, scale * 0.9f, scale * 1.1f);
                list.Add(new TreeInstance
                {
                    position = new Vector3(u, 0f, v),
                    prototypeIndex = proto,
                    heightScale = s,
                    widthScale = s * Rf(rnd, slim - 0.1f, slim + 0.1f),
                    rotation = (float)rnd.NextDouble() * Mathf.PI * 2f,
                    color = new Color32(255, 255, 255, 255),
                    lightmapColor = new Color32(255, 255, 255, 255)
                });
            }
        return list;
    }

    // 8) Cráteres de bombas: manchones oscuros pintados en el alphamap (capa Mud, sin costo extra), sin arboles ni
    //    pasto adentro, y una columna de humo permanente por cada uno. El humo es UNA malla estatica horneada
    //    (54 vertices, sin particulas, sin CPU): se ve desde lejos como cola inclinada por el viento.
    //    Ejecutar despues de 5/6/7 (esos pisan la pintura y los arboles). Re-ejecutar regenera los cráteres.
    const int CraterCount = 12;
    const float CraterGap = 200f, WindYaw = 70f;   // separacion minima (m) y direccion del viento (grados)
    const string BombDir = "Assets/Prefabs/Bombs";

    [MenuItem("Tools/Terrain/8 Bomb Craters + Smoke (todos)")]
    static void Craters()
    {
        var ts = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None);
        if (ts.Length == 0 || ts[0].terrainData.alphamapLayers < 2) { Debug.LogWarning("[Terrain] Primero ejecuta 5 Paint Terrain."); return; }
        Physics.SyncTransforms();
        var buf = new Collider[8];
        var rnd = new System.Random(777);
        var old = GameObject.Find("BombSmoke");
        if (old != null) Undo.DestroyObjectImmediate(old);
        var root = new GameObject("BombSmoke");
        Undo.RegisterCreatedObjectUndo(root, "Craters");
        var mesh = SmokeMesh(); var mat = SmokeMat();
        var done = new List<Vector3>();

        for (int tries = 0; tries < 500 && done.Count < CraterCount; tries++)
        {
            var t = ts[rnd.Next(ts.Length)]; var td = t.terrainData;
            Vector3 p = t.transform.position, sz = td.size;
            float R = Rf(rnd, 7f, 16f);
            float lx = Rf(rnd, R, sz.x - R), lz = Rf(rnd, R, sz.z - R), u = lx / sz.x, v = lz / sz.z;
            var pos = new Vector3(p.x + lx, p.y + td.GetInterpolatedHeight(u, v), p.z + lz);
            if (td.GetSteepness(u, v) > 25f || done.Exists(d => (d - pos).sqrMagnitude < CraterGap * CraterGap)) continue;
            if (Blocked(pos + Vector3.up * 2f, buf)) continue;
            done.Add(pos);
            Scorch(t, u, v, R);

            var go = new GameObject("Smoke" + done.Count);
            go.transform.SetParent(root.transform);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, WindYaw + Rf(rnd, -15f, 15f), 0f));
            go.transform.localScale = Vector3.one * Rf(rnd, 260f, 460f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
        Debug.Log($"[Terrain] {done.Count} cráteres con humo. La camara 'far' debe alcanzar la distancia a la que quieras ver las columnas.");
    }

    static bool Box(Vector3 sz, int res, float cx, float cz, float r, out int x0, out int y0, out int w, out int h)
    {
        x0 = Mathf.Max(0, Mathf.FloorToInt((cx - r) / sz.x * res)); y0 = Mathf.Max(0, Mathf.FloorToInt((cz - r) / sz.z * res));
        w = Mathf.Min(res, Mathf.CeilToInt((cx + r) / sz.x * res)) - x0; h = Mathf.Min(res, Mathf.CeilToInt((cz + r) / sz.z * res)) - y0;
        return w > 0 && h > 0;
    }

    static void Scorch(Terrain t, float u, float v, float R)
    {
        var td = t.terrainData; Vector3 sz = td.size, p = t.transform.position;
        Undo.RegisterCompleteObjectUndo(td, "Crater");
        float cx = u * sz.x, cz = v * sz.z;

        var keep = td.treeInstances.Where(i =>
        {
            float ex = (i.position.x - u) * sz.x, ez = (i.position.z - v) * sz.z;
            return ex * ex + ez * ez > R * R * 1.5f;
        }).ToArray();
        if (keep.Length != td.treeInstanceCount) td.SetTreeInstances(keep, true);

        int ar = td.alphamapWidth;
        if (Box(sz, ar, cx, cz, R * 1.4f, out int x0, out int y0, out int w, out int h))
        {
            var m = td.GetAlphamaps(x0, y0, w, h); int L = m.GetLength(2);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float dx = (x0 + x + 0.5f) / ar * sz.x - cx, dz = (y0 + y + 0.5f) / ar * sz.z - cz;
                    float edge = R * (0.75f + 0.5f * Mathf.PerlinNoise((p.x + cx + dx) * 0.15f, (p.z + cz + dz) * 0.15f));
                    float k = Mathf.Clamp01((1f - Mathf.Sqrt(dx * dx + dz * dz) / edge) * 3f);
                    if (k <= 0f) continue;
                    for (int l = 0; l < L; l++) m[y, x, l] *= 1f - k;
                    m[y, x, 1] += k;   // capa 1 = Mud (la mas oscura)
                }
            td.SetAlphamaps(x0, y0, m);
        }

        int dr = td.detailWidth, np = td.detailPrototypes.Length;
        if (Box(sz, dr, cx, cz, R * 1.1f, out x0, out y0, out w, out h))
            for (int l = 0; l < np; l++)
            {
                var d = td.GetDetailLayer(x0, y0, w, h, l);
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float dx = (x0 + x + 0.5f) / dr * sz.x - cx, dz = (y0 + y + 0.5f) / dr * sz.z - cz;
                        if (dx * dx + dz * dz < R * R * 1.2f) d[y, x] = 0;
                    }
                td.SetDetailLayer(x0, y0, l, d);
            }
    }

    // Columna de humo de altura 1: 3 planos cruzados, 8 segmentos, inclinada y ensanchada hacia arriba.
    // El degradado (gris oscuro -> claro, opaco -> transparente) va en el color de vertice.
    static Mesh SmokeMesh()
    {
        const string path = BombDir + "/Smoke.asset";
        var m = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (m != null) return m;
        Directory.CreateDirectory(BombDir);
        const int S = 8;
        var vt = new List<Vector3>(); var cl = new List<Color>(); var uv = new List<Vector2>(); var tr = new List<int>();
        for (int pl = 0; pl < 3; pl++)
        {
            float a = Mathf.PI * pl / 3f;
            var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            int b = vt.Count;
            for (int i = 0; i <= S; i++)
            {
                float y = (float)i / S, hw = 0.03f + 0.15f * y, g = 0.12f + 0.32f * y;
                float al = Mathf.Min(1f, y * 8f) * Mathf.Sqrt(1f - y) * 0.9f;
                var c = new Vector3(0.35f * y * y + 0.03f * Mathf.Sin(y * 9f + pl), y, 0.03f * Mathf.Sin(y * 7f));
                vt.Add(c - d * hw); vt.Add(c + d * hw);
                cl.Add(new Color(g, g, g, al)); cl.Add(new Color(g, g, g, al));
                uv.Add(new Vector2(0f, y)); uv.Add(new Vector2(1f, y));
            }
            for (int i = 0; i < S; i++) { int k = b + i * 2; tr.AddRange(new[] { k, k + 2, k + 1, k + 1, k + 2, k + 3 }); }
        }
        m = new Mesh { name = "Smoke" };
        m.SetVertices(vt); m.SetColors(cl); m.SetUVs(0, uv); m.SetTriangles(tr, 0);
        m.RecalculateBounds();
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static Material SmokeMat()
    {
        const string path = BombDir + "/Smoke.mat", tp = BombDir + "/Smoke_D.png";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        Directory.CreateDirectory(BombDir);
        const int S = 128;
        var px = new Color[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = x / (float)S, v = y / (float)S;
                float e = Mathf.SmoothStep(0f, 1f, (1f - Mathf.Abs(2f * u - 1f)) * 1.6f);
                px[y * S + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(e * Fbm(u, v, 3, 6, 3) * 1.4f));
            }
        Save(tp, px, S, false);
        // Sprites/Default: sin luz, mezcla alfa, funciona igual en URP y built-in, soporta instancing.
        m = new Material(Shader.Find("Sprites/Default")) { mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(tp), enableInstancing = true };
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    // 9) Pasto alto y muy denso en las zonas bajas: tercer detalle (billboards, sin mallas). Se suma al pasto existente.
    //    Ejecutar antes de 8 (los cráteres lo borran dentro de su radio).
    [MenuItem("Tools/Terrain/9 Fill Tall Grass (zonas bajas)")]
    static void TallGrass()
    {
        var ts = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None);
        WorldSpan(ts, out float lo, out float hi);
        var tex = TallGrassTex();
        foreach (var t in ts)
        {
            var td = t.terrainData;
            Undo.RegisterCompleteObjectUndo(td, "Tall Grass");
            int n = td.detailWidth;
            var old = td.detailPrototypes;
            int idx = System.Array.FindIndex(old, d => d.prototypeTexture == tex);
            if (idx < 0)
            {
                var keep = new int[old.Length][,];
                for (int i = 0; i < old.Length; i++) keep[i] = td.GetDetailLayer(0, 0, n, n, i);
                var np = new List<DetailPrototype>(old)
                {
                    new DetailPrototype
                    {
                        prototypeTexture = tex,
                        renderMode = DetailRenderMode.GrassBillboard,
                        healthyColor = new Color(0.40f, 0.50f, 0.20f),
                        dryColor = new Color(0.70f, 0.60f, 0.30f),
                        minWidth = 1.2f, maxWidth = 2f, minHeight = 2.5f, maxHeight = 4f, noiseSpread = 10f
                    }
                };
                idx = old.Length;
                td.detailPrototypes = np.ToArray();
                for (int i = 0; i < old.Length; i++) td.SetDetailLayer(0, 0, i, keep[i]);
            }

            var w = new int[n, n];
            Vector3 p = t.transform.position, sz = td.size;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                    float hn = Mathf.InverseLerp(lo, hi, p.y + td.GetInterpolatedHeight(u, v));
                    float low = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.4f, hn));
                    if (low <= 0f || td.GetSteepness(u, v) > 35f) continue;
                    float nz = Mathf.PerlinNoise((p.x + u * sz.x) * 0.03f + 11f, (p.z + v * sz.z) * 0.03f + 2f);
                    w[y, x] = Mathf.RoundToInt(low * Mathf.Clamp01((nz - 0.2f) * 3f) * 16f);
                }
            td.SetDetailLayer(0, 0, idx, w);
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
        Debug.Log("[Terrain] Pasto alto en zonas bajas.");
    }

    static Texture2D TallGrassTex()
    {
        const string path = "Assets/Textures/TallGrass.png";
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (existing != null) return existing;

        const int S = 64;
        var px = new Color[S * S];
        var rnd = new System.Random(11);
        for (int b = 0; b < 12; b++)
        {
            float cx = 3f + b * 5f, h = 40 + rnd.Next(24), lean = ((float)rnd.NextDouble() - 0.5f) * 10f;
            for (int y = 0; y < h; y++)
            {
                float f = y / h, w = (1f - f) * 1.1f + 0.3f, xc = cx + lean * f * f;
                var col = Color.Lerp(new Color(0.18f, 0.26f, 0.08f), new Color(0.70f, 0.78f, 0.36f), f);
                for (int x = Mathf.RoundToInt(xc - w); x <= Mathf.RoundToInt(xc + w); x++)
                    if (x >= 0 && x < S) px[y * S + x] = col;
            }
        }
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.SetPixels(px);
        Directory.CreateDirectory("Assets/Textures");
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.alphaIsTransparency = true;
        imp.wrapMode = TextureWrapMode.Clamp;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static bool Blocked(Vector3 c, Collider[] buf)
    {
        int k = Physics.OverlapSphereNonAlloc(c, ClearRadius, buf, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < k; i++) if (!(buf[i] is TerrainCollider)) return true;
        return false;
    }

    // Segundo tipo de pasto: yuyos finos y altos, en manchas distintas al pasto base (se conserva el existente).
    static void AddWeeds(Terrain[] ts)
    {
        var tex = WeedTex();
        foreach (var t in ts)
        {
            var td = t.terrainData;
            Undo.RegisterCompleteObjectUndo(td, "Weeds");
            int n = td.detailWidth;
            var old = td.detailPrototypes;
            int idx = System.Array.FindIndex(old, d => d.prototypeTexture == tex);
            if (idx < 0)
            {
                var keep = new int[old.Length][,];
                for (int i = 0; i < old.Length; i++) keep[i] = td.GetDetailLayer(0, 0, n, n, i);
                var np = new List<DetailPrototype>(old)
                {
                    new DetailPrototype
                    {
                        prototypeTexture = tex,
                        renderMode = DetailRenderMode.GrassBillboard,
                        healthyColor = new Color(0.62f, 0.66f, 0.30f),
                        dryColor = new Color(0.74f, 0.60f, 0.28f),
                        minWidth = 0.6f, maxWidth = 1.1f, minHeight = 1.2f, maxHeight = 2.4f, noiseSpread = 8f
                    }
                };
                idx = old.Length;
                td.detailPrototypes = np.ToArray();
                for (int i = 0; i < old.Length; i++) td.SetDetailLayer(0, 0, i, keep[i]);
            }

            var w = new int[n, n];
            Vector3 p = t.transform.position, sz = td.size;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                    float nz = Mathf.PerlinNoise((p.x + u * sz.x) * 0.05f + 3f, (p.z + v * sz.z) * 0.05f + 8f);
                    if (nz < 0.42f || td.GetSteepness(u, v) > 40f) continue;
                    w[y, x] = Mathf.RoundToInt(Mathf.Clamp01((nz - 0.4f) * 3.5f) * 10f);
                }
            td.SetDetailLayer(0, 0, idx, w);
        }
    }

    static Texture2D WeedTex()
    {
        const string path = "Assets/Textures/Weeds.png";
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (existing != null) return existing;

        const int S = 64;
        var px = new Color[S * S];
        var rnd = new System.Random(7);
        for (int b = 0; b < 14; b++)
        {
            float cx = 3f + b * 4.3f, h = 34 + rnd.Next(28), lean = ((float)rnd.NextDouble() - 0.5f) * 16f;
            for (int y = 0; y < h; y++)
            {
                float f = y / h, w = (1f - f) * 0.9f + 0.35f, xc = cx + lean * f * f;
                var col = Color.Lerp(new Color(0.25f, 0.30f, 0.10f), new Color(0.80f, 0.85f, 0.45f), f);
                for (int x = Mathf.RoundToInt(xc - w); x <= Mathf.RoundToInt(xc + w); x++)
                    if (x >= 0 && x < S) px[y * S + x] = col;
            }
        }
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.SetPixels(px);
        Directory.CreateDirectory("Assets/Textures");
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.alphaIsTransparency = true;
        imp.wrapMode = TextureWrapMode.Clamp;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // ---- Pinos procedurales: tronco torcido + copas en estrella irregulares y caídas + ramas finas. LOD0/LOD1. ----
    static TreePrototype[] Pines()
    {
        const string dir = "Assets/Prefabs/ForestFast";
        Directory.CreateDirectory(dir);
        var bark = Mat(dir, "Bark", new Color(0.09f, 0.06f, 0.045f));
        var needles = Mat(dir, "Needles", new Color(0.035f, 0.10f, 0.055f));
        var res = new TreePrototype[Normal + 2];
        for (int v = 0; v < res.Length; v++)
        {
            string pp = $"{dir}/Pine{v}.prefab";
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(pp);
            if (go == null)
            {
                var root = new GameObject($"Pine{v}");
                var r0 = Part(root, "LOD0", Build(v, 0), dir, bark, needles);
                var r1 = Part(root, "LOD1", Build(v, 1), dir, bark, needles);
                var r2 = Part(root, "LOD2", Build(v, 2), dir, bark, needles);
                var g = root.AddComponent<LODGroup>();
                float k = v == Normal ? 3.3f : v > Normal ? 9f : 1f;   // el gigante cambia de LOD a la misma distancia que un pino normal
                g.SetLODs(new[] { new LOD(0.15f * k, new[] { r0 }), new LOD(0.04f * k, new[] { r1 }), new LOD(0.012f, new[] { r2 }) });
                g.RecalculateBounds();
                go = PrefabUtility.SaveAsPrefabAsset(root, pp);
                Object.DestroyImmediate(root);
            }
            res[v] = new TreePrototype { prefab = go };
        }
        AssetDatabase.SaveAssets();
        return res;
    }

    static Material Mat(string dir, string name, Color c)
    {
        string path = $"{dir}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        m = new Material(Shader.Find("Universal Render Pipeline/Simple Lit") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        m.color = c;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.08f);
        if (m.HasProperty("_SpecColor")) m.SetColor("_SpecColor", Color.black);
        m.enableInstancing = true;
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static Renderer Part(GameObject root, string n, Mesh mesh, string dir, Material bark, Material needles)
    {
        mesh.name = $"{root.name}_{n}";
        AssetDatabase.CreateAsset(mesh, $"{dir}/{mesh.name}.asset");
        var c = new GameObject(n);
        c.transform.SetParent(root.transform, false);
        c.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = c.AddComponent<MeshRenderer>();
        bool near = n == "LOD0";   // lejos: 1 material, sin sombras, sin probes
        r.sharedMaterials = near ? new[] { bark, needles } : new[] { needles };
        r.shadowCastingMode = near ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = near;
        r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        return r;
    }

    static float Rf(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);

    static Vector3 PointAt(Vector3[] pts, float f)
    {
        float x = f * (pts.Length - 1);
        int i = Mathf.Min((int)x, pts.Length - 2);
        return Vector3.Lerp(pts[i], pts[i + 1], x - i);
    }

    static Mesh Build(int variant, int lod)
    {
        var rt = new System.Random(900 + variant * 17);   // tronco y copas: idénticos en LOD0 y LOD1
        var rb = new System.Random(500 + variant * 31);   // ramas finas: solo LOD0
        float H = Rf(rt, 8f, 15f), amp = Rf(rt, 0.4f, 1.1f), lean = Rf(rt, -1.4f, 1.4f);
        float p1 = Rf(rt, 0f, 6.28f), p2 = Rf(rt, 0f, 6.28f), r0 = Rf(rt, 0.26f, 0.45f);
        const int N = 10;
        int sides = lod == 0 ? 6 : 3;

        var pts = new Vector3[N + 1]; var rad = new float[N + 1];
        for (int k = 0; k <= N; k++)
        {
            float f = (float)k / N, w = (0.25f + f) * 0.5f * amp;
            pts[k] = new Vector3(Mathf.Sin(f * 6f + p1) * w + lean * f * f, f * H, Mathf.Sin(f * 5f + p2) * w);
            rad[k] = r0 * (1f - f * 0.93f) + 0.02f;
        }

        var vB = new List<Vector3>(); var tB = new List<int>();
        var vF = new List<Vector3>(); var tF = new List<int>();
        bool one = lod > 0;   // LOD1/2: un solo submesh = 1 draw call
        if (lod < 2)
        {
            int m = lod == 0 ? N + 1 : 5;   // LOD1: solo la base del tronco (lo demas lo tapa la copa)
            Tube(one ? vF : vB, one ? tF : tB, pts.Take(m).ToArray(), rad.Take(m).ToArray(), sides);
        }

        int tiers = lod == 2 ? 0 : rt.Next(8, 12);
        for (int ti = 0; ti < tiers; ti++)
        {
            float f = Mathf.Lerp(0.26f, 0.97f, (ti + (float)rt.NextDouble() * 0.6f) / tiers);
            float size = Mathf.Lerp(2.6f, 0.35f, f) * Rf(rt, 0.7f, 1.35f) * H / 11f;
            Vector3 c = PointAt(pts, f);
            int spokes = rt.Next(5, 8), cnt = spokes * 2;
            var q = Quaternion.Euler(Rf(rt, -14f, 14f), Rf(rt, 0f, 360f), Rf(rt, -14f, 14f));
            float droop = size * Rf(rt, 0.25f, 0.6f);

            var ring = new Vector3[cnt];
            for (int s = 0; s < cnt; s++)
            {
                bool tip = s % 2 == 0;
                float ang = Mathf.PI * 2f * s / cnt + Rf(rt, -0.15f, 0.15f);
                float rr = tip ? size * Rf(rt, 0.5f, 1.4f) : size * 0.38f * Rf(rt, 0.7f, 1.2f);
                float dy = tip ? -droop * Rf(rt, 0.6f, 1.3f) : -droop * 0.3f;
                ring[s] = c + q * new Vector3(Mathf.Cos(ang) * rr, dy, Mathf.Sin(ang) * rr);
            }
            Vector3 apex = c + q * new Vector3(0f, size * 0.22f, 0f);

            // Dos caras con vértices separados: iluminadas bien desde arriba y desde abajo.
            if (lod == 1 && ti % 2 == 1) continue;   // LOD1: la mitad de los pisos
            int a0 = vF.Count;
            vF.Add(apex); vF.AddRange(ring);
            vF.Add(apex); vF.AddRange(ring);
            int b0 = a0 + cnt + 1;
            for (int s = 0; s < cnt; s++)
            {
                int s1 = (s + 1) % cnt;
                tF.AddRange(new[] { a0, a0 + 1 + s, a0 + 1 + s1 });
                tF.AddRange(new[] { b0, b0 + 1 + s1, b0 + 1 + s });
            }
        }

        if (lod == 2)   // LOD2: cono de 5 lados (10 triangulos)
        {
            Vector3 bs = PointAt(pts, 0.2f);
            vF.Add(pts[N]); vF.Add(bs);
            for (int s = 0; s < 5; s++)
                vF.Add(bs + new Vector3(Mathf.Cos(Mathf.PI * 2f * s / 5f), 0f, Mathf.Sin(Mathf.PI * 2f * s / 5f)) * (2.2f * H / 11f));
            for (int s = 0; s < 5; s++)
            {
                int a = 2 + s, b = 2 + (s + 1) % 5;
                tF.AddRange(new[] { 0, b, a, 1, a, b });
            }
        }

        if (lod == 0)
        {
            int nb = rb.Next(5, 10);
            for (int bi = 0; bi < nb; bi++)
            {
                Vector3 s0 = PointAt(pts, Rf(rb, 0.18f, 0.8f));
                float yaw = Rf(rb, 0f, 6.28f), len = Rf(rb, 1.2f, 3.2f) * H / 11f;
                var dir = new Vector3(Mathf.Cos(yaw), Rf(rb, -0.2f, 0.5f), Mathf.Sin(yaw)).normalized;
                Vector3 side = Vector3.Cross(dir, Vector3.up);
                var bp = new Vector3[4]; var br = new float[4];
                for (int k = 0; k < 4; k++)
                {
                    float g = k / 3f;
                    bp[k] = s0 + dir * len * g + Vector3.down * len * 0.25f * g * g + side * Mathf.Sin(g * 4f + bi) * len * 0.1f;
                    br[k] = Mathf.Lerp(0.07f, 0.01f, g);
                }
                Tube(vB, tB, bp, br, 3);
            }
        }

        var mesh = new Mesh();
        var all = new List<Vector3>(vB);
        all.AddRange(vF);
        int off = vB.Count;
        for (int i = 0; i < tF.Count; i++) tF[i] += off;
        mesh.SetVertices(all);
        mesh.subMeshCount = one ? 1 : 2;
        if (one) mesh.SetTriangles(tF, 0);
        else { mesh.SetTriangles(tB, 0); mesh.SetTriangles(tF, 1); }
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // Tubo de sección poligonal siguiendo una curva (tronco y ramas).
    static void Tube(List<Vector3> v, List<int> t, Vector3[] pts, float[] rad, int sides)
    {
        int b = v.Count, n = pts.Length;
        for (int k = 0; k < n; k++)
        {
            Vector3 tan = (pts[Mathf.Min(k + 1, n - 1)] - pts[Mathf.Max(k - 1, 0)]).normalized;
            Vector3 a = Vector3.Cross(tan, Mathf.Abs(tan.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
            Vector3 c = Vector3.Cross(tan, a);
            for (int s = 0; s < sides; s++)
            {
                float ang = Mathf.PI * 2f * s / sides;
                v.Add(pts[k] + (a * Mathf.Cos(ang) + c * Mathf.Sin(ang)) * rad[k]);
            }
        }
        for (int k = 0; k < n - 1; k++)
            for (int s = 0; s < sides; s++)
            {
                int i0 = b + k * sides + s, i1 = b + k * sides + (s + 1) % sides, i2 = i0 + sides, i3 = i1 + sides;
                t.AddRange(new[] { i0, i1, i2, i1, i3, i2 });
            }
    }

    static Texture2D GrassTex()
    {
        const string path = "Assets/Textures/Grass.png";
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (existing != null) return existing;

        const int S = 64;
        var px = new Color[S * S];
        for (int b = 0; b < 7; b++)
        {
            float cx = 6 + b * 8.5f, h = 36 + (b * 7 % 24), lean = (b % 2 == 0 ? 1 : -1) * (3 + b % 3);
            for (int y = 0; y < h; y++)
            {
                float f = y / h, w = (1f - f) * 2.6f + 0.4f, xc = cx + lean * f * f;
                var col = Color.Lerp(new Color(0.15f, 0.35f, 0.1f), new Color(0.75f, 0.95f, 0.4f), f);
                for (int x = Mathf.RoundToInt(xc - w); x <= Mathf.RoundToInt(xc + w); x++)
                    if (x >= 0 && x < S) px[y * S + x] = col;
            }
        }
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.SetPixels(px);
        Directory.CreateDirectory("Assets/Textures");
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);

        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.alphaIsTransparency = true;
        imp.wrapMode = TextureWrapMode.Clamp;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // 10) HERRAMIENTA: selecciona objetos en la Hierarchy y ejecuta el menu. Les pone chapas de metal futurista
    //     oxidado y les agrega collider solido (MeshCollider; si ya tenian uno en trigger, lo vuelve solido).
    //     La textura y el material se generan una vez en Assets/Textures/RustMetal (borra la carpeta para regenerarlos).
    const string MetalDir = "Assets/Textures/RustMetal";

    [MenuItem("Tools/Terrain/10 Rusty Metal + Collider (seleccion)")]
    static void MetalSelection()
    {
        var sel = Selection.gameObjects;
        if (sel.Length == 0) { Debug.LogWarning("[Metal] Selecciona objetos en la Hierarchy."); return; }
        var mat = MetalMat();
        int nr = 0, nc = 0;
        foreach (var go in sel)
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                Undo.RecordObject(r, "Rusty Metal");
                var ms = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
                for (int i = 0; i < ms.Length; i++) ms[i] = mat;
                r.sharedMaterials = ms; nr++;
                var mf = r.GetComponent<MeshFilter>();
                if (r.TryGetComponent<Collider>(out var col))
                {
                    if (col.isTrigger) { Undo.RecordObject(col, "Solid Collider"); col.isTrigger = false; nc++; }
                }
                else if (mf != null && mf.sharedMesh != null)
                {
                    var mc = Undo.AddComponent<MeshCollider>(r.gameObject);
                    mc.sharedMesh = mf.sharedMesh;
                    mc.convex = r.GetComponentInParent<Rigidbody>() != null;
                    nc++;
                }
                EditorUtility.SetDirty(r);
            }
        UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
        Debug.Log($"[Metal] {nr} renderers pintados, {nc} colliders solidos.");
    }

    static Material MetalMat()
    {
        const string path = MetalDir + "/RustMetal.mat", dp = MetalDir + "/RustMetal_D.png", np = MetalDir + "/RustMetal_N.png";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        Directory.CreateDirectory(MetalDir);
        MetalTex(dp, np);
        m = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        var alb = AssetDatabase.LoadAssetAtPath<Texture2D>(dp);
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", alb);
        if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", alb);
        m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(np));
        m.EnableKeyword("_NORMALMAP");
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0.8f);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.4f);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.4f);
        m.mainTextureScale = new Vector2(2f, 2f);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    // Chapas 4x4 con costuras, remaches, oxido en manchas/bordes y algunos paneles con tinte cian. Tileable.
    static void MetalTex(string dp, string np)
    {
        const int S = 512, P = 128;
        var col = new Color[S * S];
        var nm = new Color[S * S];
        var h = new float[S, S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = x / (float)S, v = y / (float)S;
                int px = x % P, py = y % P;
                int ax = Mathf.Min(px, P - 1 - px), ay = Mathf.Min(py, P - 1 - py), e = Mathf.Min(ax, ay);
                float seam = e < 3 ? 1f : (e < 6 ? 0.4f : 0f);
                float rivet = Vector2.Distance(new Vector2(ax, ay), new Vector2(14f, 14f)) < 4f ? 1f : 0f;
                float pv = Hash(x / P, y / P);

                Color steel = Color.Lerp(new Color(0.30f, 0.34f, 0.38f), new Color(0.17f, 0.19f, 0.22f), pv);
                if (pv > 0.85f) steel = Color.Lerp(steel, new Color(0.10f, 0.45f, 0.50f), 0.35f);
                steel *= 1f + (Fbm(u, v, 64, 64, 2) - 0.5f) * 0.3f;

                float rust = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 0.7f, Fbm(u, v, 6, 6, 4) + (e < 14 ? 0.2f : 0f)));
                Color rc = Color.Lerp(new Color(0.30f, 0.12f, 0.05f), new Color(0.55f, 0.25f, 0.08f), Fbm(u, v, 24, 24, 2));
                Color c = Color.Lerp(steel, rc, rust) * (1f - seam * 0.65f);
                c.a = 1f;
                col[y * S + x] = c;
                h[x, y] = 0.5f - seam * 0.35f + rivet * 0.3f + (Fbm(u, v, 48, 48, 3) - 0.5f) * 0.15f * (1f + rust * 2f);
            }
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = h[(x + 1) % S, y] - h[(x - 1 + S) % S, y], dy = h[x, (y + 1) % S] - h[x, (y - 1 + S) % S];
                Vector3 nv = new Vector3(-dx * 6f, -dy * 6f, 1f).normalized;
                nm[y * S + x] = new Color(nv.x * 0.5f + 0.5f, nv.y * 0.5f + 0.5f, nv.z * 0.5f + 0.5f, 1f);
            }
        Save(dp, col, S, false);
        Save(np, nm, S, true);
    }
}