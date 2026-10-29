using UnityEngine;

/// <summary>第三人称跟随相机：跟着坦克，平滑插值，带一点朝向偏移。</summary>
public class CameraFollow : MonoBehaviour
{
    public Transform target;

    void LateUpdate()
    {
        if (target == null) return;

        var desired = target.position + target.rotation * Config.CameraOffset;
        transform.position = Vector3.Lerp(transform.position, desired, Config.CameraLerp * Time.deltaTime);
        transform.LookAt(target.position + Vector3.up * 1.5f);
    }
}
