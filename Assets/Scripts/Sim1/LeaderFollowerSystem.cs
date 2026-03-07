using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Implements leader-follower coordination for the drone swarm.
///
/// Leader (Drone 0) responsibilities:
///   - Polls all followers every FollowerCheckInterval seconds
///   - Detects stuck drones by position delta threshold
///   - Reassigns the stuck drone to a new target from the available pool
///   - Maintains separation between drones
///
/// Follower responsibilities:
///   - Report state to leader via DroneAgent public properties
///   - Accept new target assignments from leader
///
/// This is what compensates for cheap hardware — software coordination
/// ensures no drone stays stuck, maximizing total coverage.
/// </summary>
public class LeaderFollowerSystem : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────
    // PUBLIC STATE
    // ─────────────────────────────────────────────────────────────

    public int TotalReassignments { get; private set; }

    // ─────────────────────────────────────────────────────────────
    // PRIVATE
    // ─────────────────────────────────────────────────────────────

    private List<DroneAgent>   allDrones      = new List<DroneAgent>();
    private DroneAgent         leader;
    private List<Transform>    targetPool     = new List<Transform>();
    private List<Transform>    availableTargets = new List<Transform>();

    private bool missionStarted = false;

    // ─────────────────────────────────────────────────────────────
    // SETUP
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by Sim1Manager after spawning all drones.
    /// Drone 0 is always leader.
    /// </summary>
    public void Initialize(List<DroneAgent> drones, List<Transform> targets)
    {
        allDrones    = drones;
        targetPool   = new List<Transform>(targets);
        availableTargets = new List<Transform>(targets);

        leader = allDrones.FirstOrDefault(d => d.DroneID == 0);

        if (leader == null)
        {
            Debug.LogError("[LeaderFollower] No drone with ID 0 found — cannot assign leader");
            return;
        }

        Debug.Log($"[LeaderFollower] Initialized | Leader: Drone {leader.DroneID} | Drones: {allDrones.Count} | Targets: {targetPool.Count}");
    }

    public void StartMission()
    {
        missionStarted = true;
        StartCoroutine(MonitorFollowers());
    }

    // ─────────────────────────────────────────────────────────────
    // MONITOR LOOP — runs every FollowerCheckInterval seconds
    // ─────────────────────────────────────────────────────────────

    private IEnumerator MonitorFollowers()
    {
        // Warm-up: wait one extra interval before first check so drones
        // have time to move away from their spawn positions
        yield return new WaitForSeconds(Sim1Variables.FollowerCheckInterval * 2f);

        while (missionStarted)
        {
            yield return new WaitForSeconds(Sim1Variables.FollowerCheckInterval);

            foreach (DroneAgent drone in allDrones)
            {
                // Skip leader, idle, RTH, and landed drones
                if (drone.IsLeader)                              continue;
                if (drone.Mode == DroneAgent.DroneMode.Idle)     continue;
                if (drone.Mode == DroneAgent.DroneMode.RTH)      continue;
                if (drone.Mode == DroneAgent.DroneMode.Landed)   continue;

                CheckIfStuck(drone);
            }

            // Log swarm status every 5 checks
            LogSwarmStatus();
        }
    }

    // ─────────────────────────────────────────────────────────────
    // STUCK DETECTION
    // ─────────────────────────────────────────────────────────────

    private void CheckIfStuck(DroneAgent drone)
    {
        float distMoved = Vector3.Distance(
            new Vector3(drone.transform.position.x, 0, drone.transform.position.z),
            new Vector3(drone.LastCheckedPosition.x, 0, drone.LastCheckedPosition.z)
        );

        if (distMoved < Sim1Variables.FollowerStuckDistance)
        {
            drone.TimeStationary += Sim1Variables.FollowerCheckInterval;

            if (drone.TimeStationary >= Sim1Variables.FollowerStuckTime)
            {
                // Drone is stuck — reassign to new target
                ReassignDrone(drone);
                drone.TimeStationary = 0f;
            }
        }
        else
        {
            // Drone is moving — reset stationary timer
            drone.TimeStationary      = 0f;
            drone.LastCheckedPosition = drone.transform.position;
        }

        drone.LastCheckedPosition = drone.transform.position;
    }

    // ─────────────────────────────────────────────────────────────
    // REASSIGNMENT
    // ─────────────────────────────────────────────────────────────

    private void ReassignDrone(DroneAgent drone)
    {
        if (availableTargets.Count == 0)
        {
            Debug.Log($"[LeaderFollower] Drone {drone.DroneID} stuck but no targets available");
            return;
        }

        // Pick closest available target to reduce travel time
        Transform bestTarget = GetClosestTarget(drone.transform.position);

        if (bestTarget == null) return;

        availableTargets.Remove(bestTarget);
        drone.AssignNewTarget(bestTarget);
        TotalReassignments++;

        Debug.Log($"[LeaderFollower] Drone {drone.DroneID} REASSIGNED → {bestTarget.name} | Total reassignments: {TotalReassignments}");
    }

    private Transform GetClosestTarget(Vector3 from)
    {
        Transform closest = null;
        float     minDist = float.MaxValue;

        foreach (Transform t in availableTargets)
        {
            float d = Vector3.Distance(from, t.position);
            if (d < minDist)
            {
                minDist  = d;
                closest  = t;
            }
        }

        return closest;
    }

    // ─────────────────────────────────────────────────────────────
    // TARGET POOL MANAGEMENT
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by Sim1Manager when assigning initial targets to drones.
    /// Removes target from available pool so it won't be double-assigned.
    /// </summary>
    public void MarkTargetAssigned(Transform target)
    {
        availableTargets.Remove(target);
    }

    /// <summary>
    /// Called when a drone finishes its target — returns target to pool
    /// so another drone can be sent there if needed.
    /// </summary>
    public void ReturnTargetToPool(Transform target)
    {
        if (!availableTargets.Contains(target))
            availableTargets.Add(target);
    }

    // ─────────────────────────────────────────────────────────────
    // STATUS LOGGING
    // ─────────────────────────────────────────────────────────────

    private int logCounter = 0;

    private void LogSwarmStatus()
    {
        logCounter++;
        if (logCounter % 5 != 0) return;

        int active = allDrones.Count(d => d.Mode == DroneAgent.DroneMode.Active);
        int rth    = allDrones.Count(d => d.Mode == DroneAgent.DroneMode.RTH);
        int landed = allDrones.Count(d => d.Mode == DroneAgent.DroneMode.Landed);

        Debug.Log($"[LeaderFollower] STATUS — Active: {active} | RTH: {rth} | Landed: {landed} | Reassignments: {TotalReassignments}");
    }

    // ─────────────────────────────────────────────────────────────
    // CHECK ALL LANDED (for Sim1Manager to detect mission end)
    // ─────────────────────────────────────────────────────────────

    public bool AllDronesLanded()
    {
        return allDrones.All(d => d.HasLanded);
    }
}
