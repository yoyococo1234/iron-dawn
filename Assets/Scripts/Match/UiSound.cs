using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 界面音效（悬停 / 点击 / 购买 / 拒绝 / 开关面板）。
/// 片段全部在运行时合成，不依赖音频文件；走一条独立的 2D AudioSource，
/// ignoreListenerPause = true，所以暂停（AudioListener.pause）时界面照样有声。
/// </summary>
public static class UiSound
{
    public enum Kind { Hover, Click, Confirm, Coin, Deny, Open, Close }

    public static float Volume = 0.55f;

    static AudioSource _src;
    static readonly Dictionary<Kind, AudioClip> _clips = new Dictionary<Kind, AudioClip>();
    static float _lastHover = -1f;

    public static void Play(Kind k)
    {
        if (k == Kind.Hover)
        {
            // 鼠标扫过一排按钮时别连成一串
            if (Time.unscaledTime - _lastHover < 0.05f) return;
            _lastHover = Time.unscaledTime;
        }
        Ensure();
        var c = Get(k);
        if (c != null) _src.PlayOneShot(c, Mathf.Clamp01(Volume * Sfx.MasterVolume));
    }

    static void Ensure()
    {
        if (_src != null) return;
        var go = new GameObject("UiSound");
        Object.DontDestroyOnLoad(go);
        _src = go.AddComponent<AudioSource>();
        _src.playOnAwake = false;
        _src.spatialBlend = 0f;
        _src.ignoreListenerPause = true;
    }

    // ---------------- 合成 ----------------

    struct Seg
    {
        public float f0, f1, dur, amp, delay;
        public bool square;
        public Seg(float f0, float f1, float dur, float amp, float delay = 0f, bool square = false)
        { this.f0 = f0; this.f1 = f1; this.dur = dur; this.amp = amp; this.delay = delay; this.square = square; }
    }

    static AudioClip Get(Kind k)
    {
        if (_clips.TryGetValue(k, out var c) && c != null) return c;
        switch (k)
        {
            case Kind.Hover: c = Synth("hover", new Seg(2600f, 2900f, 0.028f, 0.16f)); break;
            case Kind.Click:
                c = Synth("click", new Seg(1150f, 720f, 0.055f, 0.34f), new Seg(2300f, 2300f, 0.018f, 0.10f)); break;
            case Kind.Confirm:
                c = Synth("confirm", new Seg(880f, 880f, 0.07f, 0.30f), new Seg(1320f, 1320f, 0.14f, 0.30f, 0.06f)); break;
            case Kind.Coin:
                c = Synth("coin", new Seg(1568f, 1568f, 0.06f, 0.26f), new Seg(2093f, 2093f, 0.20f, 0.30f, 0.055f)); break;
            case Kind.Deny:
                c = Synth("deny", new Seg(220f, 180f, 0.12f, 0.22f, 0f, true), new Seg(165f, 150f, 0.13f, 0.20f, 0.10f, true)); break;
            case Kind.Open: c = Synth("open", new Seg(480f, 1150f, 0.12f, 0.22f)); break;
            case Kind.Close: c = Synth("close", new Seg(1050f, 470f, 0.11f, 0.20f)); break;
        }
        _clips[k] = c;
        return c;
    }

    static AudioClip Synth(string name, params Seg[] segs)
    {
        const int rate = 44100;
        float total = 0f;
        foreach (var s in segs) total = Mathf.Max(total, s.delay + s.dur);
        int n = Mathf.CeilToInt((total + 0.02f) * rate);
        var data = new float[n];

        foreach (var s in segs)
        {
            int start = (int)(s.delay * rate);
            int len = Mathf.Max(1, (int)(s.dur * rate));
            double phase = 0;
            for (int i = 0; i < len && start + i < n; i++)
            {
                float t = i / (float)len;
                float f = Mathf.Lerp(s.f0, s.f1, t);
                phase += 2.0 * System.Math.PI * f / rate;
                float sn = Mathf.Sin((float)phase);
                // 方波用削顶正弦，没那么刺耳；正弦叠一点二次谐波，声音更"实"
                float w = s.square ? Mathf.Clamp(sn * 3f, -1f, 1f) * 0.6f
                    : sn * 0.85f + Mathf.Sin((float)phase * 2f) * 0.15f;
                float env = Mathf.Min(1f, i / (rate * 0.003f)) * Mathf.Exp(-t * 4.5f);
                data[start + i] += w * env * s.amp;
            }
        }
        for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(data[i], -1f, 1f);

        var clip = AudioClip.Create("ui_" + name, n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
