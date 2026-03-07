using UnityEngine;

/// <summary>
/// Implements Artificial Potential Field navigation.
/// Runs as a force layer on top of waypoint following —
/// the drone still tries to reach its assigned waypoint
/// but APF pushes it around obstacles detected by LIDAR.
///
/// F_total = F_attractive + F_repulsive + F_wallFollow
///
/// F_att  = -k_att * (q - q_goal)
/// F_rep  = k_rep * (1/d - 1/d0) * (1/d^2)   if d <= d0
///        = 0                                   if d > d0
/// </summary>
[RequireComponent(typeof(LidarSensor))]
public class APFController : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────
    // PUBLIC STATE (read by DroneAgent)
    // ─────────────────────────────────────────────────────────────

    public bool  IsWallFollowing  { get; private set; }
    public int   StuckEventCount  { get; private set; }
    public int   WallFollowCount  { get; private set; }
    public float PathLength       { get; private set; }

    // ─────────────────────────────────────────────────────────────
    // PRIVATE
    // ─────────────────────────────────────────────────────────────

    private LidarSensor  lidar;
    private Rigidbody    rb;

    private Vector3 smoothedVelocity;
    private Vector3 previousForce;
    private Vector3 lastPosition;
    private Vector3 stuckCheckPosition;

    private float stuckTimer        = 0f;
    private int   wallFollowDirection = 1;
    private float wallFollowTimer   = 0f;

    // ─────────────────────────────────────────────────────────────
    // INIT
    // ─────────────────────────────────────────────────────────────

    void Awake()
    {
        lidar        = GetComponent<LidarSensor>();
        rb           = GetComponent<Rigidbody>();
        lastPosition = transform.position;

        rb.useGravity             = false;
        rb.freezeRotation         = true;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.drag                   = 2f;
        rb.angularDrag            = 5f;

        lastPosition       = transform.position;
        stuckCheckPosition = transform.position;
    }

    // ─────────────────────────────────────────────────────────────
    // MAIN — called by DroneAgent every FixedUpdate with a target
    // Returns the final velocity to apply to the Rigidbody
    // ─────────────────────────────────────────────────────────────

    public Vector3 ComputeVelocity(Transform target)
    {
        if (target == null) return Vector3.zero;

        float safeDelta = Mathf.Min(Time.fixedDeltaTime, Sim1Variables.SafeDeltaTimeCap);

        lidar.UpdateReadings();

        UpdateStuckDetection(target, safeDelta);
        UpdateWallFollowTimer(safeDelta);

        float closestDist = lidar.ClosestObstacleDistance;

        // ── Speed reduction near obstacles ──────────────────────
        // SmoothStep S-curve: full speed far away, crawl near obstacle
        float speedMult = 1f;
        if (closestDist < Sim1Variables.SlowDownRadius)
        {
            speedMult = Mathf.SmoothStep(
                Sim1Variables.MinSpeedNearObstacle,
                1f,
                closestDist / Sim1Variables.SlowDownRadius
            );
        }
        float currentMaxSpeed = Sim1Variables.DroneSpeed * speedMult;

        // ── Repulsive force ──────────────────────────────────────
        // F_rep = k_rep * (1/d - 1/d0) * (1/d^2)
        Vector3 repulsiveForce = Vector3.zero;
        Vector3 wallNormal     = Vector3.zero;

        float minSafe = Sim1Variables.MinSafeDistance;

        for (int i = 0; i < lidar.Readings.Length; i++)
        {
            float dist = lidar.SmoothedDistances[i];
            if (dist >= minSafe) continue;

            Vector3 away;
            if (lidar.Readings[i].hitSomething)
                away = transform.position - lidar.Readings[i].hitPoint;
            else
                away = -lidar.Readings[i].direction;

            away.y = 0;
            if (away.magnitude < 0.001f) continue;

            // Core APF repulsion formula
            float forceMag = Sim1Variables.RepulsionStrength *
                             (1f / dist - 1f / minSafe) /
                             (dist * dist);

            // Cap per-beam force to prevent explosive spikes
            forceMag = Mathf.Min(forceMag, Sim1Variables.MaxRepulsionForce);

            repulsiveForce += away.normalized * Mathf.Max(0, forceMag);
            wallNormal     += away.normalized;
        }

        // ── Wall following ───────────────────────────────────────
        // Activated when drone faces wall — slides along it
        // This is the local minima escape mechanism
        if (lidar.IsFacingWall() && !IsWallFollowing)
        {
            IsWallFollowing   = true;
            wallFollowTimer   = 0f;
            wallFollowDirection = lidar.ChooseWallFollowDirection(target);
            WallFollowCount++;
            Debug.Log($"[APF] WALL AHEAD — following {(wallFollowDirection > 0 ? "RIGHT" : "LEFT")} | Total: {WallFollowCount}");
        }

        Vector3 wallFollowForce = Vector3.zero;
        if (IsWallFollowing && wallNormal.magnitude > 0.1f)
        {
            Vector3 slideDir = Vector3.Cross(wallNormal.normalized, Vector3.up);
            if (Vector3.Dot(slideDir, transform.right) * wallFollowDirection < 0)
                slideDir = -slideDir;

            wallFollowForce   = slideDir * Sim1Variables.WallFollowStrength * wallFollowDirection;
            wallFollowForce.y = 0;
            Debug.DrawRay(transform.position, wallFollowForce, Color.cyan);
        }

        // ── Attractive force ─────────────────────────────────────
        // F_att = -k_att * (q - q_goal) = k_att * (q_goal - q)
        Vector3 toTarget     = target.position - transform.position;
        toTarget.y           = 0;
        float distToTarget   = toTarget.magnitude;
        Vector3 attractiveForce = toTarget.normalized * Sim1Variables.AttractionStrength;

        // Scale down attraction near obstacles (SmoothStep prevents jitter)
        if (closestDist < minSafe)
        {
            float attractionScale = Mathf.SmoothStep(0.1f, 1f, closestDist / minSafe);
            attractiveForce      *= attractionScale;
        }

        // Reduce attraction when wall following (let repulsion dominate)
        if (IsWallFollowing)
            attractiveForce *= 0.3f;

        // Slow down when nearly at target
        if (distToTarget < 3f)
            attractiveForce *= distToTarget / 3f;

        // ── Combine forces ───────────────────────────────────────
        Vector3 totalForce = attractiveForce + repulsiveForce + wallFollowForce;

        // Cap to current max speed (reduced near obstacles)
        if (totalForce.magnitude > currentMaxSpeed)
            totalForce = totalForce.normalized * currentMaxSpeed;

        // ── Smooth force and velocity ────────────────────────────
        Vector3 smoothedForce = Vector3.Lerp(previousForce, totalForce, Sim1Variables.VelocitySmoothing);
        previousForce = smoothedForce;

        // smoothedForce magnitude is already capped to currentMaxSpeed above —
        // do NOT multiply by DroneSpeed again or velocity becomes ~170 m/s
        Vector3 targetVelocity = smoothedForce;

        // Height correction — maintain FlyHeight
        float   heightError          = Sim1Variables.FlyHeight - transform.position.y;
        Vector3 heightCorrectionForce = new Vector3(0, heightError * Sim1Variables.HeightCorrectionStrength, 0);
        targetVelocity.y             = heightCorrectionForce.y;

        smoothedVelocity = Vector3.Lerp(
            smoothedVelocity,
            targetVelocity,
            Sim1Variables.VelocitySmoothing * 8f  // was *2f — too slow to accelerate from rest
        );

        // Track total path length for data export
        PathLength += Vector3.Distance(transform.position, lastPosition);
        lastPosition = transform.position;

        // ── Debug rays ───────────────────────────────────────────
        Debug.DrawRay(transform.position, attractiveForce * 2f,  Color.green);
        Debug.DrawRay(transform.position, repulsiveForce  * 2f,  Color.red);
        Debug.DrawRay(transform.position, smoothedForce   * 2f,  Color.blue);
        Debug.DrawRay(transform.position, heightCorrectionForce * 2f, Color.magenta);

        return smoothedVelocity;
    }

    // ─────────────────────────────────────────────────────────────
    // ROTATION — smooth look toward movement direction
    // ─────────────────────────────────────────────────────────────

    public void ApplyRotation(Vector3 velocity)
    {
        Vector3 horizontal = velocity;
        horizontal.y = 0;

        if (horizontal.magnitude > 0.1f)
        {
            Quaternion targetRot = Quaternion.LookRotation(horizontal);
            transform.rotation   = Quaternion.Slerp(
                transform.rotation,
                targetRot,
                Time.fixedDeltaTime * Sim1Variables.RotationSmoothing
            );
        }
    }

    // ─────────────────────────────────────────────────────────────
    // STUCK DETECTION
    // ─────────────────────────────────────────────────────────────

    private void UpdateStuckDetection(Transform target, float safeDelta)
    {
        stuckTimer += safeDelta;

        if (stuckTimer < Sim1Variables.StuckThreshold) return;

        float distMoved = Vector3.Distance(
            new Vector3(transform.position.x, 0, transform.position.z),
            new Vector3(stuckCheckPosition.x, 0, stuckCheckPosition.z)
        );

        if (distMoved < Sim1Variables.StuckDistance)
        {
            wallFollowDirection *= -1;
            IsWallFollowing      = true;
            wallFollowTimer      = 0f;
            StuckEventCount++;
            Debug.Log($"[APF] STUCK #{StuckEventCount} — flipping wall follow direction");
        }

        stuckCheckPosition = transform.position;
        stuckTimer         = 0f;
    }

    private void UpdateWallFollowTimer(float safeDelta)
    {
        if (!IsWallFollowing) return;

        wallFollowTimer += safeDelta;
        if (wallFollowTimer > Sim1Variables.WallFollowTimeout)
        {
            IsWallFollowing = false;
            wallFollowTimer = 0f;
        }
    }

    // ─────────────────────────────────────────────────────────────
    // SAFETY NET — physical collision fallback
    // ─────────────────────────────────────────────────────────────

    void OnCollisionStay(Collision collision)
    {
        if (collision.gameObject == gameObject) return;
        if (!collision.gameObject.CompareTag("Obstacle")) return;

        Vector3 pushDir = transform.position - collision.contacts[0].point;
        pushDir.y       = 0;
        rb.velocity     = pushDir.normalized * Sim1Variables.DroneSpeed * 3f;
        Debug.LogWarning($"[APF] SAFETY NET triggered by: {collision.gameObject.name}");
    }

    // ─────────────────────────────────────────────────────────────
    // RESET (for re-runs)
    // ─────────────────────────────────────────────────────────────

    public void ResetStats()
    {
        StuckEventCount    = 0;
        WallFollowCount    = 0;
        PathLength         = 0f;
        IsWallFollowing    = false;
        wallFollowTimer    = 0f;
        stuckTimer         = 0f;
        smoothedVelocity   = Vector3.zero;
        previousForce      = Vector3.zero;
        stuckCheckPosition = transform.position;
        lastPosition       = transform.position;
    }
}
