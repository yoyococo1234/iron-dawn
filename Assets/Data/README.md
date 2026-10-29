# 数据资产（别手删）

这个目录里的 27 个 `.asset` 文件是**游戏的全部数值**，不是代码。

## 怎么改

Unity 里点开任意一个 → Inspector 里就是一张表 → 改数字 → 按 Play 立刻生效（不用重新编译）。

## 目录结构

| 目录 | 数量 | 内容 |
|---|---|---|
| `Ammo/` | 6 | 弹药：对装甲/软目标/空/海的伤害倍率（克制矩阵） |
| `Weapons/` | 8 | 武器：伤害、装填、射程、能装哪些弹 |
| `Vehicles/` | 6 | 载具：血量、装甲、速度、价格、挂什么武器 |
| `Zones/` | 4 | 战场区域：范围、能否占领、产金 |
| `Match/` | 1 | 对局与经济：复活涨幅、换车涨幅、各项收入 |
| `AI/` | 1 | AI 参数：视野、开火距离、换车阈值 |
| `Battlefield/` | 1 | 战场：两条出兵线、区域列表、金币箱点 |

## 关键数值在哪

| 你想调 | 改哪个文件 | 字段 |
|---|---|---|
| 一局多长 | `Match/Match_Default` | `respawnGrowth`（1.22）、`swapGrowth`（1.15） |
| 某台车多硬 | `Vehicles/Vehicle_xxx` | `maxHp`、`armorFront/Side/Rear` |
| 某种弹多疼 | `Ammo/Ammo_xxx` | `vsArmor`、`vsSoft`、`vsAir`、`vsSea` |
| 收入多少 | `Match/Match_Default` | `killRewardRate`、`captureIncome` |
| AI 多凶 | `AI/AI_Default` | `accuracy`、`aggression`、`visionRange` |

## 重新生成

菜单 `Tools → 铁皮黎明 → 生成默认数据`

**幂等**：已存在的资产只会被更新数值，不会删除、不会重复创建。
如果你手改过某些值，重跑会把它们刷回默认值——所以手改后别再跑。

## 安全规则（给未来的自己/协作者）

- ✅ 改数值：随便改
- ✅ 加新资产：复制现有资产改个名字
- ⚠️ 改字段名：会让旧值丢失，必须用 `[FormerlySerializedAs("旧名")]`
- ❌ 删资产：如果别处有引用，会变成 Missing
