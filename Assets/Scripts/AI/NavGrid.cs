using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 粗网格寻路（2 km 地图 → 20 m 一格 → 100×100 = 1 万格）。
///
/// 为什么不用 NavMesh：4097 高度图烘 NavMesh 很慢、内存也吃；粗网格一次采样 1 万个点，
/// 几十毫秒就建好，A* 在 1 万格上几乎瞬间出结果，5~10 台 AI 完全够用。
///
/// 每格存三样东西：地面高度、坡度、水深。能不能走由"谁在走"决定
/// （坦克爬坡上限不同、涉水深度不同），所以格子本身不写死可行走。
/// </summary>
public class NavGrid : MonoBehaviour
{
    public static NavGrid Instance;

    [Header("网格")]
    [Tooltip("格子边长（米）。20 m 在 2 km 图上是 100×100")]
    public float cellSize = 20f;

    [Header("运行时数据")]
    public int size = 100;
    public float[] heights;
    public float[] slopes;
    public float[] depths;

    Vector3 _origin;
    readonly List<int> _open = new List<int>();
    readonly Dictionary<int, int> _cameFrom = new Dictionary<int, int>();
    readonly Dictionary<int, float> _gScore = new Dictionary<int, float>();

    public float BuildMs { get; private set; }

    void Awake()
    {
        Instance = this;
        Build();
    }

    public void Build()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        size = Mathf.CeilToInt(BattlefieldTerrain.MapSize / cellSize);
        _origin = new Vector3(-BattlefieldTerrain.MapSize * 0.5f, 0f, -BattlefieldTerrain.MapSize * 0.5f);

        heights = new float[size * size];
        slopes = new float[size * size];
        depths = new float[size * size];

        for (int z = 0; z < size; z++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector3 c = CellCenter(x, z);
                int i = x + z * size;
                heights[i] = BattlefieldTerrain.SampleHeight(c.x, c.z);
                slopes[i] = BattlefieldTerrain.SampleSteepness(c.x, c.z);
                depths[i] = BattlefieldTerrain.WaterLevel - heights[i];
            }
        }

        sw.Stop();
        BuildMs = sw.ElapsedMilliseconds;
        Debug.Log($"[NavGrid] {size}×{size} 格（{cellSize} m/格）建成，用时 {BuildMs} ms");
    }

    public Vector3 CellCenter(int x, int z)
        => _origin + new Vector3((x + 0.5f) * cellSize, 0f, (z + 0.5f) * cellSize);

    public bool WorldToCell(Vector3 p, out int x, out int z)
    {
        x = Mathf.FloorToInt((p.x - _origin.x) / cellSize);
        z = Mathf.FloorToInt((p.z - _origin.z) / cellSize);
        return x >= 0 && x < size && z >= 0 && z < size;
    }

    int Idx(int x, int z) => x + z * size;

    /// <summary>按域判定可行走：陆看坡度+涉水，海看吃水（水深必须够），空全图可飞。</summary>
    public enum NavMode { Land, Sea, Air }

    /// <summary>这一格这台车能不能走。</summary>
    public bool Walkable(int x, int z, float maxSlope, float maxWaterDepth)
    {
        if (x < 0 || x >= size || z < 0 || z >= size) return false;
        int i = Idx(x, z);
        if (slopes[i] > maxSlope) return false;
        if (depths[i] > maxWaterDepth) return false;
        return true;
    }

    /// <summary>按域判定（海：水深 ≥ 吃水；空：全图；陆：坡度+涉水）。</summary>
    public bool Walkable(int x, int z, NavMode mode, float maxSlope, float waterParam)
    {
        if (x < 0 || x >= size || z < 0 || z >= size) return false;
        int i = Idx(x, z);
        switch (mode)
        {
            case NavMode.Sea:  return depths[i] >= waterParam;      // waterParam = 吃水深度
            case NavMode.Air:  return true;
            default:           return slopes[i] <= maxSlope && depths[i] <= waterParam;
        }
    }

    /// <summary>坡度越陡走越慢，给 A* 加代价。</summary>
    float ExtraCost(int x, int z)
    {
        int i = Idx(x, z);
        return 1f + slopes[i] / 45f;
    }

    /// <summary>A* 找路（按域判定可通行）。返回世界坐标点列表（含终点），找不到返回 false。</summary>
    public bool FindPath(Vector3 from, Vector3 to, NavMode mode, float maxSlope, float waterParam,
                         List<Vector3> outPath, int maxIterations = 20000)
    {
        outPath.Clear();
        if (!WorldToCell(from, out int sx, out int sz)) return false;
        if (!WorldToCell(to, out int tx, out int tz)) return false;

        // 目标格不可走 → 找附近最近的可走格
        if (!Walkable(tx, tz, mode, maxSlope, waterParam))
            if (!NearestWalkable(tx, tz, mode, maxSlope, waterParam, out tx, out tz)) return false;
        if (!Walkable(sx, sz, mode, maxSlope, waterParam))
            if (!NearestWalkable(sx, sz, mode, maxSlope, waterParam, out sx, out sz)) return false;

        _open.Clear();
        _cameFrom.Clear();
        _gScore.Clear();

        int start = Idx(sx, sz), goal = Idx(tx, tz);
        _gScore[start] = 0f;
        _open.Add(start);
        var fScore = new Dictionary<int, float> { [start] = Heuristic(sx, sz, tx, tz) };

        int iter = 0;
        while (_open.Count > 0 && iter++ < maxIterations)
        {
            // 取 f 最小的（1 万格规模，线性找足够快）
            int bestPos = 0;
            float bestF = float.MaxValue;
            for (int i = 0; i < _open.Count; i++)
            {
                float f = fScore.TryGetValue(_open[i], out var v) ? v : float.MaxValue;
                if (f < bestF) { bestF = f; bestPos = i; }
            }
            int cur = _open[bestPos];
            _open.RemoveAt(bestPos);

            if (cur == goal) { Reconstruct(cur, outPath); return outPath.Count > 0; }

            int cx = cur % size, cz = cur / size;
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0) continue;
                    int nx = cx + dx, nz = cz + dz;
                    if (!Walkable(nx, nz, mode, maxSlope, waterParam)) continue;

                    // 斜走时不允许"切角"（两侧都得能走）
                    if (dx != 0 && dz != 0)
                    {
                        if (!Walkable(cx + dx, cz, mode, maxSlope, waterParam)) continue;
                        if (!Walkable(cx, cz + dz, mode, maxSlope, waterParam)) continue;
                    }

                    int ni = Idx(nx, nz);
                    float step = (dx != 0 && dz != 0) ? 1.4142f : 1f;
                    float tentative = _gScore[cur] + step * ExtraCost(nx, nz);

                    if (_gScore.TryGetValue(ni, out var old) && tentative >= old) continue;

                    _cameFrom[ni] = cur;
                    _gScore[ni] = tentative;
                    fScore[ni] = tentative + Heuristic(nx, nz, tx, tz);
                    if (!_open.Contains(ni)) _open.Add(ni);
                }
            }
        }
        return false;
    }

    float Heuristic(int ax, int az, int bx, int bz)
    {
        float dx = Mathf.Abs(ax - bx), dz = Mathf.Abs(az - bz);
        return (dx + dz) + (1.4142f - 2f) * Mathf.Min(dx, dz);   // 八方向启发
    }

    void Reconstruct(int goal, List<Vector3> outPath)
    {
        var rev = new List<int>();
        int cur = goal;
        while (_cameFrom.TryGetValue(cur, out var prev)) { rev.Add(cur); cur = prev; }
        rev.Add(cur);
        rev.Reverse();
        foreach (var i in rev) outPath.Add(CellCenter(i % size, i / size));
    }

    bool NearestWalkable(int x, int z, NavMode mode, float maxSlope, float water, out int rx, out int rz)
    {
        for (int r = 1; r <= 8; r++)
        {
            for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Mathf.Abs(dx) != r && Mathf.Abs(dz) != r) continue;
                    int nx = x + dx, nz = z + dz;
                    if (Walkable(nx, nz, mode, maxSlope, water)) { rx = nx; rz = nz; return true; }
                }
        }
        rx = x; rz = z;
        return false;
    }
}
