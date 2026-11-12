using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 键位绑定（2026-11-11）。玩家在「开始界面 → 按键设置」里改，默认值就是原来的那套。
///
/// 三条约定：
///   1. **鼠标键不参与改键**（左键开火 / 右键开镜写死），只改键盘；
///   2. 同一个键不能绑两个动作 —— <see cref="TrySet"/> 会返回冲突的动作名，界面据此拒绝；
///   3. 存 PlayerPrefs，重开游戏保留。
///
/// 移动轴原本走 `Input.GetAxis("Vertical"/"Horizontal")`（旧 Input Manager 的轴，
/// 运行时改不了），所以这里改成读键自己算 -1~1，才能支持 WASD 改键。
/// </summary>
public static class Bindings
{
    public enum Act
    {
        Forward, Back, Left, Right,
        Fire, Scope, FreeLook, SwitchWeapon, MgMode,
        LockOn, Shop, Pause, Garage, Restart, Decoy,
    }

    public struct Entry
    {
        public Act act;
        public string label;
        public KeyCode def;
    }

    /// <summary>顺序即界面上的显示顺序。</summary>
    public static readonly Entry[] Table =
    {
        new Entry { act = Act.Forward,      label = "前进",       def = KeyCode.W },
        new Entry { act = Act.Back,         label = "后退",       def = KeyCode.S },
        new Entry { act = Act.Left,         label = "左转",       def = KeyCode.A },
        new Entry { act = Act.Right,        label = "右转",       def = KeyCode.D },
        new Entry { act = Act.Fire,         label = "开火（左键也可）", def = KeyCode.Space },
        new Entry { act = Act.Scope,        label = "开镜 / 拉近",  def = KeyCode.LeftShift },
        new Entry { act = Act.FreeLook,     label = "自由视角",     def = KeyCode.C },
        new Entry { act = Act.SwitchWeapon, label = "切换武器",     def = KeyCode.Q },
        new Entry { act = Act.MgMode,       label = "机枪模式",     def = KeyCode.V },
        new Entry { act = Act.LockOn,       label = "锁定目标",     def = KeyCode.LeftControl },
        new Entry { act = Act.Shop,         label = "补给商店",     def = KeyCode.Tab },
        new Entry { act = Act.Pause,        label = "暂停",        def = KeyCode.P },
        new Entry { act = Act.Garage,       label = "回车库换车",    def = KeyCode.G },
        // 2026-11-12 用户要求：删掉 R（重开一局）—— 改成按 P 暂停后在面板里选"是否重新开始"。
        new Entry { act = Act.Decoy,        label = "干扰弹（船）",  def = KeyCode.X },
    };

    static Dictionary<Act, KeyCode> _map;
    const string K = "Tank3D.Key.";

    static void Ensure()
    {
        if (_map != null) return;
        _map = new Dictionary<Act, KeyCode>();
        foreach (var e in Table)
            _map[e.act] = (KeyCode)PlayerPrefs.GetInt(K + e.act, (int)e.def);
    }

    public static KeyCode Get(Act a)
    {
        Ensure();
        return _map.TryGetValue(a, out var k) ? k : KeyCode.None;
    }

    public static string LabelOf(Act a)
    {
        foreach (var e in Table) if (e.act == a) return e.label;
        return a.ToString();
    }

    /// <summary>改键。撞到别的动作就返回 false，conflict 带出被占用的那个动作。</summary>
    public static bool TrySet(Act a, KeyCode k, out Act conflict)
    {
        Ensure();
        conflict = Act.Forward;
        if (k == KeyCode.None) return false;

        foreach (var e in Table)
        {
            if (e.act == a) continue;
            if (_map[e.act] == k) { conflict = e.act; return false; }
        }
        _map[a] = k;
        PlayerPrefs.SetInt(K + a, (int)k);
        PlayerPrefs.Save();
        return true;
    }

    public static void ResetAll()
    {
        Ensure();
        foreach (var e in Table)
        {
            _map[e.act] = e.def;
            PlayerPrefs.SetInt(K + e.act, (int)e.def);
        }
        PlayerPrefs.Save();
    }

    /// <summary>玩家没改过任何键（用来在界面上提示"当前是默认键位"）。</summary>
    public static bool IsDefault()
    {
        Ensure();
        foreach (var e in Table) if (_map[e.act] != e.def) return false;
        return true;
    }

    // ---------------- 查询 ----------------

    public static bool Down(Act a) => Input.GetKeyDown(Get(a));
    public static bool Held(Act a) => Input.GetKey(Get(a));

    /// <summary>前进/后退轴：-1..1。</summary>
    public static float AxisV
        => (Held(Act.Forward) ? 1f : 0f) - (Held(Act.Back) ? 1f : 0f);

    /// <summary>左右轴：-1..1（右为正，和 GetAxis("Horizontal") 一致）。</summary>
    public static float AxisH
        => (Held(Act.Right) ? 1f : 0f) - (Held(Act.Left) ? 1f : 0f);

    /// <summary>开火：绑定的键 **或** 鼠标左键。</summary>
    public static bool FireHeld => Held(Act.Fire) || Input.GetMouseButton(0);

    /// <summary>开镜/拉近：绑定的键 **或** 鼠标右键。</summary>
    public static bool ScopeHeld => Held(Act.Scope) || Input.GetMouseButton(1);
}
