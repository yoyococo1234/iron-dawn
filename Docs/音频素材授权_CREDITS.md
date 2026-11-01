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

## 加工说明

- 全部转成 **48 kHz / 16-bit / 单声道 WAV**（Unity 里短音效用 PCM 最稳，ogg 有解码延迟）。
- `cannon_fire`：取原始录音第一声，裁 0–1.8 s，尾部 200 ms 淡出。
- `explosion`：低频闷炸（hjm）+ 脆裂层（TinyWorlds）叠加后归一化，做成"载具爆炸"。
- `mg_fire`：裁 0–0.55 s。
- `hit_metal` / `hit_heavy`：各裁到 0.6 s / 1.2 s。
- 处理脚本：`work/process_sfx.py`（原始下载件在 `ArtSource/Audio/raw/`）。

## 待补

- **坦克履带"哗啦"循环音**还没找到合适的 CC0 素材：OpenGameArt 上没有专门的履带循环，
  ZapSplat 有但需要账号登录，Freesound 的 CC0 版本（qubodup《Tank Engine Loop》）下载同样要登录。
  目前履带行进由发动机音承担。要补的话，可选路径：
  1. 用户提供 ZapSplat / Freesound 账号，下 2–3 条履带循环；
  2. 用 `work/gen_sfx.py` 里的 `gen_track()` 程序化生成一条当底层（已写好，未启用）。
