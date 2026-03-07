using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// Displays real-time mission stats for Sim1.
/// Shows per-drone status and aggregate mission data.
/// Attach to a Canvas GameObject alongside Sim1Manager.
///
/// Required UI elements (assign in Inspector):
///   - DroneStatusText   (per-drone mode, battery, stuck events)
///   - MissionStatsText  (time, detections, reassignments)
///   - StatusText        (current mission state)
/// </summary>
public class Sim1UI : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────
    // INSPECTOR
    // ─────────────────────────────────────────────────────────────

    [Header("UI Text Elements")]
    public Text DroneStatusText;
    public Text MissionStatsText;
    public Text StatusText;
    public Text SettingsText;

    [Header("Buttons")]
    public Button StartButton;
    public Button ExportButton;
    public Button ToggleAPFButton;
    public Button ToggleLFButton;

    // ─────────────────────────────────────────────────────────────
    // PRIVATE REFS
    // ─────────────────────────────────────────────────────────────

    private Sim1Manager    manager;
    private Sim1DataExporter exporter;

    private List<DroneAgent> drones = new List<DroneAgent>();
    private float missionStartTime;
    private bool  missionActive = false;

    // ─────────────────────────────────────────────────────────────
    // INIT
    // ─────────────────────────────────────────────────────────────

    void Start()
    {
        manager  = FindObjectOfType<Sim1Manager>();
        exporter = FindObjectOfType<Sim1DataExporter>();

        if (StartButton  != null) StartButton.onClick.AddListener(OnStartClicked);
        if (ExportButton != null) ExportButton.onClick.AddListener(OnExportClicked);
        if (ToggleAPFButton != null) ToggleAPFButton.onClick.AddListener(OnToggleAPF);
        if (ToggleLFButton  != null) ToggleLFButton.onClick.AddListener(OnToggleLF);

        SetStatus("READY — Press R or Start to run");
    }

    // ─────────────────────────────────────────────────────────────
    // UPDATE
    // ─────────────────────────────────────────────────────────────

    void Update()
    {
        if (!missionActive) return;

        UpdateDroneStatus();
        UpdateMissionStats();
    }

    // ─────────────────────────────────────────────────────────────
    // DRONE STATUS TEXT
    // ─────────────────────────────────────────────────────────────

    private void UpdateDroneStatus()
    {
        if (DroneStatusText == null) return;

        drones = new List<DroneAgent>(FindObjectsOfType<DroneAgent>());
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("=== DRONE STATUS ===");

        foreach (DroneAgent d in drones)
        {
            sb.AppendLine(
                $"Drone {d.DroneID} [{(d.IsLeader ? "LEADER" : "FOLLOWER")}]\n" +
                $"  Mode:    {d.Mode}\n" +
                $"  Battery: {d.GetBatteryLevel():F1}%\n" +
                $"  Detects: {d.GetTotalDetections()}\n" +
                $"  Stuck:   {d.GetStuckEventCount()}\n" +
                $"  Walls:   {d.GetWallFollowCount()}"
            );
        }

        DroneStatusText.text = sb.ToString();
    }

    // ─────────────────────────────────────────────────────────────
    // MISSION STATS TEXT
    // ─────────────────────────────────────────────────────────────

    private void UpdateMissionStats()
    {
        if (MissionStatsText == null) return;

        float elapsed = Time.time - missionStartTime;

        LeaderFollowerSystem lf = FindObjectOfType<LeaderFollowerSystem>();
        int reassignments = lf != null ? lf.TotalReassignments : 0;

        int totalDetections = 0;
        foreach (DroneAgent d in drones)
            totalDetections += d.GetTotalDetections();

        MissionStatsText.text =
            $"=== MISSION STATS ===\n" +
            $"Time:          {elapsed:F1}s\n" +
            $"Detections:    {totalDetections}\n" +
            $"Reassignments: {reassignments}\n" +
            $"APF:           {(manager != null && manager.apfActive ? "ON" : "OFF")}\n" +
            $"LF:            {(manager != null && manager.leaderFollowerActive ? "ON" : "OFF")}";
    }

    // ─────────────────────────────────────────────────────────────
    // CALLBACKS
    // ─────────────────────────────────────────────────────────────

    public void OnMissionStart()
    {
        missionActive    = true;
        missionStartTime = Time.time;
        SetStatus("MISSION RUNNING...");
    }

    public void OnMissionComplete(float time)
    {
        missionActive = false;
        SetStatus($"MISSION COMPLETE — {time:F2}s | Press R to run again | E to export");
    }

    private void OnStartClicked()  => manager?.StartRun();
    private void OnExportClicked() => exporter?.ExportCSV();

    private void OnToggleAPF()
    {
        if (manager == null) return;
        manager.apfActive = !manager.apfActive;
        UpdateSettingsText();
    }

    private void OnToggleLF()
    {
        if (manager == null) return;
        manager.leaderFollowerActive = !manager.leaderFollowerActive;
        UpdateSettingsText();
    }

    private void UpdateSettingsText()
    {
        if (SettingsText == null || manager == null) return;
        SettingsText.text =
            $"APF: {(manager.apfActive ? "ON" : "OFF")}\n" +
            $"Leader-Follower: {(manager.leaderFollowerActive ? "ON" : "OFF")}";
    }

    private void SetStatus(string msg)
    {
        if (StatusText != null) StatusText.text = msg;
        Debug.Log($"[Sim1UI] {msg}");
    }
}
