# 载具精细模型放这里

## 怎么用

把 GLB / FBX / Prefab 丢进这个目录，**文件名 = VehicleDef 的 id**，就会自动被用上：

```
Assets/Models/Vehicles/
  land_light.glb      ← 自动对应 VehicleDef id = land_light（轻坦）
  land_main.glb
  land_heavy.glb
  sea_gunboat.glb
  air_fighter.glb
  air_attacker.glb
```

也可以直接拖到 `VehicleDef.modelPrefab` 字段上（优先级更高）。

**没有模型也没关系**：会自动退化成方块占位，代码不会崩。

## 模型必须遵守的三条

| # | 要求 | 为什么 |
|---|---|---|
| 1 | **车头朝 -Z**（Unity 的 forward 是 +Z，但我们的约定是 -Z） | 不对的话在 `VehicleDef.modelYawOffset` 里填个角度修正 |
| 2 | 模型里要有子物体名叫 **`Turret`** | 炮塔转动靠它；没有的话炮塔转不了（会在 Console 报警告） |
| 3 | 模型里要有子物体名叫 **`Muzzle`** | 开火位置；没有的话退化成车头前方 |

名字不一样也行，去 `VehicleDef` 里改 `turretBoneName` / `muzzleBoneName`。

## 为什么要拆件

Lux3D 生成的是一整块 mesh，炮塔和车体连在一起 → **转不了**。

所以生成后要拆：
- 用 Lux3D 的 `part_split` 接口，或
- 在 Blender 里手动切开，把炮塔单独命名成 `Turret`

## 尺寸

模型不用管大小——`VehicleDef.modelScale` 可以整体缩放。但**炮塔的位置**是模型自带的，所以最好按真实比例做（坦克 4–6 米长）。

## 验收

把模型放进来后跑一次 `Tools → 铁皮黎明 → 生成阶段1测试场景`，看 Console：

- 出现 `使用精细模型：land_main` → 成功
- 出现 `没找到炮塔节点「Turret」` → 节点名不对
- 什么都没有、还是方块 → 文件名和 id 对不上
