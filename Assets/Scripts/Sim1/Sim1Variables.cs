using UnityEngine;

/// <summary>
/// Global constants and configuration for Sim1 (APF + Leader-Follower obstacle environment)
/// All physical values derived from real aerodynamic and sensor calculations
/// </summary>
public static class Sim1Variables
{
    // ─────────────────────────────────────────────────────────────
    // DRONE MOVEMENT
    // ─────────────────────────────────────────────────────────────

    /// <summary>Base movement speed in m/s (30 mph search speed)</summary>
    public static float DroneSpeed = 13.41f;

    /// <summary>Height drone maintains above ground in meters</summary>
    public static float FlyHeight = 10f;

    /// <summary>How aggressively drone corrects altitude error</summary>
    public static float HeightCorrectionStrength = 12f;

    /// <summary>Max speed drone can move at full throttle</summary>
    public static float MaxSpeed = 13.41f;

    // ─────────────────────────────────────────────────────────────
    // APF SETTINGS
    // ─────────────────────────────────────────────────────────────

    /// <summary>k_att — attractive force scaling constant</summary>
    public static float AttractionStrength = 5f;

    /// <summary>k_rep — repulsive force scaling constant</summary>
    public static float RepulsionStrength = 300f;

    /// <summary>d_0 — obstacle influence radius in meters</summary>
    public static float MinSafeDistance = 8f;

    /// <summary>Distance at which drone starts slowing down near obstacles</summary>
    public static float SlowDownRadius = 12f;

    /// <summary>Minimum speed multiplier when right next to obstacle (prevents full stop)</summary>
    public static float MinSpeedNearObstacle = 0.3f;

    /// <summary>Per-lidar repulsion force cap (prevents explosive spikes)</summary>
    public static float MaxRepulsionForce = 8f;

    // ─────────────────────────────────────────────────────────────
    // LIDAR SENSOR
    // ─────────────────────────────────────────────────────────────

    /// <summary>Maximum lidar raycast range in meters</summary>
    public static float LidarMaxRange = 12f;

    /// <summary>Lidar smoothing lerp factor (reduces jitter)</summary>
    public static float LidarSmoothing = 0.15f;

    /// <summary>
    /// Thermal detection radius in meters
    /// Derived from: altitude(10m) x tan(FOV/2) = 10 x tan(27.5deg) = 5.2m
    /// </summary>
    public static float ThermalDetectionRadius = 5.2f;

    // ─────────────────────────────────────────────────────────────
    // WALL FOLLOWING (local minima escape)
    // ─────────────────────────────────────────────────────────────

    /// <summary>Force applied when sliding along a wall</summary>
    public static float WallFollowStrength = 8f;

    /// <summary>Seconds before checking if drone is stuck</summary>
    public static float StuckThreshold = 1.5f;

    /// <summary>Minimum distance moved before considered stuck (meters)</summary>
    public static float StuckDistance = 0.5f;

    /// <summary>How long to follow wall before returning to APF navigation</summary>
    public static float WallFollowTimeout = 4f;

    // ─────────────────────────────────────────────────────────────
    // SMOOTHING
    // ─────────────────────────────────────────────────────────────

    /// <summary>Force and velocity lerp factor (reduces oscillation)</summary>
    public static float VelocitySmoothing = 0.08f;

    /// <summary>Rotation lerp speed</summary>
    public static float RotationSmoothing = 3f;

    // ─────────────────────────────────────────────────────────────
    // BATTERY MODEL
    // Derived from aerodynamic drag: power scales with v^2
    // Reference: 100% / 360s at 120mph = 0.2778%/s
    // Travel drain: (35.76/53.64)^2 x 0.2778 = 0.1235%/s
    // Search drain: (13.41/53.64)^2 x 0.2778 = 0.1042%/s
    // ─────────────────────────────────────────────────────────────

    /// <summary>Battery drain per second while traveling at speed</summary>
    public static float SearchDrainRate = 0.1042f;

    /// <summary>Battery % at which drone returns to home</summary>
    public static float RTHThreshold = 37f;

    // ─────────────────────────────────────────────────────────────
    // LEADER-FOLLOWER
    // ─────────────────────────────────────────────────────────────

    /// <summary>How often leader checks follower status in seconds</summary>
    public static float FollowerCheckInterval = 1.0f;

    /// <summary>Distance threshold for considering a follower stuck</summary>
    public static float FollowerStuckDistance = 0.3f;

    /// <summary>Seconds follower must be stationary before leader reassigns</summary>
    public static float FollowerStuckTime = 3.0f;

    /// <summary>Separation distance between drones in formation (meters)</summary>
    public static float SeparationDistance = 5f;

    // ─────────────────────────────────────────────────────────────
    // SIMULATION
    // ─────────────────────────────────────────────────────────────

    /// <summary>Safe deltaTime cap to prevent physics instability</summary>
    public static float SafeDeltaTimeCap = 0.05f;

    /// <summary>Number of drones in the swarm</summary>
    public static int DroneCount = 4;
}
