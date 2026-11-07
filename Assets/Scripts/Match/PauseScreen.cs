using UnityEngine;

/// <summary>
/// P 键暂停（2026-11-07 用户要求）。
/// 单人游戏，暂停时 Time.timeScale = 0：AI、炮弹、对局计时、据点产出全部停住。
///
/// 注意和另外两个也会压 timeScale 的系统共存：
///   GameStartScreen（开局封面，开局前一直压 0）
///   GarageScreen（车库，打开时压 0）
/// 暂停前如果 timeScale 已经是 0（说明是它们压的），我们只记住"暂停前是 1"，恢复时不会把它们顶掉。
/// </summary>
public class PauseScreen : MonoBehaviour
{
    public static bool Paused { get; private set; }

    float _savedScale = 1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var go = new GameObject("PauseScreen");
        go.AddComponent<PauseScreen>();
        Object.DontDestroyOnLoad(go);
    }

    void Update()
    {
        if (!Input.GetKeyDown(KeyCode.P)) return;

        if (!Paused)
        {
            _savedScale = Time.timeScale > 0f ? Time.timeScale : 1f;
            Paused = true;
            Time.timeScale = 0f;
            AudioListener.pause = true;
        }
        else
        {
            Paused = false;
            Time.timeScale = _savedScale;
            AudioListener.pause = false;
        }
    }

    void OnGUI()
    {
        if (!Paused) return;

        var w = Screen.width;
        var h = Screen.height;

        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);
        GUI.color = Color.white;

        var style = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = Mathf.Max(20, Mathf.RoundToInt(h * 0.045f)),
            fontStyle = FontStyle.Bold,
        };
        style.normal.textColor = Color.white;

        GUI.Label(new Rect(0, h * 0.42f, w, h * 0.1f), "已暂停", style);

        style.fontSize = Mathf.Max(14, Mathf.RoundToInt(h * 0.025f));
        style.fontStyle = FontStyle.Normal;
        style.normal.textColor = new Color(1f, 1f, 1f, 0.75f);
        GUI.Label(new Rect(0, h * 0.52f, w, h * 0.08f), "按 P 继续 · 按 T 打开补给商店", style);
    }
}
