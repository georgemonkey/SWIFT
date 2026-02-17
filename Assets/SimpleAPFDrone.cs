using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class SimpleAPFDrone : MonoBehaviour
{
    [Header("Movement")]
    public float speed = 10f;
    public float flyHeight = 1f;
    public float heightCorrectionStrength = 10f;

    [Header("APF Settings")]
    public float attractionStrength = 5f;
    public float repulsionStrength = 300f;
    public float minSafeDistance = 8f;

    [Header("Lidar Configuration")]
    public float[] lidarAngles = { -80f, -40f, 0f, 40f, 80f };
    public float lidarMaxRange = 12f;

    [Header("Wall Following")]
    public float wallFollowStrength = 8f;
    public float stuckThreshold = 1.5f;
    public float stuckDistance = 0.5f;
    public float wallFollowTimeout = 4f;

    [Header("Smoothing")]
    public float velocitySmoothing = 0.08f;
    public float rotationSmoothing = 3f;
    public float lidarSmoothing = 0.15f;

    [Header("Near Obstacle Smoothing")]
    // How far away to start slowing down
    // drone smoothly decelerates in this zone
    public float slowDownRadius = 12f;
    // Minimum speed when right next to obstacle
    // prevents drone stopping completely
    public float minSpeedNearObstacle = 0.3f;
    // How hard to cap repulsion force
    // prevents explosive force spikes near obstacles
    public float maxRepulsionForce = 8f;

    private struct LidarReading
    {
        public float angle;
        public float distance;
        public Vector3 direction;
        public Vector3 hitPoint;
        public bool hitSomething;
    }

    private LidarReading[] readings;
    private float[] smoothedDistances;
    private Vector3 smoothedVelocity;
    private Vector3 previousForce;
    private Transform target;
    private Rigidbody rb;
    private Vector3 lastPosition;
    private float stuckTimer = 0f;
    private int wallFollowDirection = 1;
    private bool isWallFollowing = false;
    private float wallFollowTimer = 0f;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.freezeRotation = true;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.drag = 2f;
        rb.angularDrag = 5f;

        flyHeight = transform.position.y;
        lastPosition = transform.position;
        readings = new LidarReading[lidarAngles.Length];

        smoothedDistances = new float[lidarAngles.Length];
        for (int i = 0; i < smoothedDistances.Length; i++)
            smoothedDistances[i] = lidarMaxRange;

        smoothedVelocity = Vector3.zero;
        previousForce = Vector3.zero;

        GameObject targetObj = GameObject.FindGameObjectWithTag("Finish");
        if (targetObj != null)
        {
            target = targetObj.transform;
            Debug.Log("TARGET FOUND: " + targetObj.name);
        }
        else
        {
            Debug.LogError("TARGET NOT FOUND!");
        }
    }

    void UpdateLidarReadings()
    {
        for (int i = 0; i < lidarAngles.Length; i++)
        {
            Quaternion rotation = Quaternion.Euler(0, lidarAngles[i], 0);
            Vector3 direction = transform.rotation * rotation * Vector3.forward;

            readings[i].angle = lidarAngles[i];
            readings[i].direction = direction;

            RaycastHit hit;
            if (Physics.Raycast(transform.position, direction, out hit, lidarMaxRange))
            {
                if (hit.collider.gameObject == gameObject ||
                    hit.collider.gameObject.CompareTag("Finish"))
                {
                    readings[i].hitSomething = false;
                    readings[i].distance = lidarMaxRange;
                    smoothedDistances[i] = Mathf.Lerp(
                        smoothedDistances[i], lidarMaxRange, lidarSmoothing);
                    continue;
                }

                if (hit.point.y < transform.position.y - 0.3f)
                {
                    readings[i].hitSomething = false;
                    readings[i].distance = lidarMaxRange;
                    smoothedDistances[i] = Mathf.Lerp(
                        smoothedDistances[i], lidarMaxRange, lidarSmoothing);
                    Debug.DrawRay(transform.position,
                        direction * hit.distance, Color.grey);
                    continue;
                }

                readings[i].hitSomething = true;
                readings[i].distance = hit.distance;
                readings[i].hitPoint = hit.point;

                smoothedDistances[i] = Mathf.Lerp(
                    smoothedDistances[i], hit.distance, lidarSmoothing);

                Color rayColor;
                if (smoothedDistances[i] < minSafeDistance * 0.5f)
                    rayColor = Color.red;
                else if (smoothedDistances[i] < minSafeDistance)
                    rayColor = Color.yellow;
                else
                    rayColor = Color.green;

                Debug.DrawRay(transform.position,
                    direction * smoothedDistances[i], rayColor);
            }
            else
            {
                readings[i].hitSomething = false;
                readings[i].distance = lidarMaxRange;
                smoothedDistances[i] = Mathf.Lerp(
                    smoothedDistances[i], lidarMaxRange, lidarSmoothing);
                Debug.DrawRay(transform.position,
                    direction * lidarMaxRange,
                    new Color(0, 1, 0, 0.15f));
            }
        }
    }

    bool IsFacingWall()
    {
        int centerIdx = 0;
        float smallestAngle = float.MaxValue;
        for (int i = 0; i < lidarAngles.Length; i++)
        {
            if (Mathf.Abs(lidarAngles[i]) < smallestAngle)
            {
                smallestAngle = Mathf.Abs(lidarAngles[i]);
                centerIdx = i;
            }
        }
        return readings[centerIdx].hitSomething &&
               smoothedDistances[centerIdx] < minSafeDistance * 1.5f;
    }

    int ChooseWallFollowDirection()
    {
        float leftOpenness = 0f;
        float rightOpenness = 0f;
        int leftCount = 0;
        int rightCount = 0;

        for (int i = 0; i < lidarAngles.Length; i++)
        {
            if (lidarAngles[i] < 0)
            {
                leftOpenness += smoothedDistances[i];
                leftCount++;
            }
            else if (lidarAngles[i] > 0)
            {
                rightOpenness += smoothedDistances[i];
                rightCount++;
            }
        }

        float avgLeft = leftCount > 0
            ? leftOpenness / leftCount : lidarMaxRange;
        float avgRight = rightCount > 0
            ? rightOpenness / rightCount : lidarMaxRange;

        Vector3 toTarget = target.position - transform.position;
        toTarget.y = 0;
        float targetRight = Vector3.Dot(toTarget.normalized, transform.right);

        float rightScore = avgRight + targetRight * 3f;
        float leftScore = avgLeft - targetRight * 3f;

        return rightScore > leftScore ? 1 : -1;
    }

    void FixedUpdate()
    {
        if (target == null) return;

        UpdateLidarReadings();

        // Stuck detection
        stuckTimer += Time.fixedDeltaTime;
        if (stuckTimer > stuckThreshold)
        {
            float distanceMoved = Vector3.Distance(
                new Vector3(transform.position.x, 0, transform.position.z),
                new Vector3(lastPosition.x, 0, lastPosition.z)
            );

            if (distanceMoved < stuckDistance)
            {
                wallFollowDirection *= -1;
                isWallFollowing = true;
                wallFollowTimer = 0f;
                Debug.Log("STUCK - flipping wall follow!");
            }

            lastPosition = transform.position;
            stuckTimer = 0f;
        }

        if (isWallFollowing)
        {
            wallFollowTimer += Time.fixedDeltaTime;
            if (wallFollowTimer > wallFollowTimeout)
            {
                isWallFollowing = false;
                wallFollowTimer = 0f;
            }
        }

        // Height correction
        float heightError = flyHeight - transform.position.y;
        Vector3 heightCorrectionForce = new Vector3(
            0, heightError * heightCorrectionStrength, 0
        );

        // Find closest obstacle across all lidars
        float closestDistance = lidarMaxRange;
        for (int i = 0; i < readings.Length; i++)
        {
            if (smoothedDistances[i] < closestDistance)
                closestDistance = smoothedDistances[i];
        }

        // -----------------------------------------------
        // NEAR OBSTACLE SPEED REDUCTION
        // As drone enters slowDownRadius, smoothly
        // reduce its max speed
        // This prevents it thrashing around trying to
        // go fast while forces are fighting each other!
        //
        // Far away:  full speed
        // Entering:  smoothly slow down
        // Very close: minimum crawl speed
        // -----------------------------------------------
        float speedMultiplier = 1f;
        if (closestDistance < slowDownRadius)
        {
            // SmoothStep gives a nice S-curve transition
            // instead of a harsh linear slowdown
            speedMultiplier = Mathf.SmoothStep(
                minSpeedNearObstacle,  // Min speed factor
                1f,                    // Max speed factor
                closestDistance / slowDownRadius
            );
        }

        float currentMaxSpeed = speed * speedMultiplier;

        // APF repulsion
        Vector3 repulsiveForce = Vector3.zero;
        Vector3 wallNormal = Vector3.zero;

        for (int i = 0; i < readings.Length; i++)
        {
            float dist = smoothedDistances[i];

            if (dist < minSafeDistance)
            {
                Vector3 away;
                if (readings[i].hitSomething)
                    away = transform.position - readings[i].hitPoint;
                else
                    away = -readings[i].direction;

                away.y = 0;
                if (away.magnitude < 0.001f) continue;

                float forceMag = repulsionStrength *
                                 (1f / dist - 1f / minSafeDistance) /
                                 (dist * dist);

                // -----------------------------------------------
                // CAP REPULSION FORCE PER LIDAR
                // Without this, getting very close causes
                // explosive repulsion that creates huge jitter
                // maxRepulsionForce limits each lidar's
                // contribution to the total force
                // -----------------------------------------------
                forceMag = Mathf.Min(forceMag, maxRepulsionForce);

                repulsiveForce += away.normalized * Mathf.Max(0, forceMag);
                wallNormal += away.normalized;
            }
        }

        // Wall following
        if (IsFacingWall() && !isWallFollowing)
        {
            isWallFollowing = true;
            wallFollowTimer = 0f;
            wallFollowDirection = ChooseWallFollowDirection();
            Debug.Log("WALL AHEAD - following " +
                      (wallFollowDirection > 0 ? "RIGHT" : "LEFT"));
        }

        Vector3 wallFollowForce = Vector3.zero;
        if (isWallFollowing && wallNormal.magnitude > 0.1f)
        {
            Vector3 slideDir = Vector3.Cross(wallNormal.normalized, Vector3.up);
            if (Vector3.Dot(slideDir, transform.right) * wallFollowDirection < 0)
                slideDir = -slideDir;

            wallFollowForce = slideDir * wallFollowStrength * wallFollowDirection;
            wallFollowForce.y = 0;
            Debug.DrawRay(transform.position, wallFollowForce, Color.cyan);
        }

        // -----------------------------------------------
        // ATTRACTION with smooth distance-based scaling
        // Uses SmoothStep so transition from full
        // attraction to reduced attraction near obstacles
        // is gradual not sudden - this is the main cause
        // of jitter near obstacles!
        // -----------------------------------------------
        Vector3 toTarget = target.position - transform.position;
        toTarget.y = 0;
        float distToTarget = toTarget.magnitude;
        Vector3 attractiveForce = toTarget.normalized * attractionStrength;

        if (closestDistance < minSafeDistance)
        {
            // SmoothStep instead of linear scaling!
            float attractionScale = Mathf.SmoothStep(
                0.1f,   // Almost zero attraction when very close
                1f,     // Full attraction when at safe distance
                closestDistance / minSafeDistance
            );
            attractiveForce *= attractionScale;
        }

        if (isWallFollowing)
            attractiveForce *= 0.3f;

        if (distToTarget < 3f)
            attractiveForce *= distToTarget / 3f;

        // Combine forces
        Vector3 totalForce = attractiveForce + repulsiveForce + wallFollowForce;

        // Cap total force to current max (reduced near obstacles!)
        if (totalForce.magnitude > currentMaxSpeed)
            totalForce = totalForce.normalized * currentMaxSpeed;

        // Smooth the force transition
        Vector3 smoothedForce = Vector3.Lerp(
            previousForce,
            totalForce,
            velocitySmoothing
        );
        previousForce = smoothedForce;

        // Smooth velocity
        Vector3 targetVelocity = smoothedForce * speed;
        targetVelocity.y = heightCorrectionForce.y;

        smoothedVelocity = Vector3.Lerp(
            smoothedVelocity,
            targetVelocity,
            velocitySmoothing * 2f
        );

        rb.velocity = smoothedVelocity;

        // Smooth rotation
        Vector3 horizontalForce = smoothedForce;
        horizontalForce.y = 0;

        if (horizontalForce.magnitude > 0.1f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(horizontalForce);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                Time.fixedDeltaTime * rotationSmoothing
            );
        }

        Debug.DrawRay(transform.position, attractiveForce * 2f, Color.green);
        Debug.DrawRay(transform.position, repulsiveForce * 2f, Color.red);
        Debug.DrawRay(transform.position, smoothedForce * 2f, Color.blue);
        Debug.DrawRay(transform.position, heightCorrectionForce * 2f, Color.magenta);

        if (distToTarget < 1f)
        {
            Debug.Log("REACHED TARGET!");
            rb.velocity = Vector3.zero;
            enabled = false;
        }
    }

    void OnCollisionStay(Collision collision)
    {
        if (collision.gameObject == gameObject) return;
        if (!collision.gameObject.CompareTag("Obstacle")) return;

        Vector3 pushDirection = transform.position - collision.contacts[0].point;
        pushDirection.y = 0;
        rb.velocity = pushDirection.normalized * speed * 3f;
        Debug.LogWarning("SAFETY NET: " + collision.gameObject.name);
    }

    void OnDrawGizmos()
    {
        if (readings == null) return;

        for (int i = 0; i < readings.Length; i++)
        {
            Gizmos.color = readings[i].hitSomething ? Color.red : Color.green;
            Gizmos.DrawRay(transform.position,
                readings[i].direction * smoothedDistances[i]);
        }

        Gizmos.color = new Color(1, 0, 0, 0.2f);
        Gizmos.DrawWireSphere(transform.position, minSafeDistance);

        Gizmos.color = new Color(1, 1, 0, 0.1f);
        Gizmos.DrawWireSphere(transform.position, slowDownRadius);
    }
}