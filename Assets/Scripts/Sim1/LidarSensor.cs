using UnityEngine;

/// <summary>
/// Simulates a 5-beam LIDAR sensor array on the drone.
/// Fires raycasts at configurable angles, smooths readings to reduce jitter.
/// Ignores self, ground hits below drone, and the target object.
/// Used by APFController to calculate repulsive forces.
/// </summary>
public class LidarSensor : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────
    // PUBLIC STRUCTS
    // ─────────────────────────────────────────────────────────────

    public struct LidarReading
    {
        public float angle;
        public float distance;
        public Vector3 direction;
        public Vector3 hitPoint;
        public bool hitSomething;
    }

    // ─────────────────────────────────────────────────────────────
    // INSPECTOR
    // ─────────────────────────────────────────────────────────────

    [Header("Beam Angles (degrees from forward)")]
    public float[] lidarAngles = { -80f, -40f, 0f, 40f, 80f };

    // ─────────────────────────────────────────────────────────────
    // PUBLIC READ-ONLY STATE
    // ─────────────────────────────────────────────────────────────

    public LidarReading[] Readings { get; private set; }
    public float[] SmoothedDistances { get; private set; }
    public float ClosestObstacleDistance { get; private set; }

    // ─────────────────────────────────────────────────────────────
    // PRIVATE
    // ─────────────────────────────────────────────────────────────

    private float maxRange;
    private float smoothing;

    // ─────────────────────────────────────────────────────────────
    // INIT
    // ─────────────────────────────────────────────────────────────

    void Awake()
    {
        maxRange  = Sim1Variables.LidarMaxRange;
        smoothing = Sim1Variables.LidarSmoothing;

        Readings          = new LidarReading[lidarAngles.Length];
        SmoothedDistances = new float[lidarAngles.Length];

        for (int i = 0; i < SmoothedDistances.Length; i++)
            SmoothedDistances[i] = maxRange;

        ClosestObstacleDistance = maxRange;
    }

    // ─────────────────────────────────────────────────────────────
    // UPDATE — called by APFController each FixedUpdate
    // ─────────────────────────────────────────────────────────────

    public void UpdateReadings()
    {
        maxRange  = Sim1Variables.LidarMaxRange;
        smoothing = Sim1Variables.LidarSmoothing;

        float closest = maxRange;

        for (int i = 0; i < lidarAngles.Length; i++)
        {
            Quaternion rot       = Quaternion.Euler(0, lidarAngles[i], 0);
            Vector3    direction = transform.rotation * rot * Vector3.forward;

            Readings[i].angle     = lidarAngles[i];
            Readings[i].direction = direction;

            RaycastHit hit;
            if (Physics.Raycast(transform.position, direction, out hit, maxRange))
            {
                // Ignore self
                if (hit.collider.gameObject == gameObject)
                {
                    SetNoHit(i, direction);
                    continue;
                }

                // Ignore target
                if (hit.collider.gameObject.CompareTag("Finish"))
                {
                    SetNoHit(i, direction);
                    continue;
                }

                // Ignore ground hits below drone
                if (hit.point.y < transform.position.y - 0.3f)
                {
                    SetNoHit(i, direction);
                    Debug.DrawRay(transform.position, direction * hit.distance, Color.grey);
                    continue;
                }

                // Valid obstacle hit
                Readings[i].hitSomething = true;
                Readings[i].distance     = hit.distance;
                Readings[i].hitPoint     = hit.point;

                SmoothedDistances[i] = Mathf.Lerp(SmoothedDistances[i], hit.distance, smoothing);

                if (SmoothedDistances[i] < closest)
                    closest = SmoothedDistances[i];

                // Color-coded debug rays
                Color rayColor;
                float minSafe = Sim1Variables.MinSafeDistance;
                if      (SmoothedDistances[i] < minSafe * 0.5f) rayColor = Color.red;
                else if (SmoothedDistances[i] < minSafe)        rayColor = Color.yellow;
                else                                             rayColor = Color.green;

                Debug.DrawRay(transform.position, direction * SmoothedDistances[i], rayColor);
            }
            else
            {
                SetNoHit(i, direction);
            }
        }

        ClosestObstacleDistance = closest;
    }

    // ─────────────────────────────────────────────────────────────
    // HELPERS
    // ─────────────────────────────────────────────────────────────

    private void SetNoHit(int i, Vector3 direction)
    {
        Readings[i].hitSomething = false;
        Readings[i].distance     = maxRange;

        SmoothedDistances[i] = Mathf.Lerp(SmoothedDistances[i], maxRange, smoothing);

        Debug.DrawRay(transform.position, direction * maxRange, new Color(0, 1, 0, 0.15f));
    }

    public bool IsFacingWall()
    {
        // Find the beam closest to straight-ahead (smallest absolute angle offset)
        int   centerIdx     = 0;
        float smallestAngle = float.MaxValue;

        for (int i = 0; i < lidarAngles.Length; i++)
        {
            float absAngle = Mathf.Abs(lidarAngles[i]);
            if (absAngle < smallestAngle)
            {
                smallestAngle = absAngle;
                centerIdx     = i;
            }
        }

        // Guard: if no beam is within 45° of forward, can't reliably detect wall ahead
        if (smallestAngle > 45f) return false;

        return Readings[centerIdx].hitSomething &&
               SmoothedDistances[centerIdx] < Sim1Variables.MinSafeDistance * 1.5f;
    }

    public int ChooseWallFollowDirection(Transform target)
    {
        float leftOpenness  = 0f;
        float rightOpenness = 0f;
        int   leftCount     = 0;
        int   rightCount    = 0;

        for (int i = 0; i < lidarAngles.Length; i++)
        {
            if (lidarAngles[i] < 0) { leftOpenness  += SmoothedDistances[i]; leftCount++;  }
            else if (lidarAngles[i] > 0) { rightOpenness += SmoothedDistances[i]; rightCount++; }
        }

        float avgLeft  = leftCount  > 0 ? leftOpenness  / leftCount  : maxRange;
        float avgRight = rightCount > 0 ? rightOpenness / rightCount : maxRange;

        Vector3 toTarget  = target.position - transform.position;
        toTarget.y        = 0;
        float targetRight = Vector3.Dot(toTarget.normalized, transform.right);

        float rightScore = avgRight + targetRight * 3f;
        float leftScore  = avgLeft  - targetRight * 3f;

        return rightScore > leftScore ? 1 : -1;
    }

    // ─────────────────────────────────────────────────────────────
    // GIZMOS
    // ─────────────────────────────────────────────────────────────

    void OnDrawGizmos()
    {
        if (Readings == null) return;

        for (int i = 0; i < Readings.Length; i++)
        {
            Gizmos.color = Readings[i].hitSomething ? Color.red : Color.green;
            Gizmos.DrawRay(transform.position, Readings[i].direction * SmoothedDistances[i]);
        }

        Gizmos.color = new Color(1, 0, 0, 0.2f);
        Gizmos.DrawWireSphere(transform.position, Sim1Variables.MinSafeDistance);

        Gizmos.color = new Color(1, 1, 0, 0.1f);
        Gizmos.DrawWireSphere(transform.position, Sim1Variables.SlowDownRadius);

        Gizmos.color = new Color(0, 1, 1, 0.1f);
        Gizmos.DrawWireSphere(transform.position, Sim1Variables.ThermalDetectionRadius);
    }
}
