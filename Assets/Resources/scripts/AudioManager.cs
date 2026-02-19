using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class AudioManager : MonoBehaviour
{
    // ===== Bootstrap / Singleton =====
    private static AudioManager _instance;

    public static AudioManager Instance
    {
        get
        {
            EnsureInstance();
            return _instance;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        EnsureInstance();
    }

    private static void EnsureInstance()
    {
        if (_instance != null) return;

        _instance = FindAnyObjectByType<AudioManager>();
        if (_instance != null) return;

        var go = new GameObject(nameof(AudioManager));
        _instance = go.AddComponent<AudioManager>();
    }

    [Header("Pool")]
    [SerializeField] private int sePoolSize = 12;

    [Header("Volumes (0..1)")]
    [SerializeField, Range(0f, 1f)] private float seVolume = 0.8f;
    [SerializeField, Range(0f, 1f)] private float bgmVolume = 0.8f;

    private const string SE_ROOT = "se/";
    private const string BGM_ROOT = "bgm/";

    private AudioSource _bgmSource;
    private readonly List<AudioSource> _seSources = new List<AudioSource>();

    private readonly Dictionary<string, AudioClip> _seCache = new Dictionary<string, AudioClip>();
    private readonly Dictionary<string, AudioClip> _bgmCache = new Dictionary<string, AudioClip>();

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);

        InitializeAudioSources();
        ApplyVolumes();
    }

    private void InitializeAudioSources()
    {
        _bgmSource = gameObject.AddComponent<AudioSource>();
        _bgmSource.playOnAwake = false;
        _bgmSource.loop = true;
        _bgmSource.spatialBlend = 0f;

        _seSources.Clear();
        for (int i = 0; i < Mathf.Max(1, sePoolSize); i++)
        {
            var src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = false;
            src.spatialBlend = 0f; // 2D
            _seSources.Add(src);
        }
    }

    private void ApplyVolumes()
    {
        seVolume = Mathf.Clamp01(seVolume);
        bgmVolume = Mathf.Clamp01(bgmVolume);

        if (_bgmSource != null) _bgmSource.volume = bgmVolume;

        for (int i = 0; i < _seSources.Count; i++)
        {
            if (_seSources[i] != null) _seSources[i].volume = seVolume;
        }
    }

    public void SetSEVolume(float v01)
    {
        seVolume = Mathf.Clamp01(v01);
        ApplyVolumes();
    }

    public float GetSEVolume() => seVolume;

    public void SetBGMVolume(float v01)
    {
        bgmVolume = Mathf.Clamp01(v01);
        ApplyVolumes();
    }

    public float GetBGMVolume() => bgmVolume;

    public void PlaySE(string fileName, float volumeScale = 1f, float value01 = 0.5f)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return;

        var yjsClip = LoadCachedClip(_seCache, SE_ROOT, "yjs");
        var normalClip = LoadCachedClip(_seCache, SE_ROOT, fileName);

        var clip = IsYjsStore.isYjsMode ? yjsClip : normalClip;
        if (clip == null) return;

        var src = FindFreeSESource();
        if (src == null) return;

        float v = Mathf.Clamp01(value01);

        float pitch = Mathf.Pow(2f, (v - 0.5f) * 2f);
        src.pitch = pitch;
        src.PlayOneShot(clip, Mathf.Clamp01(volumeScale));
    }



    private AudioSource FindFreeSESource()
    {
        for (int i = 0; i < _seSources.Count; i++)
        {
            var s = _seSources[i];
            if (s != null && !s.isPlaying)
                return s;
        }
        return null;
    }

    public void PlayBGM(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return;

        var yjsClip = LoadCachedClip(_bgmCache, BGM_ROOT, "bigyajue");
        var nomalClip = LoadCachedClip(_bgmCache, BGM_ROOT, fileName);

        var clip = IsYjsStore.isYjsMode ? yjsClip : nomalClip;
        if (clip == null) return;

        if (_bgmSource == null) return;

        // 常にリスタート
        _bgmSource.Stop();
        _bgmSource.clip = clip;
        _bgmSource.volume = bgmVolume;
        _bgmSource.Play();
    }

    public void StopBGM()
    {
        if (_bgmSource == null) return;
        _bgmSource.Stop();
        _bgmSource.clip = null;
    }

    private static AudioClip LoadCachedClip(Dictionary<string, AudioClip> cache, string root, string fileName)
    {
        var key = root + fileName;

        if (cache.TryGetValue(key, out var cached))
            return cached;

        var clip = Resources.Load<AudioClip>(key);
        if (clip == null)
        {
            Debug.LogWarning($"AudioManager: AudioClip not found in Resources: \"{key}\"");
            cache[key] = null;
            return null;
        }

        cache[key] = clip;
        return clip;
    }
}
