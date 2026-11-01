using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 音效播放的统一入口。
///
/// 片段放在 Assets/Resources/Audio/ 下，用 Resources.Load 取（这样不用在场景/预制体里拖引用，
/// 程序生成的车也能直接出声）。所有一次性音效都建临时 AudioSource，播完自动销毁；
/// 同时发声数超过上限就丢新的，避免集群开火时音频爆掉。
/// </summary>
public static class Sfx
{
    // 片段名（对应 Assets/Resources/Audio/<name>.wav）
    public const string EngineLoop = "engine_loop";
    public const string TrackLoop = "track_loop";
    public const string CannonFire = "cannon_fire";
    public const string MgFire = "mg_fire";
    public const string Explosion = "explosion";
    public const string HitMetal = "hit_metal";
    public const string HitHeavy = "hit_heavy";

    public static float MasterVolume = 1f;
    public static int MaxConcurrent = 24;

    static readonly Dictionary<string, AudioClip> _cache = new Dictionary<string, AudioClip>();
    static int _active;

    public static AudioClip Get(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (_cache.TryGetValue(name, out var c)) return c;
        c = Resources.Load<AudioClip>("Audio/" + name);
        if (c == null) Debug.LogWarning($"[Sfx] 找不到音频片段 Audio/{name}");
        _cache[name] = c;
        return c;
    }

    /// <summary>在某个位置播一次性 3D 音效。</summary>
    public static void PlayAt(string name, Vector3 pos, float volume = 1f, float pitch = 1f,
                              float minDistance = 10f, float maxDistance = 400f)
    {
        var clip = Get(name);
        if (clip == null) return;
        if (_active >= MaxConcurrent) return;

        var go = new GameObject("Sfx_" + name);
        go.transform.position = pos;
        var src = go.AddComponent<AudioSource>();
        src.clip = clip;
        src.volume = Mathf.Clamp01(volume * MasterVolume);
        src.pitch = Mathf.Clamp(pitch, 0.5f, 2f);
        src.spatialBlend = 1f;
        src.rolloffMode = AudioRolloffMode.Logarithmic;
        src.minDistance = minDistance;
        src.maxDistance = maxDistance;
        src.dopplerLevel = 0.15f;
        src.playOnAwake = false;
        src.Play();

        _active++;
        Object.Destroy(go, clip.length / Mathf.Max(0.5f, src.pitch) + 0.15f);
        // 用一个轻量组件在销毁时把计数减回来
        go.AddComponent<SfxLifetime>();
    }

    /// <summary>2D（界面/自己车上的音效），不受空间衰减影响。</summary>
    public static void Play2D(string name, float volume = 1f, float pitch = 1f)
    {
        var clip = Get(name);
        if (clip == null) return;
        if (_active >= MaxConcurrent) return;

        var go = new GameObject("Sfx2D_" + name);
        var src = go.AddComponent<AudioSource>();
        src.clip = clip;
        src.volume = Mathf.Clamp01(volume * MasterVolume);
        src.pitch = Mathf.Clamp(pitch, 0.5f, 2f);
        src.spatialBlend = 0f;
        src.playOnAwake = false;
        src.Play();

        _active++;
        Object.Destroy(go, clip.length / Mathf.Max(0.5f, src.pitch) + 0.15f);
        go.AddComponent<SfxLifetime>();
    }

    /// <summary>给某个 Transform 挂一条循环音源（发动机、履带这类）。</summary>
    public static AudioSource CreateLoop(string name, Transform parent, float volume, float pitch,
                                         float minDistance = 12f, float maxDistance = 420f)
    {
        var clip = Get(name);
        if (clip == null || parent == null) return null;

        var go = new GameObject("Loop_" + name);
        go.transform.SetParent(parent, false);
        var src = go.AddComponent<AudioSource>();
        src.clip = clip;
        src.loop = true;
        src.volume = Mathf.Clamp01(volume * MasterVolume);
        src.pitch = Mathf.Clamp(pitch, 0.5f, 2f);
        src.spatialBlend = 1f;
        src.rolloffMode = AudioRolloffMode.Logarithmic;
        src.minDistance = minDistance;
        src.maxDistance = maxDistance;
        src.dopplerLevel = 0f;
        src.playOnAwake = false;
        src.Play();
        return src;
    }

    /// <summary>保证场景里有且只有一个 AudioListener（程序建的相机没有）。</summary>
    public static void EnsureListener()
    {
        if (Object.FindFirstObjectByType<AudioListener>() != null) return;
        var cam = Camera.main;
        if (cam == null) return;
        cam.gameObject.AddComponent<AudioListener>();
    }

    internal static void ReleaseOne() => _active = Mathf.Max(0, _active - 1);

    /// <summary>一次性音效的生命周期标记：销毁时把并发计数减回来。</summary>
    class SfxLifetime : MonoBehaviour
    {
        void OnDestroy() => ReleaseOne();
    }
}
