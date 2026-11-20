using System;
using System.Collections;
using UnityEngine;

namespace Runtime.Scripts.Audio
{
    public enum Sfx { Shoot, HitLight, HitHeavy, EnemyShoot, EnemyHit, Step, EnemyStep, Jump, MenuOpen, MenuClose, MissionComplete, GameOver, Land, Respawn, Pickup, ChargerWindup, ChargerHit }
    public enum Music { None, Exterior, Lab }

    /// <summary>
    /// Uso: AudioManager.Play(Sfx.Shoot); AudioManager.Play(Sfx.EnemyStep, pos); AudioManager.PlayMusic(Music.Lab);
    /// Poner UNA vez en la escena y arrastrar los clips en el inspector.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        [Serializable] public class Entry { public Sfx id; public AudioClip[] clips; [Range(0, 2)] public float volume = 1f; public Vector2 pitch = new Vector2(0.95f, 1.05f); [Tooltip("Multiplica la distancia a la que se oye (2 = el doble).")] public float reach = 1f; }
        [Serializable] public class Track { public Music id; public AudioClip clip; [Range(0, 1)] public float volume = 0.5f; }

        public Entry[] sfx;
        public Track[] music;
        public float fade = 1f;
        [Tooltip("Sonidos 3D: volumen pleno hasta X metros, silencio total a Y metros.")]
        public Vector2 range = new Vector2(10f, 100f);

        static AudioManager I;
        AudioSource _m;
        Music _cur;
        Coroutine _f;
        AudioSource[] _c, _x; // un canal fijo por efecto (+ uno de refuerzo para ganancia > 1): el siguiente corta al anterior, nunca se solapan

        void Awake()
        {
            if (I != null) { Destroy(gameObject); return; }
            I = this;
            DontDestroyOnLoad(gameObject);
            _m = gameObject.AddComponent<AudioSource>();
            _m.loop = true;
            int n = Enum.GetValues(typeof(Sfx)).Length;
            _c = new AudioSource[n];
            _x = new AudioSource[n];
            for (int i = 0; i < n; i++) { _c[i] = Make("Sfx_" + (Sfx)i); _x[i] = Make("Sfx_" + (Sfx)i + "_boost"); }
        }

        AudioSource Make(string name)
        {
            var a = new GameObject(name).AddComponent<AudioSource>();
            a.transform.SetParent(transform);
            a.playOnAwake = false;
            a.dopplerLevel = 0f;
            a.rolloffMode = AudioRolloffMode.Linear;
            return a;
        }

        void Fill(AudioSource a, AudioClip c, Vector3? at, float pitch, float vol, float reach)
        {
            a.Stop();
            a.transform.position = at ?? Vector3.zero;
            a.spatialBlend = at.HasValue ? 1f : 0f;
            a.minDistance = range.x * reach;
            a.maxDistance = range.y * reach;
            a.clip = c; a.pitch = pitch; a.volume = vol;
            a.Play();
        }

        public static void Play(Sfx id, Vector3? at = null) => Play(id, null, at);

        /// <summary>Igual que Play, pero con un clip propio (ej. el de cada item). Si clip es null usa los de la lista del inspector.</summary>
        public static void Play(Sfx id, AudioClip clip, Vector3? at = null)
        {
            if (I == null) return;
            Entry e = null;
            foreach (var x in I.sfx) if (x.id == id) { e = x; break; }
            if (clip == null)
            {
                if (e == null || e.clips == null || e.clips.Length == 0) return;
                clip = e.clips[UnityEngine.Random.Range(0, e.clips.Length)];
                if (clip == null) return;
            }
            float v = e != null ? e.volume : 1f, p = e != null ? UnityEngine.Random.Range(e.pitch.x, e.pitch.y) : 1f, r = e != null ? e.reach : 1f;
            I.Fill(I._c[(int)id], clip, at, p, Mathf.Min(v, 1f), r);
            // Ganancia > 1: un segundo canal sincronizado suma el excedente (AudioSource.volume tope = 1).
            if (v > 1f) I.Fill(I._x[(int)id], clip, at, p, v - 1f, r);
            else I._x[(int)id].Stop();
        }

        public static void PlayMusic(Music id)
        {
            if (I == null || I._cur == id) return;
            I._cur = id;
            if (I._f != null) I.StopCoroutine(I._f);
            I._f = I.StartCoroutine(I.Fade(id));
        }

        IEnumerator Fade(Music id)
        {
            float v0 = _m.volume;
            for (float t = 0; t < fade; t += Time.unscaledDeltaTime) { _m.volume = Mathf.Lerp(v0, 0f, t / fade); yield return null; }
            _m.Stop();
            foreach (var t in music)
            {
                if (t.id != id || t.clip == null) continue;
                _m.clip = t.clip;
                _m.Play();
                for (float k = 0; k < fade; k += Time.unscaledDeltaTime) { _m.volume = Mathf.Lerp(0f, t.volume, k / fade); yield return null; }
                _m.volume = t.volume;
                break;
            }
        }
    }
}