// Poner en CUALQUIER carpeta que NO sea Editor (ej. Assets/Scripts/NightFx.cs).
// Un unico controlador para todo: balizas de las antenas (2 materiales compartidos), lentes y haces de los reflectores.
// 10 ticks/s, sin Update por objeto, sin asignaciones, y solo escribe materiales cuando algo cambia.
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways, DisallowMultipleComponent]
public sealed class NightFx : MonoBehaviour
{
    public Material white, red, lens, beam;
    public Light sun;            // vacio = RenderSettings.sun o primera luz direccional
    public bool alwaysNight;     // para probar de dia

    Renderer[] beams = new Renderer[0];
    int tick = -1, qn = -1;
    bool w, r;

    void OnEnable()
    {
        Collect();
        if (!sun) sun = RenderSettings.sun;
        if (!sun)
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) { sun = l; break; }
    }

    public void Collect()
    {
        var all = GetComponentsInChildren<MeshRenderer>(true);
        var l = new List<Renderer>(all.Length);
        foreach (var m in all) if (m.sharedMaterial == beam) l.Add(m);
        beams = l.ToArray();
        tick = -1;
    }

    void Update()
    {
        float t = Time.realtimeSinceStartup;
        int k = (int)(t * 10f);
        if (k == tick) return;
        tick = k;

        float n = alwaysNight || !sun ? 1f : Mathf.InverseLerp(.1f, -.1f, -sun.transform.forward.y);
        float p = t % 2f;
        bool nw = p < .2f, nr = p >= 1f && p < 1.5f;     // blanca: destello; roja: pulso lento
        int nq = Mathf.RoundToInt(n * 20f);
        if (nw == w && nr == r && nq == qn) return;
        w = nw; r = nr; qn = nq; n = nq / 20f;

        if (white) white.color = Glow(1f, 1f, 1f, w ? 4f + 6f * n : .15f);
        if (red) red.color = Glow(1f, .08f, .08f, r ? 4f + 6f * n : .15f);
        if (lens) lens.color = Glow(1f, .95f, .8f, .1f + 8f * n);
        if (beam) beam.color = new Color(1f, 1f, 1f, n);
        bool on = n > .02f;
        foreach (var b in beams) if (b) b.enabled = on;   // de dia los haces ni se dibujan
    }

    static Color Glow(float r, float g, float b, float k) => new Color(r * k, g * k, b * k, 1f);
}