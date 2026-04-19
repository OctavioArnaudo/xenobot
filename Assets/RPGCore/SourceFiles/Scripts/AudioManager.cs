using UnityEngine;
using System.Collections;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("AudioSources — configurar clip, volumen y pitch aquí")]
    public AudioSource srcBiomaA;
    public AudioSource srcBiomaB;

    [Header("Transición")]
    [Range(0f, 10f)] public float crossfadeDuration = 3f;

    private AudioSource _current;
    private Coroutine _fade;
    private float _volBiomaA;
    private float _volBiomaB;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        _volBiomaA = srcBiomaA.volume;
        _volBiomaB = srcBiomaB.volume;

        srcBiomaA.loop = srcBiomaB.loop = true;
        srcBiomaA.volume = srcBiomaB.volume = 0f;

        srcBiomaA.Play();
        srcBiomaB.Play();
    }

    public void Play(AudioSource target)
    {
        if (target == _current) return;
        if (_fade != null) StopCoroutine(_fade);
        _fade = StartCoroutine(Crossfade(target));
    }

    IEnumerator Crossfade(AudioSource target)
    {
        float fromVol = _current != null ? _current.volume : 0f;
        float toVol = target == srcBiomaA ? _volBiomaA : _volBiomaB;
        float elapsed = 0f;

        while (elapsed < crossfadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / crossfadeDuration);
            if (_current != null) _current.volume = Mathf.Lerp(fromVol, 0f, t);
            target.volume = Mathf.Lerp(0f, toVol, t);
            yield return null;
        }

        if (_current != null) _current.volume = 0f;
        target.volume = toVol;
        _current = target;
        _fade = null;
    }
}