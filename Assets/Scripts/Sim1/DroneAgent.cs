using UnityEngine;

/// <summary>
/// Main drone brain for Sim1.
/// Combines:
///   - APF navigation (obstacle avoidance force layer)
///   - Target following (moves toward assigned waypoint/target)
///   - Battery model (aerodynamic drag scaling)
///   - Thermal detection (5.2m radius survivor scan)
///   - RTH (return to home when battery hits threshold)
///
/// Assigned a target by Sim1Manager or LeaderFollowerSystem.
/// Reports state to LeaderFollowerSystem so leader can monitor followers.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(APFController))]
[RequireComponent(typeof(LidarSensor))]
[RequireComponent(typeof(ThermalDetector))]
public class DroneAgent : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────
    // IDENTITY
    // ─────────────────────────────────────────────────────────────

    public int  DroneID   { get; private set; }
    public bool IsLeader  { get; private set; }

    // ─────────────────────────────────────────────────────────────
    // MISSION STATE
    // ─────────────────────────────────────────────────────────────

    public enum DroneMode { Idle, Active, RTH, Landed }
    public DroneMode Mode { get; private set; } = DroneMode.Idle;

    public bool   MissionComplete { get; private set; }
    public bool   HasLanded       { get; private set; }
    public float  BatteryLevel    { get; private set; } = 100f;

    // ─────────────────────────────────────────────────────────────
    // NAVIGATION
    // ─────────────────────────────────────────────────────────────

    /// <summary>Current target the drone is navigating toward (set by manager)</summary>
    public Transform CurrentTarget { get; private set; }

    /// <summary>Home position to return to when RTH triggers</summary>
    public Vector3 HomePosition { get; private set; }

    // ─────────────────────────────────────────────────────────────
    // TRACKING (for LeaderFollowerSystem to read)
    // ─────────────────────────────────────────────────────────────

    public Vector3 LastCheckedPosition { get; set; }
    public float   TimeStationary      { get; set; }

    // ─────────────────────────────────────────────────────────────
    // COMPONENTS
    // ─────────────────────────────────────────────────────────────

    private APFController  apf;
    private ThermalDetector thermal;
    private Rigidbody      rb;

    // RTH target holder
    private GameObject rthTargetObj;

    // ─────────────────────────────────────────────────────────────
    // INIT
    // ─────────────────────────────────────────────────────────────

    public void Initialize(int id, bool isLeader, Vector3 homePos)
    {
        DroneID      = id;
        IsLeader     = isLeader;
        HomePosition = homePos;

        apf     = GetComponent<APFController>();
        thermal = GetComponent<ThermalDetector>();
        rb      = GetComponent<Rigidbody>();

        // Create RTH target object (destroy previous if re-initializing)
        if (rthTargetObj != null) Destroy(rthTargetObj);
        rthTargetObj                    = new GameObject($"RTH_Target_Drone{id}");
        rthTargetObj.transform.position = homePos;

        apf.ResetStats();
        thermal.ResetDetections();

        LastCheckedPosition = transform.position;
        TimeStationary      = 0f;

        Debug.Log($"[Drone {DroneID}] Initialized | Leader: {IsLeader} | Home: {homePos}");
    }

    // ─────────────────────────────────────────────────────────────
    // MISSION CONTROL
    // ─────────────────────────────────────────────────────────────

    public void StartMission(Transform target)
    {
        CurrentTarget = target;
        Mode          = DroneMode.Active;
        Debug.Log($"[Drone {DroneID}] Mission started → target: {target.name}");
    }

    public void AssignNewTarget(Transform target)
    {
        if (Mode == DroneMode.RTH || Mode == DroneMode.Landed) return;
        CurrentTarget = target;
        Debug.Log($"[Drone {DroneID}] New target assigned: {target.name}");
    }

    public void TriggerRTH(string reason)
    {
        if (Mode == DroneMode.RTH || Mode == DroneMode.Landed) return;
        Mode          = DroneMode.RTH;
        CurrentTarget = rthTargetObj.transform;
        Debug.Log($"[Drone {DroneID}] RTH triggered | Reason: {reason} | Battery: {BatteryLevel:F1}%");
    }

    // ─────────────────────────────────────────────────────────────
    // FIXED UPDATE
    // ─────────────────────────────────────────────────────────────

    void FixedUpdate()
    {
        if (Mode == DroneMode.Idle || Mode == DroneMode.Landed) return;

        float safeDelta = Mathf.Min(Time.fixedDeltaTime, Sim1Variables.SafeDeltaTimeCap);

        // Battery drain
        BatteryLevel -= Sim1Variables.SearchDrainRate * safeDelta;
        BatteryLevel  = Mathf.Max(0f, BatteryLevel);

        // RTH check
        if (BatteryLevel <= Sim1Variables.RTHThreshold && Mode != DroneMode.RTH)
            TriggerRTH("Battery threshold reached");

        // Navigate using APF toward current target
        if (CurrentTarget != null)
        {
            Vector3 velocity = apf.ComputeVelocity(CurrentTarget);
            rb.velocity      = velocity;
            apf.ApplyRotation(velocity);
        }

        // Thermal scan every frame
        thermal.Scan();

        // Check if reached target
        CheckTargetReached();
    }

    // ─────────────────────────────────────────────────────────────
    // TARGET REACHED
    // ─────────────────────────────────────────────────────────────

    private void CheckTargetReached()
    {
        if (CurrentTarget == null) return;

        float dist = Vector3.Distance(
            new Vector3(transform.position.x, 0, transform.position.z),
            new Vector3(CurrentTarget.position.x, 0, CurrentTarget.position.z)
        );

        if (Mode == DroneMode.RTH && dist < 1.5f)
        {
            rb.velocity      = Vector3.zero;
            Mode             = DroneMode.Landed;
            HasLanded        = true;
            MissionComplete  = true;
            Debug.Log($"[Drone {DroneID}] LANDED | Battery: {BatteryLevel:F1}% | Detections: {thermal.TotalDetections} | Stuck events: {apf.StuckEventCount}");
            return;
        }

        if (Mode == DroneMode.Active && dist < 1f)
        {
            Debug.Log($"[Drone {DroneID}] TARGET REACHED: {CurrentTarget.name}");
            MissionComplete = true;
            TriggerRTH("Target reached");
        }
    }

    // ─────────────────────────────────────────────────────────────
    // PUBLIC DATA ACCESSORS (for Sim1DataExporter)
    // ─────────────────────────────────────────────────────────────

    public float GetBatteryLevel()        => BatteryLevel;
    public int   GetTotalDetections()     => thermal.TotalDetections;
    public float GetPathLength()          => apf.PathLength;
    public int   GetStuckEventCount()     => apf.StuckEventCount;
    public int   GetWallFollowCount()     => apf.WallFollowCount;
    public bool  GetIsWallFollowing()     => apf.IsWallFollowing;

    // ─────────────────────────────────────────────────────────────
    // CLEANUP
    // ─────────────────────────────────────────────────────────────

    void OnDestroy()
    {
        if (rthTargetObj != null)
            Destroy(rthTargetObj);
    }
}
