# 音频素材来源与授权（CREDITS）

游戏里用到的所有音效都来自 **OpenGameArt.org**，均为 CC0 或 CC-BY（可商用）。
CC-BY 的条目**必须在报告书/游戏内标注作者**，下面这份就是标注清单。

| 游戏内文件 | 原始素材 | 作者 | 授权 | 来源 |
|---|---|---|---|---|
| `Resources/Audio/engine_loop.wav` | Engine-loop heavy vehicle/tank | Nayckron（协作 qubodup） | **CC-BY 3.0** | https://opengameart.org/content/engine-loop-heavy-vehicletank |
| `Resources/Audio/cannon_fire.wav` | Cannon fire | Thimras | CC0 | https://opengameart.org/content/cannon-fire |
| `Resources/Audio/explosion.wav` | ① Big low frequency explosion boom ② Explosion | ① Hansjörg Malthaner (Varkalandar) ② TinyWorlds | ① **CC-BY 3.0** ② CC0 | ① https://opengameart.org/content/big-low-frequency-explosion-boom ② https://opengameart.org/content/explosion-0 |
| `Resources/Audio/mg_fire.wav` | Light Machine Gun | KuraiWolf | **CC-BY 4.0** | https://opengameart.org/content/light-machine-gun |
| `Resources/Audio/hit_metal.wav` | clank1（Mechanical Sounds） | BMacZero | CC0 | https://opengameart.org/content/mechanical-sounds |
| `Resources/Audio/hit_heavy.wav` | Cannon hit | Thimras | CC0 | https://opengameart.org/content/cannon-hit |
| `Resources/Audio/track_loop.wav` | **从视频音轨抠取**：M-1 Tank: Close up of Treads Rolling（22.5–27.5 s） | YouTube 上传者 | ⚠ **非 CC 授权，风险项** | https://www.youtube.com/watch?v=_bSJpWYUg-s |
| `Resources/Audio/engine_loop_jet.wav` | 战斗机引擎：① Jet Constant Noise ② Constant Jet Noise - Interior（两条混合） | craigsmith | **CC0** | ① https://freesound.org/people/craigsmith/sounds/437888/ ② https://freesound.org/people/craigsmith/sounds/437923/ |
| `Resources/Audio/engine_loop_prop.wav` | 攻击机引擎：G06-18_C-47 2 Engine Prop Idle | craigsmith | **CC0** | https://freesound.org/people/craigsmith/sounds/437838/ |

## 加工说明

- 全部转成 **48 kHz / 16-bit / 单声道 WAV**（Unity 里短音效用 PCM 最稳，ogg 有解码延迟）。
- `cannon_fire`：取原始录音第一声，裁 0–1.8 s，尾部 200 ms 淡出。
- `explosion`：低频闷炸（hjm）+ 脆裂层（TinyWorlds）叠加后归一化，做成"载具爆炸"。
- `mg_fire`：裁 0–0.55 s。
- `hit_metal` / `hit_heavy`：各裁到 0.6 s / 1.2 s。
- 处理脚本：`work/process_sfx.py`（原始下载件在 `ArtSource/Audio/raw/`）。

## 待补

- **坦克履带"哗啦"循环音**：已用真实录像音轨补上（`track_loop.wav`），但**授权是风险项**：
  - ZapSplat 上那套 "Steady rattling tracks of a driving tank"（快/中/慢三条）**全部是付费内容**
    （页面标注 Access Premium Content，免费账号下不到，2026-11-01 实测确认）
  - OpenGameArt 没有专门的履带循环；Freesound 的 CC0 版本下载需要登录
  - 所以从 YouTube 视频音轨里抠了 5 秒（`work/gen_track_from_video.py`，earshot 选出最佳窗口）
  - **提交前要么换成可商用素材，要么在报告书里明确标注这是 demo 用途**
  - 备选：`work/gen_sfx.py` 里的 `gen_track()`（纯合成）、
    `work/gen_track_from_samples.py`（用 CC0 金属撞击录音编排，已跑通，产物被覆盖了）


---

## 2026-11-05 新增：命中反馈音效（穿透系统）

来源：**Red Eclipse** 官方音效包，OpenGameArt 分发
https://opengameart.org/content/red-eclipse-sounds

| 文件 | 用途 | 包内原始文件 | 授权 |
|---|---|---|---|
| `Resources/Audio/hit_ricochet.ogg` | 跳弹 | `sounds/weapons/ricochet.ogg` | CC-BY-SA 3.0 |
| `Resources/Audio/hit_nonpen.ogg` | 未击穿 | `sounds/sfx/bounce.ogg` | CC-BY-SA 3.0 |
| `Resources/Audio/hit_pen.ogg` | 击穿 | `sounds/weapons/explode2.ogg` | CC-BY-SA 3.0 |

⚠️ **CC-BY-SA 3.0 要求署名 + 相同方式共享**。提交前二选一：
1. 在报告书/游戏内保留署名（"Sound effects from Red Eclipse, CC-BY-SA 3.0"）；
2. 换成 CC0 素材（Freesound 的 CC0 下载需要登录，本次没走通）。
---

## 2026-11-05 修订：命中音效换成 Freesound CC0

上一版用的 Red Eclipse 包（CC-BY-SA）声音太"轻武器"，换成 Freesound 上 **CC0** 的坦克/金属素材：

| 文件 | 内容 | 来源 | 授权 |
|---|---|---|---|
| `Resources/Audio/hit_ricochet.wav` | 跳弹（截 0.40~0.80 s，单一金属跳弹声） | https://freesound.org/s/148827/ | CC0 |
| `Resources/Audio/hit_nonpen.wav` | 未击穿（截 0.15~1.00 s，金属撞击余响） | https://freesound.org/s/697758/ | CC0 |
| `Resources/Audio/hit_pen.wav` | 击穿（截 0.00~1.50 s，坦克炮击发/命中爆响） | https://freesound.org/s/127845/ | CC0 |

选段用 earshot 打分（跳弹 z+4.6、未击穿 z+3.3、击穿 z+5.5），
ffmpeg 裁切 + 淡出 + 48 kHz/单声道归一。

**注**：这些是 Freesound 的**预览 mp3**（未登录无法下原始 wav），码率 54~71 kbps，
当短音效用没问题；要更高音质需要登录后下原文件。