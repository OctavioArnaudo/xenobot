using System;
using System.Collections;
using UnityEngine;

namespace Runtime.Scripts.Audio
{
    public enum Sfx { Shoot, HitLight, HitHeavy, EnemyShoot, EnemyHit, Step, EnemyStep, Jump, MenuOpen, MenuClose, MissionComplete, GameOver }
    public enum Music { None, Exterior, Lab }

    /// <summary>
    /// Uso: AudioManager.Play(Sfx.Shoot); AudioManager.Play(Sfx.EnemyStep, pos); AudioManager.PlayMusic(Music.Lab);
    /// Poner UNA vez en la escena y arrastrar los clips en el inspector.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        [Serializable] public class Entry { public Sfx id; public AudioClip[] clips; [Range(0, 1)] public float volume = 1f; public Vector2 pitch = new Vector2(0.95f, 1.05f); }
        [Serializable] public class Track { public Music id; public AudioClip clip; [Range(0, 1)] public float volume = 0.5f; }

        public Entry[] sfx;
        public Track[] music;
        public float fade = 1f;

        static AudioManager I;
        AudioSource _s, _m;
        Music _cur;
        Coroutine _f;

        void Awake()
        {
            if (I != null) { Destroy(gameObject); return; }
            I = this;
            DontDestroyOnLoad(gameObject);
            _s = gameObject.AddComponent<AudioSource>();
            _m = gameObject.AddComponent<AudioSource>();
            _m.loop = true;
        }

        public static void Play(Sfx id, Vector3? at = null)
        {
            if (I == null) return;
            foreach (var e in I.sfx)
            {
                if (e.id != id || e.clips == null || e.clips.Length == 0) continue;
                var c = e.clips[UnityEngine.Random.Range(0, e.clips.Length)];
                if (c == null) return;
                if (at.HasValue) AudioSource.PlayClipAtPoint(c, at.Value, e.volume);
                else { I._s.pitch = UnityEngine.Random.Range(e.pitch.x, e.pitch.y); I._s.PlayOneShot(c, e.volume); }
                return;
            }
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