using UnityEngine;

public class CameraSwing : MonoBehaviour
{
    public GameObject target;
    private Transform targetTransform;

    private VrmToController vrmToController;

    public float positionZ = 1f;

    Vector3 baseDir;
    float baseDist;
    float t;

    const float TAU = 6.28318530718f;
    const float FallbackDistance = 5f;

    void Awake()
    {
        if (target == null) return;
        targetTransform = target.GetComponent<Transform>();
        vrmToController = target.GetComponent<VrmToController>();

        baseDist = Vector3.Distance(transform.position, targetTransform.position);
        if (baseDist < 0.01f) baseDist = FallbackDistance;

        baseDir = (transform.position - targetTransform.position).normalized;
    }

    void LateUpdate()
    {
        if (target == null) return;

        t += Time.deltaTime;

        float angleRange = 15 + positionZ * 10;
        float dy = Mathf.Sin(TAU * t / 25) * angleRange;

        var targetHeight = vrmToController.height;

        Vector3 center = new Vector3(
            targetTransform.position.x,
            targetTransform.position.y + 0.94f + (1 - positionZ) * (0.5f - targetHeight * 0.3f),
            targetTransform.position.z
        );

        Vector3 dir = baseDir;
        dir = Quaternion.AngleAxis(dy, Vector3.up) * dir;

        float distance = 4f + positionZ * 15f;
        Vector3 pos = center + dir.normalized * distance;
        transform.position = pos;
        transform.LookAt(center, Vector3.up);
    }

    // スライダー側・コントローラー側から呼ぶ用
    public void SetPositionZ(float value)
    {
        positionZ = Mathf.Clamp01(value);
    }
}
