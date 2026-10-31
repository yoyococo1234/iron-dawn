# Lux3D 载具资产清单（铁皮黎明：2km 三域坦克战场）

- 生成时间：2026-10-31 09:21–09:31（Asia/Shanghai）
- 账号：Aholo「马头蒸菜」，模型：Lux3D **G1**（text-to-3D）
- 统一参数：`version=G1`、`style=photorealistic`、`faceCount=60000`、`outputFormat=[glb]`、`aiPredictSize=true`
- 输出目录：`D:\UnityProjects\Tank3D\ArtSource\Lux3D\`
- 结果预览图：`preview_all.png`（每台 3 视角：3/4 前视 / 侧视 / 俯视，自写 z-buffer 渲染，非官方截图）
- 提示词原文：`_prompts.txt`（与本文件第 2 节一致）

## 1. 资产表

| 载具 id | 文件名 | 格式 | 文件大小 | 三角面 | 顶点 | 实际消耗积分 | 状态 |
|---|---|---|---|---|---|---|---|
| land_light | land_light.glb | GLB 2.0（材质内嵌） | 9,427,444 B ≈ 8.99 MB | 59,590 | 63,160 | 约 20（API 未回传明细，见 §3） | ✅ 成功 |
| land_main | land_main.glb | GLB 2.0（材质内嵌） | 9,348,604 B ≈ 8.92 MB | 58,104 | 58,548 | 约 20 | ✅ 成功 |
| land_heavy | land_heavy.glb | GLB 2.0（材质内嵌） | 9,207,160 B ≈ 8.78 MB | 59,391 | 60,506 | 约 20 | ✅ 成功 |
| sea_gunboat | sea_gunboat.glb | GLB 2.0（材质内嵌） | 8,988,268 B ≈ 8.57 MB | 58,208 | 61,454 | 约 20 | ✅ 成功 |
| air_fighter | air_fighter.glb | GLB 2.0（材质内嵌） | 7,325,532 B ≈ 6.99 MB | 57,520 | 45,638 | 约 20 | ✅ 成功 |
| air_attacker | air_attacker.glb | GLB 2.0（材质内嵌） | 8,005,376 B ≈ 7.63 MB | 59,598 | 51,689 | 约 20 | ✅ 成功 |

**合计：6/6 成功，0 失败，约 120 积分。**

校验方式（全部通过）：文件头 magic = `glTF`、GLB 版本 2、`declared_length` 与实际字节数一致、JSON chunk 可解析、三角面 5.7 万～6.0 万（远高于「空壳」阈值）、每台内嵌 2 张贴图（`textures_embedded=2`，`textures_external=0`）、1 个 material、1 个 node。
另外对 6 台做了离线渲染目视核对（`preview_all.png`）：炮塔/炮管/负重轮与履带、舰炮/舰桥/栏杆、三角翼/平直翼与挂架均可辨认，非糊成一团的团块。

各台 Lux3D 任务号：land_light `4512662`、land_main `4512668`、land_heavy `4512674`、sea_gunboat `4512675`、air_fighter `4512686`、air_attacker `4512812`。

## 2. 提示词

### 2.1 统一模板

```
Low-poly realistic military game asset of {SUBJECT}, {FEATURES}. Military hardware realism, low-polygon game-ready mesh, grey-green camouflage paint scheme, hard-surface panel lines, weathered metal, no cartoon, no anime, no stylized exaggeration, single isolated vehicle, plain neutral grey background, no ground plane, no environment, no people, no text, centered composition, whole vehicle visible, correct real-world proportions ({SIZE}).
```

模板设计要点（保证 6 台风格统一）：
1. 固定开头 `Low-poly realistic military game asset of …` —— 低多边形 + 写实，压住「卡通化」倾向；
2. 固定风格段 `Military hardware realism / low-polygon game-ready mesh / grey-green camouflage` —— 军武写实 + 灰绿迷彩，6 台同一套词；
3. 固定负向词 `no cartoon, no anime, no stylized exaggeration` —— 防二次元/卡通；
4. 固定无背景段 `single isolated vehicle / plain neutral grey background / no ground plane / no environment` —— 只出单一物体，避免生成地台和场景；
5. 固定 `centered composition, whole vehicle visible` + 真实尺寸 `({SIZE})` —— 控制构图完整性与比例参考。

### 2.2 六台完整提示词（实际提交原文）

**land_light（轻坦，7.1 m × 2.2 m）**
```
Low-poly realistic military game asset of a light tank, twin-man turret with a short-barreled 76mm gun, caterpillar tracks with rubber-padded road wheels, compact low-silhouette hull, grey camouflage paint. Military hardware realism, low-polygon game-ready mesh, grey-green camouflage paint scheme, hard-surface panel lines, weathered metal, no cartoon, no anime, no stylized exaggeration, single isolated vehicle, plain neutral grey background, no ground plane, no environment, no people, no text, centered composition, whole vehicle visible, correct real-world proportions (7.1 m long, 2.2 m tall).
```

**land_main（主战坦克，8.6 m × 2.6 m）**
```
Low-poly realistic military game asset of a modern main battle tank, wedge-shaped angular turret with a long-barreled 120mm smoothbore main gun, six road wheels per side with steel caterpillar tracks, sloped frontal glacis armor, side skirt armor. Military hardware realism, low-polygon game-ready mesh, grey-green camouflage paint scheme, hard-surface panel lines, weathered metal, no cartoon, no anime, no stylized exaggeration, single isolated vehicle, plain neutral grey background, no ground plane, no environment, no people, no text, centered composition, whole vehicle visible, correct real-world proportions (8.6 m long, 2.6 m tall).
```

**land_heavy（重装坦克，10.2 m × 3.0 m）**
```
Low-poly realistic military game asset of a heavy assault tank, very thick slab-sided armor, boxy rectangular turret with a heavy long gun, bolted add-on armor blocks on the hull front and turret sides, wide tracks with armored side skirts. Military hardware realism, low-polygon game-ready mesh, grey-green camouflage paint scheme, hard-surface panel lines, weathered metal, no cartoon, no anime, no stylized exaggeration, single isolated vehicle, plain neutral grey background, no ground plane, no environment, no people, no text, centered composition, whole vehicle visible, correct real-world proportions (10.2 m long, 3.0 m tall).
```

**sea_gunboat（内河炮艇，18 m × 4.6 m）**
```
Low-poly realistic military game asset of a river gunboat, single enclosed main gun turret on the foredeck, raised armored bridge wheelhouse amidships, deck railings and vents, flat-bottomed shallow-draft grey steel hull. Military hardware realism, low-polygon game-ready mesh, grey-green camouflage paint scheme, hard-surface panel lines, weathered metal, no cartoon, no anime, no stylized exaggeration, single isolated vehicle, plain neutral grey background, no ground plane, no environment, no water, no people, no text, centered composition, whole vehicle visible, correct real-world proportions (18 m long, 4.6 m wide).
```

**air_fighter（战斗机，11 m 长 / 10.5 m 翼展）**
```
Low-poly realistic military game asset of a single-engine jet fighter aircraft, delta wing planform, single vertical tail fin, bubble canopy, nose air intake, landing gear retracted. Military hardware realism, low-polygon game-ready mesh, grey-green camouflage paint scheme, hard-surface panel lines, weathered metal, no cartoon, no anime, no stylized exaggeration, single isolated vehicle, plain neutral grey background, no ground plane, no environment, no people, no text, centered composition, whole vehicle visible, correct real-world proportions (11 m long, 10.5 m wingspan).
```

**air_attacker（攻击机，12 m 长）**
```
Low-poly realistic military game asset of a close air support attack aircraft, straight low-mounted wings, underwing weapon pylons carrying bombs and rocket pods, armored cockpit bathtub, single engine with rear exhaust. Military hardware realism, low-polygon game-ready mesh, grey-green camouflage paint scheme, hard-surface panel lines, weathered metal, no cartoon, no anime, no stylized exaggeration, single isolated vehicle, plain neutral grey background, no ground plane, no environment, no people, no text, centered composition, whole vehicle visible, correct real-world proportions (12 m long).
```

## 3. 失败项与原因

**最终无失败项（6/6 一次生成成功，没有触发重试）。** 过程中只有一次提交层面的拒绝，非生成失败：

- `air_attacker` 首次提交（09:25 左右）返回 `GENERATION_CONCURRENCY_LIMIT_EXCEEDED`（账号并发上限 5 个在跑任务）。该错误**不创建任务、不扣积分**；等 land_light / sea_gunboat 完成后重新提交即成功（任务号 4512812）。

积分消耗明细：Aholo 的 task 查询接口（`lux3d_get_task`）只返回 taskId / status / outputs，**不返回本次扣费数值**，因此「实际消耗积分」按官方口径（单件约 20 积分）估算为 6 × 20 ≈ 120，余额应从 3600 降到约 3480，需在控制台核对。若后续需要精确数字，只能登录 https://lux3d.aholo3d.com/ 看账单。

## 4. 一句话总结

**这批模型可以直接进 Unity**：6 台都是真实可辨认的低多边形写实军武资产、GLB 内嵌材质与贴图、约 5.8 万三角面/台，可直接拖进 `Assets/` 做正式载具；但有三点必须处理——**① 朝向不统一**（land_light / land_main / sea_gunboat / air_fighter / air_attacker 长轴沿 **+Z**，land_heavy 长轴沿 **+X**，需在 Unity 里对 land_heavy 绕 Y 轴转 90°）；**② 单位不是米**（`aiPredictSize` 给的是任意尺度，例如炮艇包围盒长 132、主战坦克长 20.8，进引擎后必须按真实尺寸归一化：炮艇 ×0.136、主战坦克 ×0.41、轻坦 ×0.81、重装 ×1.07、战斗机 ×0.62、攻击机 ×0.69）；**③ 面数偏高**（≈6 万三角面/台，6 台同屏再加地形对移动端不友好，建议 Unity 里生成 LOD 或降面到 1–2 万）。材质是内嵌的（每台 2 张贴图、1 个 PBR 材质），不需要额外找贴图文件。

### 附：包围盒（glTF 本地坐标，未乘节点变换）与归一化系数

| 载具 | bbox 尺寸 (X,Y,Z) | 长轴 | 目标长/宽 | 建议缩放 |
|---|---|---|---|---|
| land_light | 4.80 × 4.42 × 8.80 | Z | 长 7.1 m | ×0.81 |
| land_main | 9.80 × 8.43 × 20.80 | Z | 长 8.6 m | ×0.41 |
| land_heavy | 9.50 × 3.63 × 4.66 | X | 长 10.2 m | ×1.07（并绕 Y 转 90°） |
| sea_gunboat | 35.0 × 48.5 × 132.2 | Z | 长 18 m | ×0.136 |
| air_fighter | 14.45 × 7.33 × 17.74 | Z（翼展沿 X） | 长 11 m | ×0.62 |
| air_attacker | 20.01 × 6.32 × 17.53 | Z（翼展沿 X） | 长 12 m | ×0.69 |

> 高度一项偏大（如主战坦克 Y=8.43 vs 长 20.8），说明模型含天线/枪管等细长构件或生成时比例略有拉伸，进 Unity 后建议按「车长」这一维度做等比缩放，再目视微调高度，不要按 Y 轴独立缩放（会变形）。

### 附：目录内容

- `land_light.glb` / `land_main.glb` / `land_heavy.glb` / `sea_gunboat.glb` / `air_fighter.glb` / `air_attacker.glb` —— 正式交付资产（GLB，材质内嵌）
- `preview_all.png` —— 6 台 × 3 视角预览拼图
- `_prompts.txt` —— 提示词模板与 6 台原文
- `_raw/land_light_result.zip` —— Lux3D 原始 ZIP 样例（内含未内嵌材质的 `MODEL.glb` + 5 张 PBR filter 贴图），仅作留档，Unity 请用上面的 `textured.glb` 版
