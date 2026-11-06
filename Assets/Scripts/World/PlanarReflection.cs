using UnityEngine;

/// <summary>
/// 水面平面反射：用一台"镜像相机"把水面以上的场景渲染到 RenderTexture，
/// 水面着色器再按屏幕坐标采样 —— 港口、城市、天空就映进水里了。
///
/// Built-in 管线没有现成的平面反射，这里是标准做法（镜像相机 + 斜裁剪面）。
/// 挂在水面物体上即可，会自动找主相机。
/// </summary>
[ExecuteAlways]
public class PlanarReflection : MonoBehaviour
{
    [Tooltip("反射贴图分辨率（越高越清晰，也越费）")]
    public int textureSize = 1024;
    [Tooltip("哪些层参与反射")]
    public LayerMask reflectLayers = ~0;
    [Tooltip("裁剪面偏移，防止水面自身被反射进去")]
    public float clipPlaneOffset = 0.07f;
    [Tooltip("材质里的反射贴图属性名")]
    public string texProperty = "_ReflectionTex";

    Camera _cam;
    Camera _mainCam;
    RenderTexture _rt;
    Material _mat;

    void OnEnable()
    {
        var mr = GetComponent<MeshRenderer>();
        if (mr != null) _mat = mr.sharedMaterial;
        CreateCamera();
    }

    void OnDisable()
    {
        if (_cam != null)
        {
            _cam.targetTexture = null;
            DestroyImmediate(_cam.gameObject);
            _cam = null;
        }
        if (_rt != null)
        {
            if (_mat != null) _mat.SetTexture(texProperty, null);
            _rt.Release();
            DestroyImmediate(_rt);
            _rt = null;
        }
    }

    void CreateCamera()
    {
        if (_cam != null) return;
        var go = new GameObject("WaterReflectionCam");
        go.hideFlags = HideFlags.DontSave;
        _cam = go.AddComponent<Camera>();
        _cam.enabled = false;                       // 手动 Render
        _cam.cullingMask = reflectLayers;
        _cam.clearFlags = CameraClearFlags.Skybox;
        _cam.depth = -50;
        _cam.allowHDR = false;
        _cam.allowMSAA = false;

        _rt = new RenderTexture(textureSize, textureSize, 16, RenderTextureFormat.Default);
        _rt.name = "WaterReflection";
        _rt.hideFlags = HideFlags.DontSave;
        _cam.targetTexture = _rt;
        if (_mat != null) _mat.SetTexture(texProperty, _rt);
    }

    void LateUpdate()
    {
        if (_cam == null || _rt == null || _mat == null) { OnDisable(); OnEnable(); return; }

        _mainCam = Camera.main;
        if (_mainCam == null) return;

        // ---- 关于水面镜像主相机 ----
        Vector3 pos = transform.position;
        Vector3 normal = transform.up;

        float d = -Vector3.Dot(normal, pos) - clipPlaneOffset;
        var reflectMatrix = Matrix4x4.zero;
        reflectMatrix.m00 = 1f - 2f * normal.x * normal.x;
        reflectMatrix.m01 = -2f * normal.x * normal.y;
        reflectMatrix.m02 = -2f * normal.x * normal.z;
        reflectMatrix.m03 = -2f * d * normal.x;
        reflectMatrix.m10 = -2f * normal.y * normal.x;
        reflectMatrix.m11 = 1f - 2f * normal.y * normal.y;
        reflectMatrix.m12 = -2f * normal.y * normal.z;
        reflectMatrix.m13 = -2f * d * normal.y;
        reflectMatrix.m20 = -2f * normal.z * normal.x;
        reflectMatrix.m21 = -2f * normal.z * normal.y;
        reflectMatrix.m22 = 1f - 2f * normal.z * normal.z;
        reflectMatrix.m23 = -2f * d * normal.z;
        reflectMatrix.m33 = 1f;

        var camTrans = _mainCam.transform;
        _cam.transform.position = reflectMatrix.MultiplyPoint(camTrans.position);
        _cam.transform.rotation = Quaternion.LookRotation(
            reflectMatrix.MultiplyVector(camTrans.forward),
            reflectMatrix.MultiplyVector(camTrans.up));

        _cam.projectionMatrix = _mainCam.projectionMatrix;
        _cam.fieldOfView = _mainCam.fieldOfView;
        _cam.nearClipPlane = _mainCam.nearClipPlane;
        _cam.farClipPlane = _mainCam.farClipPlane;
        _cam.aspect = _mainCam.aspect;

        // 斜裁剪面：只渲染水面以上的东西，避免水下物体被反射
        Vector4 clipPlane = CameraSpacePlane(_cam, pos, normal, 1.0f);
        _cam.projectionMatrix = _mainCam.CalculateObliqueMatrix(clipPlane);

        if (_mat.GetTexture(texProperty) == null) _mat.SetTexture(texProperty, _rt);
        _cam.Render();
    }

    Vector4 CameraSpacePlane(Camera cam, Vector3 pos, Vector3 normal, float sideSign)
    {
        Vector3 offsetPos = pos + normal * clipPlaneOffset;
        Matrix4x4 m = cam.worldToCameraMatrix;
        Vector3 cpos = m.MultiplyPoint(offsetPos);
        Vector3 cnormal = m.MultiplyVector(normal).normalized * sideSign;
        return new Vector4(cnormal.x, cnormal.y, cnormal.z, -Vector3.Dot(cpos, cnormal));
    }
}
