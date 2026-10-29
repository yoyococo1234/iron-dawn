using UnityEngine;

/// <summary>
/// 全局可调参数。改这里重跑 ArenaBuilder 就能重建场景，不用手动摆物体。
/// </summary>
public static class Config
{
    // ---------- 战场 ----------
    public const float ArenaSize = 120f;      // 正方形战场边长（米）
    public const float TileSize = 4f;         // 格子边长（米）
    public const float GroundY = 0f;

    // 水域：一个矩形区域（相对战场中心）
    public static readonly Rect WaterRect = new Rect(-60f, 10f, 120f, 50f); // x, z, 宽, 深
    public const float WaterY = 0.05f;

    // ---------- 玩家坦克 ----------
    public const float TankSpeed = 14f;          // 前进速度 m/s
    public const float TankTurnSpeed = 70f;      // 车体转向 度/秒
    public const float TurretSensitivity = 3.2f; // 鼠标灵敏度
    public const float FireCooldown = 0.45f;
    public const float ProjectileSpeed = 90f;
    public const float ProjectileLife = 4f;

    // ---------- 相机 ----------
    public static readonly Vector3 CameraOffset = new Vector3(0f, 11f, -15f);
    public const float CameraLerp = 6f;

    // ---------- 生成器 ----------
    public const int RandomSeed = 20261029;
    public const int WallClusterCount = 14;   // 墙体簇数量
}
