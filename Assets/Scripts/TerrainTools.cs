// Poner en una carpeta "Editor" (Assets/Editor/TerrainTools.cs). Menu: Tools/Terrain
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using ESM = UnityEditor.SceneManagement.EditorSceneManager;

static class TerrainTools
{
    static Terrain[] Selected() => Selection.GetFiltered<Terrain>(SelectionMode.Deep);

    // 1) Todos los terrenos: distancias de dibujo + informe de por que los arboles pueden no verse.
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

    // 2) Borra TODOS los arboles de los terrenos seleccionados (seleccionar TerrainsPrefab sirve).
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

    // 3) Pasto automatico por pendiente y ruido (textura de pasto generada una sola vez).
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

    // 4) Une los bordes de TODOS los terrenos de la escena (no depende de la seleccion).
    //    Detecta vecinos por solapamiento (cualquier tamano/resolucion), alinea posiciones, iguala la altura
    //    mundial del borde y suaviza una franja. Escribe un informe completo en la Console.
    [MenuItem("Tools/Terrain/4 Stitch Edges (todos)")]
    static void Stitch()
    {
        const float MaxGap = 200f;       // hueco maximo entre terrenos que se sigue considerando vecino
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

        // 1) Posiciones exactas (solo si los tamanos coinciden), en orden de grilla.
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
    // y reparte el ajuste en una franja con caida suave.
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

    // 5) Pinta TODOS los terrenos con 4 capas (pasto, barro oscuro, tierra, tierra seca clara) segun altura,
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

    // Ruido de valor periodico (tileable); px/py = periodo por eje.
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

    // 6) Rellena TODOS los terrenos con pinos oscuros, deformes e intrincados (4 variantes, tamano aleatorio, en
    //    manchas de bosque) y agrega un segundo pasto: yuyos finos. Reemplaza los arboles actuales.
    //    Mallas y prefabs se generan una vez en Assets/Prefabs/ForestFast (borra esa carpeta para regenerarlos).
    const float TreeSpacing = 10f;   // metros entre arboles (menos = mas denso)
    const float MaxTreeSlope = 30f;  // no crecen en pendientes mayores
    const float ClearRadius = 2.5f;  // evita arboles dentro de edificios u objetos con collider

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

    // 8) Crateres de bombas: manchones oscuros pintados en el alphamap (capa Mud, sin costo extra), sin arboles ni
    //    pasto adentro, y una columna de humo permanente por cada uno. El humo es UNA malla estatica horneada
    //    (54 vertices, sin particulas, sin CPU): se ve desde lejos como cola inclinada por el viento.
    //    Ejecutar despues de 5/6/7 (esos pisan la pintura y los arboles). Re-ejecutar regenera los crateres.
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
        Debug.Log($"[Terrain] {done.Count} crateres con humo. La camara 'far' debe alcanzar la distancia a la que quieras ver las columnas.");
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
    //    Ejecutar antes de 8 (los crateres lo borran dentro de su radio).
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

    // 13) Cuenco de bordes: eleva TODOS los bordes del mapa (union de los terrenos) con colinas irregulares que suben hacia
    //     el limite. Si falta altura de terreno (size.y), la amplia reescalando las alturas. Ejecutar UNA vez, despues de 4 y
    //     antes de 5/6/7/9. Crea el marcador "BorderHills" (borralo para poder volver a ejecutar; sumaria otra vez).
    const float RimWidth = 140f, RimHeight = 60f;   // ancho medio (m) y altura media (m) de las colinas

    [MenuItem("Tools/Terrain/13 Border Hills (todos)")]
    static void BorderHills()
    {
        var ts = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None);
        if (ts.Length == 0) return;
        if (GameObject.Find("BorderHills") != null) { Debug.LogWarning("[Terrain] Ya aplicado ('BorderHills'). Ctrl+Z o borra el marcador para repetir."); return; }
        var m = MapRect(ts);
        for (int i = 0; i < ts.Length; i++)
        {
            var td = ts[i].terrainData; Vector3 p = ts[i].transform.position, sz = td.size;
            EditorUtility.DisplayProgressBar("Colinas de borde", ts[i].name, (float)i / ts.Length);
            Undo.RegisterCompleteObjectUndo(td, "Border Hills");
            int r = td.heightmapResolution;
            var H = td.GetHeights(0, 0, r, r);
            var add = new float[r, r];
            float need = sz.y;
            for (int y = 0; y < r; y++)
                for (int x = 0; x < r; x++)
                {
                    float a = Rise(m, p.x + x / (r - 1f) * sz.x, p.z + y / (r - 1f) * sz.z);
                    add[y, x] = a;
                    need = Mathf.Max(need, H[y, x] * sz.y + a);
                }
            float ny = need > sz.y ? need * 1.02f : sz.y;
            for (int y = 0; y < r; y++)
                for (int x = 0; x < r; x++) H[y, x] = (H[y, x] * sz.y + add[y, x]) / ny;
            if (ny != sz.y) td.size = new Vector3(sz.x, ny, sz.z);
            td.SetHeights(0, 0, H);
            td.SetTreeInstances(td.treeInstances, true);   // reasienta los arboles existentes
        }
        EditorUtility.ClearProgressBar();
        var mk = new GameObject("BorderHills");
        Undo.RegisterCreatedObjectUndo(mk, "Border Hills");
        UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
        Debug.Log($"[Terrain] Colinas de borde sobre {ts.Length} terrenos. Mapa X {m.xMin:F0}..{m.xMax:F0}, Z {m.yMin:F0}..{m.yMax:F0}. Lo que estuviera a menos de ~{RimWidth:F0} m del limite queda enterrado.");
    }

    // 14) Corona de arboles en las colinas de 13: pinos densos hacia el borde, con gigantes y colosos que dan la impresion
    //     de bosque que sigue mas alla del mapa. AGREGA a los arboles actuales (ejecutar despues de 6 o 7, que los borran).
    //     Crea el marcador "BorderTrees" (borralo para repetir).
    [MenuItem("Tools/Terrain/14 Border Trees (todos)")]
    static void BorderTrees()
    {
        var ts = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None);
        if (ts.Length == 0) return;
        if (GameObject.Find("BorderTrees") != null) { Debug.LogWarning("[Terrain] Ya aplicado ('BorderTrees'). Ctrl+Z o borra el marcador para repetir."); return; }
        foreach (var t in ts)
        {
            var pr = t.terrainData.treePrototypes;
            if (pr.Length < Normal + 2 || pr[Normal].prefab == null || pr[Normal].prefab.name != "Pine" + Normal)
            { Debug.LogWarning("[Terrain] Faltan los pinos procedurales: ejecuta primero 6 o 7."); return; }
        }
        const float Sp = 5f;
        var m = MapRect(ts);
        int total = 0;
        for (int i = 0; i < ts.Length; i++)
        {
            var t = ts[i]; var td = t.terrainData;
            EditorUtility.DisplayProgressBar("Corona de arboles", t.name, (float)i / ts.Length);
            Undo.RegisterCompleteObjectUndo(td, "Border Trees");
            var list = new List<TreeInstance>(td.treeInstances);
            int before = list.Count;
            var rnd = new System.Random(4242 + i);
            Vector3 p = t.transform.position, sz = td.size;
            for (float z = 0; z < sz.z; z += Sp)
                for (float x = 0; x < sz.x; x += Sp)
                {
                    float lx = x + ((float)rnd.NextDouble() - 0.5f) * Sp * 0.9f, lz = z + ((float)rnd.NextDouble() - 0.5f) * Sp * 0.9f;
                    if (lx < 0 || lz < 0 || lx > sz.x || lz > sz.z) continue;
                    float wx = p.x + lx, wz = p.z + lz, zone = RimW(wx, wz) * 0.65f;
                    float d = EdgeDist(m, wx, wz);
                    if (d > zone) continue;
                    float k = 1f - d / zone;                                  // 1 en el borde, 0 en el limite interior
                    if ((float)rnd.NextDouble() > 0.25f + 0.75f * k) continue;
                    float u = lx / sz.x, v = lz / sz.z;
                    if (td.GetSteepness(u, v) > 60f) continue;
                    float roll = (float)rnd.NextDouble(), s, slim;
                    int proto;
                    if (roll < 0.04f * k) { proto = Normal + 1; s = Rf(rnd, 9f, 13f); slim = 0.5f; }
                    else if (roll < 0.22f * k) { proto = Normal; s = Rf(rnd, 3.5f, 5.5f); slim = 0.7f; }
                    else { proto = rnd.Next(Normal); s = Mathf.Lerp(1.4f, 2.8f, k) * Rf(rnd, 0.8f, 1.2f); slim = Rf(rnd, 0.8f, 1.25f); }
                    list.Add(new TreeInstance
                    {
                        position = new Vector3(u, 0f, v),
                        prototypeIndex = proto,
                        heightScale = s,
                        widthScale = s * slim,
                        rotation = (float)rnd.NextDouble() * Mathf.PI * 2f,
                        color = new Color32(255, 255, 255, 255),
                        lightmapColor = new Color32(255, 255, 255, 255)
                    });
                }
            td.SetTreeInstances(list.ToArray(), true);
            total += list.Count - before;
        }
        EditorUtility.ClearProgressBar();
        var mk = new GameObject("BorderTrees");
        Undo.RegisterCreatedObjectUndo(mk, "Border Trees");
        UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
        Debug.Log($"[Terrain] Corona: {total} arboles agregados en los bordes.");
    }

    static Rect MapRect(Terrain[] ts)
    {
        float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue;
        foreach (var t in ts)
        {
            Vector3 p = t.transform.position, s = t.terrainData.size;
            x0 = Mathf.Min(x0, p.x); z0 = Mathf.Min(z0, p.z); x1 = Mathf.Max(x1, p.x + s.x); z1 = Mathf.Max(z1, p.z + s.z);
        }
        return Rect.MinMaxRect(x0, z0, x1, z1);   // yMin/yMax = Z
    }

    static float EdgeDist(Rect m, float x, float z) => Mathf.Min(Mathf.Min(x - m.xMin, m.xMax - x), Mathf.Min(z - m.yMin, m.yMax - z));

    // Ancho local de la colina: varia a lo largo del borde.
    static float RimW(float x, float z) => RimWidth * (0.6f + 0.8f * Mathf.PerlinNoise(x * 0.006f + 3f, z * 0.006f + 7f));

    // Altura (m) agregada en un punto: cuenco que sube hacia el limite, con altura y rugosidad irregulares.
    static float Rise(Rect m, float x, float z)
    {
        float d = EdgeDist(m, x, z);
        if (d > RimWidth * 1.4f) return 0f;
        float t = Mathf.Clamp01(1f - d / RimW(x, z));
        if (t <= 0f) return 0f;
        float n2 = Mathf.PerlinNoise(x * 0.02f + 11f, z * 0.02f + 5f), n3 = Mathf.PerlinNoise(x * 0.08f, z * 0.08f + 13f);
        return (RimHeight * (0.55f + 0.9f * n2) * (0.85f + 0.3f * n3) + (n3 - 0.5f) * 8f) * Mathf.Pow(t, 1.5f);
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

    // ---- Pinos procedurales: tronco torcido + copas en estrella irregulares y caidas + ramas finas. LOD0/LOD1. ----
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
        var rt = new System.Random(900 + variant * 17);   // tronco y copas: identicos en LOD0 y LOD1
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

            // Dos caras con vertices separados: iluminadas bien desde arriba y desde abajo.
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

    // Tubo de seccion poligonal siguiendo una curva (tronco y ramas).
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

    // 11) ASSET: plataforma OVALADA para base urbana futurista (losa de metal oxidado + cerco perimetral de malla de
    //     alambre con 4 aberturas). Cada ejecucion genera una variante distinta (ovalo y deformacion al azar) como
    //     Assets/Prefabs/UrbanBase/UrbanBase_####.prefab. Todo con collider solido; las aberturas quedan libres.
    const float BaseThick = 0.6f, FenceH = 3f, PanelW = 4f;

    [MenuItem("Tools/Terrain/11 Urban Base Platform (genera prefab)")]
    static void UrbanBase()
    {
        const string dir = "Assets/Prefabs/UrbanBase";
        Directory.CreateDirectory(dir);
        var rnd = new System.Random();
        string name = "UrbanBase_" + rnd.Next(1000, 9999);
        float rx = Rf(rnd, 20f, 25f), rz = Rf(rnd, 12f, 16f);
        float a1 = Rf(rnd, 0.02f, 0.06f), a2 = Rf(rnd, 0.02f, 0.05f), p1 = Rf(rnd, 0f, 6.28f), p2 = Rf(rnd, 0f, 6.28f);

        // Contorno oval con deformacion suave, remuestreado en tramos de ~PanelW.
        const int D = 720;
        var dense = new Vector2[D + 1];
        var cum = new float[D + 1];
        for (int i = 0; i <= D; i++)
        {
            float t = i * Mathf.PI * 2f / D, e = 1f + a1 * Mathf.Sin(2f * t + p1) + a2 * Mathf.Sin(3f * t + p2);
            dense[i] = new Vector2(rx * e * Mathf.Cos(t), rz * e * Mathf.Sin(t));
            if (i > 0) cum[i] = cum[i - 1] + Vector2.Distance(dense[i], dense[i - 1]);
        }
        int M = Mathf.Max(12, Mathf.RoundToInt(cum[D] / PanelW));
        var S = new Vector2[M];
        for (int j = 0, k = 0; j < M; j++)
        {
            float d = cum[D] * j / M;
            while (cum[k + 1] < d) k++;
            S[j] = Vector2.Lerp(dense[k], dense[k + 1], Mathf.InverseLerp(cum[k], cum[k + 1], d));
        }

        // Losa: tapa en abanico + pared lateral.
        var vt = new List<Vector3> { new Vector3(0, BaseThick, 0) };
        var uv = new List<Vector2> { Vector2.zero };
        var tr = new List<int>();
        for (int j = 0; j < M; j++) { vt.Add(new Vector3(S[j].x, BaseThick, S[j].y)); uv.Add(S[j] / 40f); }
        for (int j = 0; j < M; j++) { tr.Add(0); tr.Add(1 + (j + 1) % M); tr.Add(1 + j); }
        int b = vt.Count;
        for (int j = 0; j <= M; j++)
        {
            var p = S[j % M];
            float u = cum[D] * j / M / 40f;
            vt.Add(new Vector3(p.x, BaseThick, p.y)); uv.Add(new Vector2(u, BaseThick / 40f));
            vt.Add(new Vector3(p.x, 0f, p.y)); uv.Add(new Vector2(u, 0f));
        }
        for (int j = 0; j < M; j++)
        {
            int ti = b + 2 * j, bi = ti + 1, tn = ti + 2, bn = ti + 3;
            tr.Add(bi); tr.Add(ti); tr.Add(tn);
            tr.Add(bi); tr.Add(tn); tr.Add(bn);
        }
        var mesh = new Mesh { name = name + "_Mesh" };
        mesh.SetVertices(vt);
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(tr, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, $"{dir}/{name}_Mesh.asset");

        Material metal = MetalMat(), deck = DeckMat();
        var root = new GameObject(name);
        var pg = new GameObject("Platform");
        pg.transform.SetParent(root.transform, false);
        pg.AddComponent<MeshFilter>().sharedMesh = mesh;
        pg.AddComponent<MeshRenderer>().sharedMaterial = deck;
        pg.AddComponent<MeshCollider>().sharedMesh = mesh;

        // Cerco siguiendo el contorno; 4 aberturas de 2 paneles (~8 m) repartidas con algo de azar.
        var open = new bool[M];
        for (int g = 0; g < 4; g++) { int s = (M * g / 4 + rnd.Next(-1, 2) + M) % M; open[s] = open[(s + 1) % M] = true; }
        float y = BaseThick + FenceH / 2;
        IV.Clear(); IU.Clear(); IT.Clear();
        for (int i = 0; i < M; i++)
        {
            Vector2 A = Inset(S[i]), B = Inset(S[(i + 1) % M]);
            if (!open[i])
            {
                Vector3 d = new Vector3(B.x - A.x, 0f, B.y - A.y), mid = new Vector3(A.x, 0f, A.y) + d * 0.5f;
                var q = Quaternion.LookRotation(d);
                var pc = new GameObject("WireCollider");
                pc.transform.SetParent(root.transform, false);
                pc.transform.SetLocalPositionAndRotation(mid + Vector3.up * y, q);
                pc.transform.localScale = new Vector3(0.04f, FenceH, d.magnitude);
                pc.AddComponent<BoxCollider>();
                Lattice(new Vector3(A.x, BaseThick, A.y), new Vector3(B.x, BaseThick, B.y));
                Blk(root.transform, "Rail", mid + Vector3.up * (BaseThick + FenceH), new Vector3(0.1f, 0.1f, d.magnitude), metal, false).transform.localRotation = q;
            }
            if (!open[i] || !open[(i + M - 1) % M])
                Blk(root.transform, "Post", new Vector3(A.x, y + 0.1f, A.y), new Vector3(0.18f, FenceH + 0.2f, 0.18f), metal, true);
        }
        var fg = new GameObject("Fence");
        fg.transform.SetParent(root.transform, false);
        fg.AddComponent<MeshFilter>().sharedMesh = Bake($"{dir}/{name}_Fence.asset");
        fg.AddComponent<MeshRenderer>().sharedMaterial = metal;
        var go = PrefabUtility.SaveAsPrefabAsset(root, $"{dir}/{name}.prefab");
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        Selection.activeObject = go;
        EditorGUIUtility.PingObject(go);
        Debug.Log($"[UrbanBase] {name}: oval {rx * 2:0}x{rz * 2:0} m, {M} paneles, en {dir}");
    }

    // Enrejado como geometria real (barras finas visibles por ambos lados) en lugar de textura con alfa:
    // se ve a cualquier distancia y todos los paneles salen en UNA sola malla (1 draw call).
    static void Lattice(Vector3 a, Vector3 b)
    {
        const float step = 0.25f, w = 0.02f;
        Vector3 d = b - a, up = Vector3.up * FenceH, o = Vector3.up * w;
        float len = d.magnitude;
        Vector3 dir = d / len, ow = dir * w;
        for (float s = 0f; s <= len + 0.001f; s += step)
        {
            Vector3 p = a + dir * Mathf.Min(s, len);
            Both(p - ow, p - ow + up, p + ow + up, p + ow);
        }
        for (float h = 0f; h <= FenceH + 0.001f; h += step)
        {
            Vector3 p = Vector3.up * Mathf.Min(h, FenceH);
            Both(a + p - o, a + p + o, b + p + o, b + p - o);
        }
    }

    static void Both(Vector3 a, Vector3 b, Vector3 c, Vector3 d) { Q(a, b, c, d); Q(d, c, b, a); }

    static Vector2 Inset(Vector2 p) => p - p.normalized * 0.15f;

    static GameObject Blk(Transform p, string n, Vector3 pos, Vector3 size, Material m, bool col)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = n;
        g.transform.SetParent(p, false);
        g.transform.localPosition = pos;
        g.transform.localScale = size;
        g.GetComponent<Renderer>().sharedMaterial = m;
        if (!col) Object.DestroyImmediate(g.GetComponent<Collider>());
        return g;
    }

    // Misma chapa oxidada de la herramienta 10, con tiling mayor para la losa grande.
    static Material DeckMat()
    {
        const string path = MetalDir + "/RustMetalDeck.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        m = new Material(MetalMat()) { mainTextureScale = new Vector2(5f, 5f) };
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    // Malla de alambre tipo eslabon (rombos), recorte por alfa, tileable.
    static Material WireMat()
    {
        const string path = MetalDir + "/WireMesh.mat", tp = MetalDir + "/WireMesh_D.png";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        Directory.CreateDirectory(MetalDir);
        const int S = 128;
        var px = new Color[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = x / (float)S, v = y / (float)S;
                float a = Mathf.Abs(Mathf.Repeat((u + v) * 8f, 1f) - 0.5f), b = Mathf.Abs(Mathf.Repeat((u - v) * 8f, 1f) - 0.5f);
                px[y * S + x] = new Color(0.55f, 0.58f, 0.62f, (a > 0.44f || b > 0.44f) ? 1f : 0f);
            }
        Save(tp, px, S, false);
        var imp = (TextureImporter)AssetImporter.GetAtPath(tp);
        imp.alphaIsTransparency = true;
        imp.mipMapsPreserveCoverage = true;
        imp.alphaTestReferenceValue = 0.5f;
        imp.SaveAndReimport();
        m = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(tp);
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
        if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
        if (m.HasProperty("_AlphaClip")) m.SetFloat("_AlphaClip", 1f);
        if (m.HasProperty("_Mode")) m.SetFloat("_Mode", 1f);
        if (m.HasProperty("_Cutoff")) m.SetFloat("_Cutoff", 0.5f);
        if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0.7f);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.5f);
        m.EnableKeyword("_ALPHATEST_ON");
        m.renderQueue = 2450;
        m.mainTextureScale = new Vector2(4f, 3f);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    // 12) INTERIOR DE LABORATORIO en la MISMA escena (requiere SceneDoor.cs FUERA de la carpeta Editor).
    //     Ver el menu mas abajo. El interior queda lejos del mundo (InteriorOffset, +300 m en X por laboratorio).
    const string InteriorDir = "Assets/Prefabs/Interiors";
    static readonly Vector3 InteriorOffset = new Vector3(0f, 2000f, 0f);
    static readonly List<Vector3> IV = new List<Vector3>();
    static readonly List<Vector2> IU = new List<Vector2>();
    static readonly List<int> IT = new List<int>();
    static readonly List<Renderer> IR = new List<Renderer>();

    // Tuneles: ancho 2*TW, alto TH (arco de radio TW arriba), NS tramos por tunel con costillas cada 4 tramos.
    const int NS = 49, TK = 16;
    const float TW = 2f, TH = 4.5f, RibIn = 0.3f, SB = 0.5f, CB = 0.866025f;   // SB/CB = seno/coseno de 30 grados
    static readonly Vector3[] TP = new Vector3[NS + 1], TS = new Vector3[NS + 1];   // centro y lateral de cada seccion

    [MenuItem("Tools/Terrain/12 Lab Interior (seleccion)", true)]
    static bool LabOk() => Selection.activeGameObject != null && !EditorUtility.IsPersistent(Selection.activeGameObject);

    // 12) Interior de laboratorio en la MISMA escena: raiz "<Lab>_Interior" a InteriorOffset (+300 m en X por cada laboratorio).
    //     Selecciona UN edificio. Crea/reutiliza "Door_<lab>" (Trigger + ReturnPoint), genera 2 salas + 2 tuneles y el trigger "Exit".
    //     Si ya existe "<Lab>_Interior" no regenera (borralo antes). Guarda la escena despues.
    [MenuItem("Tools/Terrain/12 Lab Interior (seleccion)")]
    static void LabInterior()
    {
        var lab = Selection.activeGameObject;
        string labName = lab.name;
        string id = string.Concat(labName.Split(Path.GetInvalidFileNameChars())).Replace(' ', '_') + "_Interior";
        if (GameObject.Find(id) != null) { Debug.LogWarning($"[Interior] '{id}' ya existe. Borralo para regenerarlo (perderias lo que pusiste dentro)."); return; }

        lab.GetComponentsInChildren(IR);
        Bounds bd = default; bool has = false;
        foreach (var r in IR) { if (!has) { bd = r.bounds; has = true; } else bd.Encapsulate(r.bounds); }
        float R = has ? Mathf.Clamp(Mathf.Min(bd.extents.x, bd.extents.z) * 0.5f, 8f, 14f) : 10f, D = 2f * R + 13f;
        const float T = 0.4f;
        const int M = 64;
        Directory.CreateDirectory(InteriorDir);

        var dg = GameObject.Find("Door_" + labName);
        var door = dg != null ? dg.GetComponentInChildren<SceneDoor>(true) : null;
        if (door == null) door = MakeDoor(lab, bd, has);

        var rA = new Vector2[M]; var rB = new Vector2[M]; var gA = new bool[M]; var gB = new bool[M];
        var off = new Vector2(0f, D);
        for (int i = 0; i < M; i++)
        {
            float a = Mathf.PI * 2f * i / M;
            rA[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R;
            rB[i] = rA[i] + off;
        }
        Gap(rA, Vector2.zero, gA, new Vector2(0f, -1f), 1.5f);
        for (int k = 0; k < 2; k++)
        {
            float s = k * 2f - 1f;
            Gap(rA, Vector2.zero, gA, new Vector2(s * SB, CB), TW);
            Gap(rB, off, gB, new Vector2(s * SB, -CB), TW);
        }

        Wall(rA, gA, true, TH, T);
        Wall(rB, gB, true, TH, T);
        var lamps = new Vector3[2];
        for (int k = 0; k < 2; k++) { TunnelPath(k * 2f - 1f, R, D); Tunnel(false); lamps[k] = TP[NS / 2] + Vector3.up * 3f; }
        var walls = Bake(AssetDatabase.GenerateUniqueAssetPath($"{InteriorDir}/{id}_Walls.asset"));

        Disc(rA, Vector2.zero);
        Disc(rB, off);
        for (int k = 0; k < 2; k++) { TunnelPath(k * 2f - 1f, R, D); Tunnel(true); }
        var slab = Bake(AssetDatabase.GenerateUniqueAssetPath($"{InteriorDir}/{id}_Slab.asset"));

        int n = Object.FindObjectsByType<SceneDoor>(FindObjectsSortMode.None).Count(d => d.exit);
        var root = new GameObject(id);
        Undo.RegisterCreatedObjectUndo(root, "Lab Interior");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, lab.scene);
        root.transform.position = InteriorOffset + Vector3.right * 300f * n;
        Part(root, "Walls", walls, MetalMat());
        Part(root, "Slab", slab, DeckMat());
        var spawn = Mark(root, "PlayerSpawn", new Vector3(0f, 1.2f, -R + 3f)).transform;

        var ex = Mark(root, "Exit", new Vector3(0f, 1.5f, -R + 0.4f));
        ex.transform.localScale = new Vector3(3f, 3f, 1f);
        ex.AddComponent<BoxCollider>().isTrigger = true;
        var sd = ex.AddComponent<SceneDoor>(); sd.exit = true; sd.point = spawn;

        door.dest = spawn;
        EditorUtility.SetDirty(door);

        Lamp(root, new Vector3(0f, TH - 0.8f, 0f), R * 1.6f);
        Lamp(root, new Vector3(0f, TH - 0.8f, D), R * 1.6f);
        foreach (var lp in lamps) Lamp(root, lp, 12f);
        AssetDatabase.SaveAssets();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(lab.scene);
        Selection.activeGameObject = root;
        Debug.Log($"[Interior] '{id}' creado en esta escena a {root.transform.position} (2 salas de radio {R:0.#} m). Ubica 'Door_{labName}/Trigger' en la entrada del modelo y guarda la escena.");
    }

    static void Lamp(GameObject root, Vector3 pos, float range)
    {
        var l = Mark(root, "Light", pos).AddComponent<Light>();
        l.type = LightType.Point; l.range = range; l.intensity = 2f; l.color = new Color(0.75f, 0.9f, 1f); l.shadows = LightShadows.None;
    }

    // Marca como abertura los segmentos del contorno cuyos 2 vertices caen dentro de un pasillo de semiancho hw hacia la direccion d.
    static void Gap(Vector2[] p, Vector2 c, bool[] gap, Vector2 d, float hw)
    {
        for (int i = 0; i < p.Length; i++)
            if (In(p[i] - c, d, hw) && In(p[(i + 1) % p.Length] - c, d, hw)) gap[i] = true;
    }

    static bool In(Vector2 v, Vector2 d, float hw) => Vector2.Dot(v, d) > 0f && Mathf.Abs(v.x * d.y - v.y * d.x) <= hw;

    // Sala circular: piso con espesor 0.5 m (colision segura) y techo de una cara.
    static void Disc(Vector2[] r, Vector2 c2)
    {
        Vector3 c = new Vector3(c2.x, 0f, c2.y), up = Vector3.up * TH, dn = Vector3.down * 0.5f;
        for (int i = 0; i < r.Length; i++)
        {
            Vector3 a = new Vector3(r[i].x, 0f, r[i].y), b = new Vector3(r[(i + 1) % r.Length].x, 0f, r[(i + 1) % r.Length].y);
            T3(c, b, a);
            T3(c + up, a + up, b + up);
            T3(c + dn, a + dn, b + dn);
            Q(a + dn, a, b, b + dn);
        }
    }

    // Eje del tunel: curva de Bezier cubica que sale radialmente de A (30 grados al norte) y llega radialmente a B.
    // s = -1/+1 (lado). Los extremos se hunden 0.25 m en el muro para que el marco quede embebido en su espesor.
    static void TunnelPath(float s, float R, float D)
    {
        Vector2 d0 = new Vector2(s * SB, CB), d3 = new Vector2(s * SB, -CB);
        Vector2 p0 = d0 * (R - 0.25f), p3 = new Vector2(0f, D) + d3 * (R - 0.25f);
        float k = R * 0.65f;
        Vector2 p1 = p0 + d0 * k, p2 = p3 + d3 * k;
        for (int i = 0; i <= NS; i++)
        {
            float t = (float)i / NS, u = 1f - t;
            Vector2 p = u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
            Vector2 d = (3f * u * u * (p1 - p0) + 6f * u * t * (p2 - p1) + 3f * t * t * (p3 - p2)).normalized;
            TP[i] = new Vector3(p.x, 0f, p.y);
            TS[i] = new Vector3(d.y, 0f, -d.x);
        }
    }

    // Punto m del perfil en la seccion i. Perfil: pared izq, arco de radio r (centro a TH-TW), pared der.
    // sq = true proyecta el arco al cuadrado exterior (para rellenar las esquinas del marco).
    static Vector3 Sp(int i, int m, float r, bool sq)
    {
        Vector2 q;
        if (m == 0) q = new Vector2(-r, 0f);
        else if (m == TK + 2) q = new Vector2(r, 0f);
        else
        {
            float f = Mathf.PI - (m - 1) * Mathf.PI / TK, c = Mathf.Cos(f), s = Mathf.Sin(f), k = sq ? r / Mathf.Max(Mathf.Abs(c), s) : r;
            q = new Vector2(k * c, TH - TW + k * s);
        }
        return TP[i] + TS[i] * q.x + Vector3.up * q.y;
    }

    static float Rr(int s) => s % 4 == 0 ? TW - RibIn : TW;   // tramos con costilla: radio interior menor

    // Anillo plano en la seccion i entre el perfil de radio a y el de radio b (b cuadrado si sq).
    static void Rim(int i, float a, float b, bool sq)
    {
        for (int m = 0; m <= TK + 1; m++) Both(Sp(i, m, a, false), Sp(i, m + 1, a, false), Sp(i, m + 1, b, sq), Sp(i, m, b, sq));
    }

    // Tunel con costillas en arco (doble cara). floor = true genera solo el piso (va en la malla del piso).
    static void Tunnel(bool floor)
    {
        for (int s = 0; s < NS; s++)
        {
            if (floor) { Both(Sp(s, 0, TW, false), Sp(s, TK + 2, TW, false), Sp(s + 1, TK + 2, TW, false), Sp(s + 1, 0, TW, false)); continue; }
            float r = Rr(s);
            for (int m = 0; m <= TK + 1; m++) Both(Sp(s, m, r, false), Sp(s, m + 1, r, false), Sp(s + 1, m + 1, r, false), Sp(s + 1, m, r, false));
        }
        if (floor) return;
        for (int i = 1; i < NS; i++) if (Rr(i - 1) != Rr(i)) Rim(i, TW - RibIn, TW, false);
        Rim(0, Rr(0), TW, true);
        Rim(NS, Rr(NS - 1), TW, true);
    }

    // Trigger de entrada (escena exterior): grupo "Door_<lab>" en la raiz con "Trigger" (cubo unitario que se ubica y
    // escala con las herramientas Move/Scale normales; se ve como caja translucida) y "ReturnPoint" (esfera + flecha).
    static SceneDoor MakeDoor(GameObject lab, Bounds b, bool has)
    {
        Vector3 p = has ? new Vector3(b.center.x, b.min.y + 1.5f, b.min.z) : lab.transform.position + Vector3.up * 1.5f;
        var g = new GameObject("Door_" + lab.name);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(g, lab.scene);
        Undo.RegisterCreatedObjectUndo(g, "Interior Door");
        var tg = new GameObject("Trigger");
        tg.transform.SetParent(g.transform, false);
        tg.transform.position = p;
        tg.transform.localScale = new Vector3(3f, 3f, 1.5f);
        tg.AddComponent<BoxCollider>().isTrigger = true;
        var rp = new GameObject("ReturnPoint").transform;
        rp.SetParent(g.transform, false);
        rp.position = p + new Vector3(0f, -1.5f, -2.5f);
        var d = tg.AddComponent<SceneDoor>();
        d.point = rp;
        return d;
    }

    static GameObject Mark(GameObject root, string n, Vector3 pos)
    {
        var g = new GameObject(n);
        g.transform.SetParent(root.transform, false);
        g.transform.localPosition = pos;
        return g;
    }

    static void Part(GameObject root, string n, Mesh m, Material mat)
    {
        var g = Mark(root, n, Vector3.zero);
        g.isStatic = true;
        g.AddComponent<MeshFilter>().sharedMesh = m;
        g.AddComponent<MeshRenderer>().sharedMaterial = mat;
        g.AddComponent<MeshCollider>().sharedMesh = m;
    }

    static Mesh Bake(string path)
    {
        var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        m.SetVertices(IV); m.SetUVs(0, IU); m.SetTriangles(IT, 0);
        m.RecalculateNormals(); m.RecalculateTangents(); m.RecalculateBounds();
        AssetDatabase.CreateAsset(m, path);
        IV.Clear(); IU.Clear(); IT.Clear();
        return m;
    }

    // Triangulo con vertices propios y UV por proyeccion de caja (1 tile cada 4 m).
    static void T3(Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 n = Vector3.Cross(b - a, c - a).normalized;
        bool flat = Mathf.Abs(n.y) > 0.5f;
        Vector3 u = flat ? Vector3.right : new Vector3(-n.z, 0f, n.x), w = flat ? Vector3.forward : Vector3.up;
        Uv(a, u, w); Uv(b, u, w); Uv(c, u, w);
    }

    static void Uv(Vector3 p, Vector3 u, Vector3 w)
    {
        IT.Add(IV.Count); IV.Add(p); IU.Add(new Vector2(Vector3.Dot(p, u), Vector3.Dot(p, w)) * 0.25f);
    }

    static void Q(Vector3 a, Vector3 b, Vector3 c, Vector3 d) { T3(a, b, c); T3(a, c, d); }

    static Vector3 Side(Vector2[] p, int i, bool closed)
    {
        int n = p.Length, a = closed ? (i + n - 1) % n : Mathf.Max(i - 1, 0), b = closed ? (i + 1) % n : Mathf.Min(i + 1, n - 1);
        return Vector3.Cross(Vector3.up, new Vector3(p[b].x - p[a].x, 0f, p[b].y - p[a].y)).normalized;
    }

    // Muro con espesor a lo largo de una polilinea (cerrada o abierta); gap[i] = segmento i sin muro (abertura).
    static void Wall(Vector2[] p, bool[] gap, bool closed, float h, float t)
    {
        int n = p.Length, segs = closed ? n : n - 1;
        Vector3 up = Vector3.up * h;
        for (int i = 0; i < segs; i++)
        {
            if (gap[i]) continue;
            int j = (i + 1) % n;
            Vector3 pa = new Vector3(p[i].x, 0f, p[i].y), pb = new Vector3(p[j].x, 0f, p[j].y);
            Vector3 sa = Side(p, i, closed) * t * 0.5f, sb = Side(p, j, closed) * t * 0.5f;
            Vector3 aS = pa + sa, aO = pa - sa, bS = pb + sb, bO = pb - sb;
            Q(aS, aS + up, bS + up, bS);
            Q(bO, bO + up, aO + up, aO);
            Q(aS + up, aO + up, bO + up, bS + up);
            if (i == 0 ? (!closed || gap[segs - 1]) : gap[i - 1]) Q(aO, aO + up, aS + up, aS);
            if (i == segs - 1 ? (!closed || gap[0]) : gap[i + 1]) Q(bS, bS + up, bO + up, bO);
        }
    }

    // 15) ASSET: consola de nave espacial (1-3 modulos). Cada ejecucion genera una variante distinta como
    //     Assets/Prefabs/Consoles/Console_####.prefab: carcasa, pantallas, botones, teclados, interruptores,
    //     monitor superior opcional y cables que caen por detras y serpentean por el piso.
    //     UNA sola malla con un submesh por material (pantallas y botones emisivos) y un solo BoxCollider.
    static List<CombineInstance>[] CI;
    static Mesh CubeM;

    static void Cb(int m, Matrix4x4 p, float x, float y, float z, float sx, float sy, float sz)
    {
        CI[m].Add(new CombineInstance { mesh = CubeM, transform = p * Matrix4x4.TRS(new Vector3(x, y, z), Quaternion.identity, new Vector3(sx, sy, sz)) });
    }

    [MenuItem("Tools/Terrain/15 Console Panel (genera prefab)")]
    static void ConsolePanel()
    {
        const string dir = "Assets/Prefabs/Consoles";
        Directory.CreateDirectory(dir);
        string[] nm = { "Body", "Dark", "Screen", "Red", "Amber", "Green", "Cyan", "Cable" };   // 2..6 = emisivos
        Color[] cl = { Color.gray, new Color(.03f, .035f, .04f), new Color(.1f, .8f, .7f), new Color(1f, .1f, .1f), new Color(1f, .6f, 0f), new Color(.2f, 1f, .3f), new Color(.2f, .8f, 1f), new Color(.02f, .02f, .02f) };
        var mats = new Material[nm.Length];
        for (int i = 0; i < nm.Length; i++)
        {
            mats[i] = i == 0 ? MetalMat() : Mat(dir, nm[i], cl[i]);
            if (i < 2 || i > 6 || !mats[i].HasProperty("_EmissionColor")) continue;
            mats[i].EnableKeyword("_EMISSION");
            mats[i].SetColor("_EmissionColor", cl[i] * 1.5f);
            EditorUtility.SetDirty(mats[i]);
        }

        var rnd = new System.Random();
        string name = "Console_" + rnd.Next(1000, 9999);
        CubeM = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        CI = new List<CombineInstance>[nm.Length];
        for (int i = 0; i < nm.Length; i++) CI[i] = new List<CombineInstance>();

        const float MW = 0.9f;                       // ancho por modulo; frente = +Z, cables hacia -Z
        int n = rnd.Next(1, 4);
        float W = n * MW, x0 = -W / 2f;
        var I = Matrix4x4.identity;
        Cb(0, I, 0, .45f, 0, W, .9f, .7f);
        Cb(1, I, 0, .04f, .02f, W + .04f, .08f, .74f);

        for (int k = 0; k < n; k++)
        {
            float cx = x0 + (k + .5f) * MW;
            var D = Matrix4x4.TRS(new Vector3(cx, 1f, .05f), Quaternion.Euler(25f, 0f, 0f), Vector3.one);   // cubierta inclinada
            Cb(1, D, 0, 0, 0, MW - .04f, .06f, .66f);
            int kind = rnd.Next(3);
            if (kind == 0)       // pantalla grande + fila de botones
            {
                Cb(1, D, 0, .035f, -.12f, .74f, .02f, .3f);
                Cb(rnd.Next(2, 7), D, 0, .045f, -.12f, .68f, .02f, .24f);
                for (int b = 0; b < 6; b++) Cb(rnd.Next(3, 7), D, -.3f + b * .12f, .05f, .22f, .06f, .04f, .06f);
            }
            else if (kind == 1)  // teclado 3x4 + tira de pantalla + botones laterales
            {
                Cb(1, D, 0, .035f, -.22f, .7f, .02f, .1f);
                Cb(rnd.Next(2, 7), D, 0, .045f, -.22f, .64f, .02f, .06f);
                for (int r = 0; r < 4; r++)
                    for (int c = 0; c < 3; c++) Cb(1, D, -.22f + c * .1f, .06f, -.08f + r * .1f, .08f, .05f, .08f);
                for (int b = 0; b < 4; b++) Cb(rnd.Next(3, 7), D, .22f, .05f, -.08f + b * .1f, .06f, .04f, .06f);
            }
            else                 // matriz de botones + interruptores
            {
                for (int r = 0; r < 4; r++)
                    for (int c = 0; c < 6; c++) Cb(rnd.Next(4) == 0 ? 1 : rnd.Next(3, 7), D, -.3f + c * .12f, .05f, -.2f + r * .11f, .05f, .04f, .05f);
                for (int b = 0; b < 5; b++) Cb(0, D, -.24f + b * .12f, .07f, .24f, .025f, .1f, .025f);
            }
            if (rnd.Next(2) == 0)  // monitor superior
            {
                var M = Matrix4x4.TRS(new Vector3(cx, 1.45f, -.15f), Quaternion.Euler(-10f, 0f, 0f), Vector3.one);
                Cb(1, I, cx, 1.2f, -.22f, .1f, .2f, .1f);
                Cb(1, M, 0, 0, 0, .8f, .5f, .06f);
                Cb(rnd.Next(2, 7), M, 0, 0, .035f, .72f, .42f, .02f);
            }
        }

        // Cables: nacen en la cara trasera, caen al piso y serpentean.
        var v = new List<Vector3>(); var t = new List<int>();
        int nc = rnd.Next(3, 7);
        for (int k = 0; k < nc; k++)
        {
            Vector3 a = new Vector3(Rf(rnd, x0 + .1f, -x0 - .1f), Rf(rnd, .2f, .8f), -.35f);
            Vector3 b = new Vector3(Rf(rnd, x0 - 2f, -x0 + 2f), .03f, -Rf(rnd, 1f, 3f));
            var P = new Vector3[10]; var R = new float[10];
            for (int i = 0; i < 10; i++)
            {
                float f = i / 9f;
                P[i] = Vector3.Lerp(a, b, f);
                P[i].y = Mathf.Lerp(a.y, .03f, Mathf.Sqrt(f));
                P[i].x += Mathf.Sin(f * 6f + k) * .1f * f;
                R[i] = .025f;
            }
            Tube(v, t, P, R, 5);
        }
        var cm = new Mesh();
        cm.SetVertices(v); cm.SetTriangles(t, 0); cm.RecalculateNormals();
        CI[7].Add(new CombineInstance { mesh = cm, transform = I });

        var parts = new List<CombineInstance>(); var ms = new List<Material>();
        for (int i = 0; i < nm.Length; i++)
        {
            if (CI[i].Count == 0) continue;
            var mi = new Mesh();
            mi.CombineMeshes(CI[i].ToArray(), true, true);
            parts.Add(new CombineInstance { mesh = mi, transform = I });
            ms.Add(mats[i]);
        }
        var mesh = new Mesh { name = name + "_Mesh" };
        mesh.CombineMeshes(parts.ToArray(), false, false);
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, $"{dir}/{name}_Mesh.asset");

        var root = new GameObject(name);
        root.AddComponent<MeshFilter>().sharedMesh = mesh;
        root.AddComponent<MeshRenderer>().sharedMaterials = ms.ToArray();
        var bc = root.AddComponent<BoxCollider>();
        bc.center = new Vector3(0f, .6f, 0f); bc.size = new Vector3(W, 1.2f, .7f);
        var go = PrefabUtility.SaveAsPrefabAsset(root, $"{dir}/{name}.prefab");
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        Selection.activeObject = go;
        EditorGUIUtility.PingObject(go);
        Debug.Log($"[Console] {name}: {n} modulos, {nc} cables, en {dir}");
    }

    // 16) CRISTALES: racimos de puntas y estalagmitas PROCEDURALES (6 variantes de malla, sin FBX) de vidrio translucido y
    //     luminoso (violeta, rojo, amarillo; la luz sale solo del material emisivo, sin Lights). Aparecen SOLO en laderas
    //     inclinadas (>= CrystalMinSlope), sorteadas por todo el mapa, orientadas con la pendiente.
    //     Requiere Crystal.cs fuera de Editor. Al romperse (Damage) sueltan gemas apilables.
    //     ACUMULABLE: cada ejecucion AGREGA racimos nuevos (sin pisar los existentes); 17 borra todos.
    //     CrystalScale = tamano de los racimos (4 = ~9-14 m de alto antes de la variacion por instancia).
    const int CrystalSites = 400, CrystalVariants = 6;
    const float CrystalMinSlope = 30f, CrystalScale = 4f;
    const string CrDir = "Assets/Prefabs/CrystalsXL";

    [MenuItem("Tools/Terrain/16 Crystals (laderas)")]
    static void Crystals()
    {
        var ts = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None);
        if (ts.Length == 0) return;
        Directory.CreateDirectory(CrDir);
        string[] nm = { "Violet", "Red", "Yellow" };
        Color[] cl = { new Color(.6f, .1f, 1f), new Color(1f, .03f, .03f), new Color(1f, .85f, .02f) };
        // Los assets se crean una sola vez y se reutilizan en las siguientes pasadas.
        bool fresh = !File.Exists($"{CrDir}/Pick_Violet.prefab");
        Mesh gem = null; var meshes = new Mesh[CrystalVariants];
        if (fresh)
        {
            gem = Spikes($"{CrDir}/Gem.asset", 99, 1, .5f);
            for (int i = 0; i < CrystalVariants; i++) meshes[i] = Spikes($"{CrDir}/Cluster{i}.asset", 10 + i, 5 + i % 5, (2.2f + i * .25f) * CrystalScale);
        }
        var pk = new GameObject[3]; var cr = new GameObject[3 * CrystalVariants];
        for (int c = 0; c < 3; c++)
        {
            var m = fresh ? Glass(nm[c], cl[c]) : null;
            pk[c] = fresh ? CrPrefab(gem, m, $"{CrDir}/Pick_{nm[c]}.prefab", c, null) : AssetDatabase.LoadAssetAtPath<GameObject>($"{CrDir}/Pick_{nm[c]}.prefab");
            for (int k = 0; k < CrystalVariants; k++)
                cr[c * CrystalVariants + k] = fresh ? CrPrefab(meshes[k], m, $"{CrDir}/Crystal_{nm[c]}_{k}.prefab", c, pk) : AssetDatabase.LoadAssetAtPath<GameObject>($"{CrDir}/Crystal_{nm[c]}_{k}.prefab");
        }

        var root = GameObject.Find("Crystals");
        if (root == null) { root = new GameObject("Crystals"); Undo.RegisterCreatedObjectUndo(root, "Crystals"); }
        Physics.SyncTransforms();
        var sites = new List<Vector3>();
        foreach (Transform ch in root.transform) sites.Add(ch.position);   // lo ya colocado cuenta para la separacion
        var buf = new Collider[8]; var rnd = new System.Random();
        int quota = CrystalSites / ts.Length + 1, total = 0;
        foreach (var t in ts)
        {
            var td = t.terrainData; Vector3 p = t.transform.position, sz = td.size;
            int got = 0;
            for (int tries = 0; tries < quota * 80 && got < quota; tries++)
            {
                float u = Rf(rnd, 0f, 1f), v = Rf(rnd, 0f, 1f);
                if (td.GetSteepness(u, v) < CrystalMinSlope) continue;
                var c = new Vector3(p.x + u * sz.x, p.y + td.GetInterpolatedHeight(u, v), p.z + v * sz.z);
                if (sites.Exists(s => (s - c).sqrMagnitude < 64f * CrystalScale * CrystalScale) || Blocked(c + Vector3.up * 2f, buf)) continue;
                sites.Add(c); got++;
                var up = Quaternion.FromToRotation(Vector3.up, Vector3.Slerp(Vector3.up, td.GetInterpolatedNormal(u, v), .6f));
                int col = rnd.Next(3);
                for (int j = rnd.Next(1, 4); j > 0; j--)
                {
                    float wx = c.x + Rf(rnd, -2.5f, 2.5f) * CrystalScale, wz = c.z + Rf(rnd, -2.5f, 2.5f) * CrystalScale;
                    var g = (GameObject)PrefabUtility.InstantiatePrefab(cr[col * CrystalVariants + rnd.Next(CrystalVariants)], root.transform);
                    g.transform.SetPositionAndRotation(new Vector3(wx, p.y + t.SampleHeight(new Vector3(wx, 0f, wz)) - .1f, wz), up * Quaternion.Euler(0f, Rf(rnd, 0f, 360f), 0f));
                    g.transform.localScale = Vector3.one * Rf(rnd, .6f, 1.8f);
                    total++;
                }
            }
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
        Debug.Log($"[Crystals] +{total} racimos nuevos, {root.transform.childCount} en total (pendiente >= {CrystalMinSlope} grados). Si salen pocos, baja CrystalMinSlope; ejecuta de nuevo para sumar mas.");
    }

    // 17) Borra todos los cristales colocados por 16.
    [MenuItem("Tools/Terrain/17 Clear Crystals")]
    static void ClearCrystals()
    {
        var old = GameObject.Find("Crystals");
        if (old == null) return;
        Undo.DestroyObjectImmediate(old);
        UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
        Debug.Log("[Crystals] Borrados.");
    }

    // Vidrio: URP Lit transparente "premultiply" (la emision y el brillo no se atenuan con la transparencia).
    static Material Glass(string n, Color c)
    {
        string path = $"{CrDir}/Glass{n}.mat";
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        m.SetColor("_BaseColor", new Color(c.r, c.g, c.b, .4f));
        m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 1f); m.SetFloat("_Cull", 0f); m.SetFloat("_ZWrite", 0f);
        m.SetFloat("_SrcBlend", 1f); m.SetFloat("_DstBlend", 10f); m.SetFloat("_SrcBlendAlpha", 1f); m.SetFloat("_DstBlendAlpha", 10f);
        m.SetFloat("_Smoothness", 1f); m.SetFloat("_Metallic", 0f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.EnableKeyword("_ALPHAPREMULTIPLY_ON"); m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", c * 3f);
        m.renderQueue = 3000;
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static void Fc(List<Vector3> v, List<int> t, Vector3 a, Vector3 b, Vector3 c)
    {
        t.Add(v.Count); t.Add(v.Count + 1); t.Add(v.Count + 2);
        v.Add(a); v.Add(b); v.Add(c);
    }

    // Racimo de n puntas facetadas (prisma de 5-7 lados que se afina hasta la punta), la primera es la mayor y central.
    static Mesh Spikes(string path, int seed, int n, float hmax)
    {
        var r = new System.Random(seed);
        var v = new List<Vector3>(); var t = new List<int>();
        for (int i = 0; i < n; i++)
        {
            bool one = n == 1, main = i == 0;
            float h = main ? hmax : Rf(r, .25f, .7f) * hmax, rad = .05f + h * Rf(r, .06f, .13f);
            Vector2 o = main ? Vector2.zero : new Vector2(Rf(r, -1f, 1f), Rf(r, -1f, 1f)).normalized * Rf(r, .25f, .9f) * hmax / 2.5f;
            var q = one ? Quaternion.identity : Quaternion.Euler(Rf(r, -32f, 32f), Rf(r, 0f, 360f), Rf(r, -32f, 32f));
            var b = new Vector3(o.x, one ? 0f : -.08f * hmax, o.y);
            int s = r.Next(5, 8);
            var a = new Vector3[s]; var m = new Vector3[s];
            for (int k = 0; k < s; k++)
            {
                float ang = Mathf.PI * 2f * k / s;
                var d = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                a[k] = b + q * (d * rad);
                m[k] = b + q * (d * rad * .8f + Vector3.up * h * .72f);
            }
            Vector3 tip = b + q * new Vector3(rad * .2f, h, 0f);
            for (int k = 0; k < s; k++)
            {
                int j = (k + 1) % s;
                Fc(v, t, a[k], m[k], a[j]); Fc(v, t, a[j], m[k], m[j]); Fc(v, t, m[k], tip, m[j]);
            }
        }
        var mesh = new Mesh { name = Path.GetFileNameWithoutExtension(path) };
        mesh.SetVertices(v); mesh.SetTriangles(t, 0);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    // drops == null -> gema recolectable con Rigidbody; si no -> racimo minable.
    static GameObject CrPrefab(Mesh mesh, Material m, string path, int color, GameObject[] drops)
    {
        var root = new GameObject(Path.GetFileNameWithoutExtension(path));
        root.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = root.AddComponent<MeshRenderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Bounds b = mesh.bounds;
        if (drops == null)
        {
            root.AddComponent<Rigidbody>().mass = .1f;
            var sc = root.AddComponent<SphereCollider>(); sc.center = b.center; sc.radius = b.extents.y;
            root.AddComponent<CrystalPickup>().color = color;
        }
        else
        {
            var bc = root.AddComponent<BoxCollider>();
            bc.center = b.center; bc.size = new Vector3(b.size.x * .7f, b.size.y, b.size.z * .7f);
            var c = root.AddComponent<Crystal>(); c.color = color; c.drops = drops;
        }
        var go = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return go;
    }
}