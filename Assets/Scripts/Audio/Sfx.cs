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
    public const string EngineLoop = "engine_loop";            // 陆战车（柴油机）
    public const string EngineLoopBoat = "engine_loop_boat";   // 炮艇（船用柴油机 + 水声）
    public const string EngineLoopJet = "engine_loop_jet";     // 喷气机（涡轮啸叫）
    public const string EngineLoopProp = "engine_loop_prop";   // 螺旋桨机（转桨轰鸣）
    public const string TrackLoop = "track_loop";
    public const string CannonFire = "cannon_fire";
    public const string MgFire = "mg_fire";
    public const string Explosion = "explosion";
    public const string HitMetal = "hit_metal";
    public const string HitHeavy = "hit_heavy";

    // 命中反馈三种（穿透系统）：跳弹 / 未击穿 / 击穿
    public const string HitRicochet = "hit_ricochet";
    public const string HitNonPen = "hit_nonpen";
    public const string HitPen = "hit_pen";
    // 机枪 / 机炮打中装甲：叮（不是炮弹那种砰）
    public const string HitMg = "hit_mg";

    // 命中 / 被击中 / 击杀 的语音播报（豆包音频生成，见 work/doubao_gen_v2.py）
    // 跳弹只留 2 句；"车体受损"是坦克专用，军舰不播（用 VoHitSea）
    public static readonly string[] VoRicochet = { "vo_ricochet_1", "vo_ricochet_2" };
    public static readonly string[] VoNonPen = { "vo_nonpen_1", "vo_nonpen_2", "vo_nonpen_3" };
    public static readonly string[] VoPen = { "vo_pen_1", "vo_pen_2", "vo_pen_3" };
    public static readonly string[] VoHit = { "vo_hit_1", "vo_hit_2", "vo_hit_3" };
    // 非陆战载具（军舰 / 飞机）不用"车体受损"这句，只播另外两句
    public static readonly string[] VoHitNoHull = { "vo_hit_1", "vo_hit_3" };
    public static readonly string[] VoKill = { "vo_kill_1", "vo_kill_2", "vo_kill_3" };

    static int _lastVo = -1;
    /// <summary>最后播放的语音文件名（调试/自测用）。</summary>
    public static string LastVo = "";

    // ---- 语音闸门（2026-11-06 用户要求：一句没播完之前不要再叠一句）----
    // 机枪扫射时每 0.1 s 就有一次命中结算，每发都会想播"打穿了"，
    // 不拦的话会变成一串叠在一起的语音。这里用一条独立的 AudioSource，
    // 上一句没播完就直接丢弃新的（击杀可以插队打断）。
    static AudioSource _voSource;
    static float _voEndAt;
    static float _voLastAt = -99f;

    /// <summary>当前有没有语音还在播。</summary>
    public static bool VoiceBusy => _voSource != null && _voSource.isPlaying && Time.time < _voEndAt;

    /// <summary>从一组语音里随机挑一句（尽量不连着重复同一句）。</summary>
    /// <param name="interrupt">true = 打断正在播的那句（击杀、被击中这种重要播报用）</param>
    public static void PlayRandomVo(string[] pool, float volume = 0.9f, bool interrupt = false)
    {
        if (pool == null || pool.Length == 0) return;

        // 上一句还在播 → 直接不播（除非是重要播报要插队）
        if (!interrupt)
        {
            if (VoiceBusy) return;
            if (Time.time - _voLastAt < 0.35f) return;      // 两句之间至少留 0.35 s，别接得太紧
        }

        int i = Random.Range(0, pool.Length);
        if (pool.Length > 1 && i == _lastVo) i = (i + 1) % pool.Length;
        _lastVo = i;

        var clip = Get(pool[i]);
        if (clip == null) return;

        if (_voSource == null)
        {
            var go = new GameObject("SfxVoice");
            _voSource = go.AddComponent<AudioSource>();
            _voSource.playOnAwake = false;
            _voSource.spatialBlend = 0f;      // 2D：语音永远听得清
            _voSource.loop = false;
        }

        _voSource.Stop();
        _voSource.clip = clip;
        _voSource.volume = Mathf.Clamp01(volume * MasterVolume);
        _voSource.pitch = 1f;
        _voSource.Play();

        LastVo = pool[i];
        _voLastAt = Time.time;
        _voEndAt = Time.time + clip.length;
    }

    public static float MasterVolume = 1f;
    public static int MaxConcurrent = 40;

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
