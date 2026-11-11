using UnityEngine;

/// <summary>
/// 排查用：启动时按命令行参数关掉场景里的部分物件，这样**一次打包就能测多种组合**，
/// 不用每换一个变量就重新构建一次。
///
/// 支持参数（可组合）：
///   -noprops        关掉整个 Props（树/楼/石/灌木）
///   -nobuildings    只关建筑
///   -notrees        只关树
///   -norocks        只关岩石和灌木
///   -nocolliders    保留模型但删掉所有碰撞体
///   -noshadow       所有物件关阴影
///   -lowfx          关掉海面泡沫/反射之类的高开销效果
///
/// 正式发布时不需要它 —— 不带参数启动时它什么都不做。
/// </summary>
public class PropDebugToggle : MonoBehaviour
{
    void Awake()
    {
        var args = System.Environment.GetCommandLineArgs();
        bool Has(string k)
        {
            foreach (var a in args)
                if (string.Equals(a, k, System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        bool noProps = Has("-noprops");
        bool noBuild = Has("-nobuildings") || noProps;
        bool noTrees = Has("-notrees") || noProps;
        bool noRocks = Has("-norocks") || noProps;
        bool noColl = Has("-nocolliders");
        bool noShadow = Has("-noshadow");

        if (!noBuild && !noTrees && !noRocks && !noColl && !noShadow && !Has("-lowfx")) return;

        var sb = new System.Text.StringBuilder("[PropDebugToggle] ");
        int n = 0;

        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            string nm = t.name;
            if (nm == "Buildings" && noBuild) { t.gameObject.SetActive(false); n++; continue; }
            if (nm == "Trees" && noTrees) { t.gameObject.SetActive(false); n++; continue; }
            if ((nm == "Rocks" || nm == "Bushes") && noRocks) { t.gameObject.SetActive(false); n++; continue; }
            if (nm == "TreeCollider" && (noColl || noTrees)) { t.gameObject.SetActive(false); n++; continue; }
        }

        if (noColl)
        {
            foreach (var c in GetComponentsInChildren<Collider>(true))
            {
                if (c is TerrainCollider) continue;
                c.enabled = false;
                n++;
            }
        }

        if (noShadow)
        {
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                n++;
            }
        }

        sb.Append("关闭 ").Append(n).Append(" 项；参数: ");
        if (noBuild) sb.Append("nobuildings ");
        if (noTrees) sb.Append("notrees ");
        if (noRocks) sb.Append("norocks ");
        if (noColl) sb.Append("nocolliders ");
        if (noShadow) sb.Append("noshadow ");
        Debug.Log(sb.ToString());

        if (Has("-lowfx"))
        {
            foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (mb == null) continue;
                var tn = mb.GetType().Name;
                if (tn.Contains("PlanarReflection") || tn.Contains("SeaFoamWake"))
                {
                    mb.enabled = false;
                    Debug.Log("[PropDebugToggle] 关掉 " + tn);
                }
            }
        }
    }
}
