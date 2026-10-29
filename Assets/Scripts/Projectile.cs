using UnityEngine;

/// <summary>子弹：直线飞，超时或撞到东西就销毁。</summary>
public class Projectile : MonoBehaviour
{
    float _age;

    void Update()
    {
        _age += Time.deltaTime;
        if (_age >= Config.ProjectileLife) Destroy(gameObject);
    }

    void OnCollisionEnter(Collision c)
    {
        Destroy(gameObject);
    }
}
