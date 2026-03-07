using UnityEngine;

[ExecuteAlways]
public class Obstacle : MonoBehaviour
{
    public float padding = 1f;

    private SphereCollider boundary;
    private Collider obstacleCollider;

    void OnEnable()
    {
        Setup();
    }

    void OnValidate()
    {
        Setup();
    }

    void Setup()
    {
        obstacleCollider = GetComponent<Collider>();
        if (!obstacleCollider) return;

        if (!boundary)
        {
            boundary = GetComponent<SphereCollider>();

            if (!boundary)
                boundary = gameObject.AddComponent<SphereCollider>();
        }

        boundary.isTrigger = true;

        Bounds bounds = obstacleCollider.bounds;

        float radiusX = bounds.extents.x;
        float radiusZ = bounds.extents.z;

        float radius = Mathf.Max(radiusX, radiusZ) + padding;

        boundary.radius = radius;

        Vector3 localCenter = transform.InverseTransformPoint(bounds.center);
        boundary.center = localCenter;
    }

    void OnDrawGizmos()
    {
        if (!boundary) return;

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.TransformPoint(boundary.center), boundary.radius);
    }
}