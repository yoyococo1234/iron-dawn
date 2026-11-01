using UnityEngine;

/// <summary>
/// 载具音频：发动机循环音，随车速变调、变响。
///
/// 挂在载具根节点上（VehicleFactory 生成时自动加）。三条域都用同一套引擎声，
/// 但飞机/炮艇的音高范围不一样——飞机转速更高，炮艇更闷。
/// </summary>
[RequireComponent(typeof(VehiclePhysics))]
public class VehicleAudio : MonoBehaviour
{
    [Tooltip("怠速时的音高")]
    public float idlePitch = 0.80f;
    [Tooltip("满速时的音高")]
    public float maxPitch = 1.45f;
    [Tooltip("怠速音量")]
    public float idleVolume = 0.18f;
    [Tooltip("满速音量")]
    public float maxVolume = 0.62f;
    [Tooltip("车速多少算满速（m/s）")]
    public float fullSpeed = 14f;

    VehiclePhysics _phys;
    AudioSource _engine;
    float _pitch = 1f;
    float _vol;

    void Start()
    {
        _phys = GetComponent<VehiclePhysics>();
        Sfx.EnsureListener();

        var def = _phys != null ? _phys.def : null;
        float basePitch = idlePitch;
        float topPitch = maxPitch;
        float range = fullSpeed;

        if (def != null)
        {
            switch (def.domain)
            {
                case Domain.Air:
                    basePitch = 0.95f; topPitch = 1.7f; range = 60f;
                    break;
                case Domain.Sea:
                    basePitch = 0.65f; topPitch = 1.15f; range = 16f;
                    break;
                default:
                    basePitch = 0.80f; topPitch = 1.45f; range = 14f;
                    break;
            }
        }

        _pitch = basePitch;
        _vol = idleVolume;
        _engine = Sfx.CreateLoop(Sfx.EngineLoop, transform, _vol, _pitch, 14f, 450f);
        if (_engine != null)
        {
            _engine.pitch = basePitch;
            // 每台车随机一点，避免集群时完全同相
            _engine.time = Random.Range(0f, 1.5f);
        }

        // 履带循环音只给陆战车（飞机/炮艇没有履带）
        if (def == null || def.domain == Domain.Land)
        {
            _track = Sfx.CreateLoop(Sfx.TrackLoop, transform, 0f, 0.8f, 8f, 320f);
            if (_track != null)
            {
                _track.volume = 0f;
                _track.time = Random.Range(0f, 3f);
            }
        }

        _fullPitch = topPitch;
        _fullRange = Mathf.Max(1f, range);
    }

    AudioSource _track;

    float _fullPitch = 1.45f;
    float _fullRange = 14f;

    void Update()
    {
        if (_engine == null) return;

        float v = _phys != null ? Mathf.Abs(_phys.speed) : 0f;
        float t = Mathf.Clamp01(v / _fullRange);

        float targetPitch = Mathf.Lerp(idlePitch, _fullPitch, Mathf.Pow(t, 0.7f));
        float targetVol = Mathf.Lerp(idleVolume, maxVolume, Mathf.Pow(t, 0.5f));

        _pitch = Mathf.Lerp(_pitch, targetPitch, Time.deltaTime * 4f);
        _vol = Mathf.Lerp(_vol, targetVol, Time.deltaTime * 4f);

        _engine.pitch = _pitch;
        _engine.volume = Mathf.Clamp01(_vol * Sfx.MasterVolume);

        // 履带：静止时几乎没声，越跑越响、音高越高（履带板滚过的节奏跟着变速）
        if (_track != null)
        {
            float tv = Mathf.Clamp01(v / Mathf.Max(0.5f, _fullRange * 0.75f));
            _trackVol = Mathf.Lerp(_trackVol, tv * 0.55f, Time.deltaTime * 3.5f);
            _trackPitch = Mathf.Lerp(_trackPitch, Mathf.Lerp(0.72f, 1.35f, tv), Time.deltaTime * 3.5f);
            _track.volume = Mathf.Clamp01(_trackVol * Sfx.MasterVolume);
            _track.pitch = _trackPitch;
        }
    }

    float _trackVol;
    float _trackPitch = 1f;
}
