using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Master controller for Sim1 (APF + Leader-Follower obstacle environment).
///
/// Responsibilities:
///   1. Spawns drones at staging positions
///   2. Assigns targets from scene
///   3. Starts leader-follower monitoring
///   4. Tracks mission time
///   5. Detects mission completion
///   6. Records and exports trial data
///
/// Attach to a GameObject called "Sim1Manager" in your scene.
/// Assign: dronePrefab, stagingPositions, targetObjects in Inspector.
/// </summary>
public class Sim1Manager : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────
    // INSPECTOR
    // ─────────────────────────────────────────────────────────────

    [Header("Prefabs")]
    public GameObject dronePrefab;

    [Header("Scene References")]
    [Tooltip("Spawn positions for drones (one per drone)")]
    public Transform[] stagingPositions;

    [Tooltip("Target objects drones navigate toward (tagged Finish)")]
    public Transform[] targetObjects;

    [Header("Run Settings")]
    [Tooltip("Run APF obstacle avoidance")]
    public bool apfActive = true;

    [Tooltip("Run leader-follower coordination")]
    public bool leaderFollowerActive = true;

    [Tooltip("Notes for this trial (e.g. obstacle layout name)")]
    public string trialNotes = "";

    // ─────────────────────────────────────────────────────────────
    // PRIVATE
    // ─────────────────────────────────────────────────────────────

    private List<DroneAgent>    drones      = new List<DroneAgent>();
    private LeaderFollowerSystem lf;
    private Sim1DataExporter    exporter;
    private Sim1UI              ui;

    private float missionStartTime;
    private bool  missionEnded     = false;

    /// <summary>Read by Sim1ExperimentRunner to know when a trial has finished</summary>
    public bool MissionRunning { get; private set; } = false;

    // ─────────────────────────────────────────────────────────────
    // START
    // ─────────────────────────────────────────────────────────────

    void Start()
    {
        lf       = GetComponent<LeaderFollowerSystem>();
        exporter = GetComponent<Sim1DataExporter>();
        ui       = GetComponent<Sim1UI>();

        if (lf       == null) lf       = gameObject.AddComponent<LeaderFollowerSystem>();
        if (exporter == null) exporter = gameObject.AddComponent<Sim1DataExporter>();

        ValidateSetup();
    }

    // ─────────────────────────────────────────────────────────────
    // VALIDATION
    // ─────────────────────────────────────────────────────────────

    private void ValidateSetup()
    {
        if (dronePrefab == null)
        {
            Debug.LogError("[Sim1Manager] dronePrefab not assigned");
            return;
        }

        if (stagingPositions == null || stagingPositions.Length == 0)
        {
            Debug.LogError("[Sim1Manager] No staging positions assigned");
            return;
        }

        if (targetObjects == null || targetObjects.Length == 0)
        {
            Debug.LogError("[Sim1Manager] No target objects assigned");
            return;
        }

        Debug.Log($"[Sim1Manager] Setup valid | Drones: {stagingPositions.Length} | Targets: {targetObjects.Length}");
    }

    // ─────────────────────────────────────────────────────────────
    // RUN
    // ─────────────────────────────────────────────────────────────

    public void StartRun()
    {
        if (MissionRunning)
        {
            Debug.LogWarning("[Sim1Manager] Mission already running");
            return;
        }

        MissionRunning = true; // guard immediately before any yield
        CleanupDrones();
        StartCoroutine(RunMission());
    }

    private IEnumerator RunMission()
    {
        missionEnded = false;

        // ── Spawn drones ─────────────────────────────────────────
        int droneCount = Mathf.Min(stagingPositions.Length, Sim1Variables.DroneCount);

        for (int i = 0; i < droneCount; i++)
        {
            GameObject obj   = Instantiate(dronePrefab, stagingPositions[i].position, Quaternion.identity);
            obj.name         = $"Drone_{i}";

            DroneAgent agent = obj.GetComponent<DroneAgent>();
            if (agent == null)
            {
                Debug.LogError($"[Sim1Manager] dronePrefab missing DroneAgent component");
                yield break;
            }

            // Disable APF if not active this trial
            APFController apf = obj.GetComponent<APFController>();
            if (apf != null && !apfActive)
                apf.enabled = false;

            agent.Initialize(i, isLeader: i == 0, homePos: stagingPositions[i].position);
            drones.Add(agent);

            // Sequential takeoff delay
            yield return new WaitForSeconds(0.3f);
        }

        Debug.Log($"[Sim1Manager] {drones.Count} drones spawned");

        // ── Assign targets ───────────────────────────────────────
        List<Transform> targets = new List<Transform>(targetObjects);

        // Initialize leader-follower FIRST so MarkTargetAssigned isn't
        // wiped when Initialize rebuilds availableTargets
        lf.Initialize(drones, targets);

        for (int i = 0; i < drones.Count; i++)
        {
            if (i < targets.Count)
            {
                drones[i].StartMission(targets[i]);
                lf.MarkTargetAssigned(targets[i]);
            }
        }

        // ── Start leader-follower monitoring ─────────────────────
        if (leaderFollowerActive)
            lf.StartMission();

        // ── Start timer ──────────────────────────────────────────
        missionStartTime = Time.time;

        if (ui != null) ui.OnMissionStart();

        Debug.Log("[Sim1Manager] Mission started");

        // ── Wait for completion ──────────────────────────────────
        yield return StartCoroutine(WaitForMissionEnd(false));
    }

    private IEnumerator WaitForMissionEnd(bool timedout)
    {
        while (!lf.AllDronesLanded())
            yield return new WaitForSeconds(0.5f);

        float missionTime = Time.time - missionStartTime;

        Debug.Log($"[Sim1Manager] Mission complete | Time: {missionTime:F2}s | Timedout: {timedout}");

        // Record trial
        exporter.RecordTrial(
            drones,
            missionTime,
            lf.TotalReassignments,
            apfActive,
            leaderFollowerActive,
            timedout,
            trialNotes
        );

        MissionRunning = false;
        missionEnded   = true;

        if (ui != null) ui.OnMissionComplete(missionTime);
    }

    // ─────────────────────────────────────────────────────────────
    // UPDATE — keyboard shortcuts
    // ─────────────────────────────────────────────────────────────

    void Update()
    {
        // R = start run
        if (Input.GetKeyDown(KeyCode.R) && !MissionRunning)
            StartRun();

        // E = export CSV
        if (Input.GetKeyDown(KeyCode.E))
            exporter.ExportCSV();

        // 1 = toggle APF for next run
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            apfActive = !apfActive;
            Debug.Log($"[Sim1Manager] APF: {(apfActive ? "ON" : "OFF")}");
        }

        // 2 = toggle leader-follower for next run
        if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            leaderFollowerActive = !leaderFollowerActive;
            Debug.Log($"[Sim1Manager] LeaderFollower: {(leaderFollowerActive ? "ON" : "OFF")}");
        }
    }

    // ─────────────────────────────────────────────────────────────
    // FORCE END (called by ExperimentRunner on timeout)
    // ─────────────────────────────────────────────────────────────

    public void ForceEndMission()
    {
        if (!MissionRunning) return;

        // RTH all active drones immediately
        foreach (DroneAgent d in drones)
        {
            if (d.Mode != DroneAgent.DroneMode.Landed)
                d.TriggerRTH("Timeout");
        }

        Debug.LogWarning("[Sim1Manager] Mission TIMED OUT — forcing RTH on all drones");
    }

    // ─────────────────────────────────────────────────────────────
    // CLEANUP
    // ─────────────────────────────────────────────────────────────

    private void CleanupDrones()
    {
        foreach (DroneAgent d in drones)
            if (d != null) Destroy(d.gameObject);

        drones.Clear();
    }

    void OnDestroy() => CleanupDrones();
}
