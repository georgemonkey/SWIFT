using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class MissionUI : MonoBehaviour
{
    [Header("References")]
    public GeofenceDrawer geofenceDrawer;
    public DroneSpawner droneSpawner;
    public GeofenceSectorManager sectorManager;

    [Header("UI Elements")]
    public Button resetButton;
    public TMP_Text statusText;

    [Header("Stats Overlay")]
    public TMP_Text droneStatusText;   // per-drone status
    public TMP_Text missionStatsText;  // mission-wide stats
    public TMP_Text sectorAreaText;    // sector area top right
    public TMP_Text variablesText;     // all Variables displayed

    private float elapsedTime = 0f;
    private bool missionRunning = false;

    void Start()
    {
        if (resetButton != null)
            resetButton.onClick.AddListener(OnReset);

        SetStatus("Step 1: Draw search geofence. " +
            "Step 2: Draw staging zone.");

        // Show variables immediately on load
        DisplayVariables();
    }

    void Update()
    {
        if (missionRunning)
        {
            elapsedTime += Time.deltaTime;
            Variables.missionElapsedTime = elapsedTime;
        }

        UpdateDroneStatus();
        UpdateMissionStats();
        UpdateSectorArea();

        // Check if mission started
        var drones = droneSpawner?.GetDrones();
        if (drones != null && drones.Count > 0 && !missionRunning)
            missionRunning = true;
    }

    void DisplayVariables()
    {
        if (variablesText == null) return;

        variablesText.text =
            $"<b>MISSION PARAMETERS</b>\n" +
            $"Drones: {Variables.droneCount}\n" +
            $"Active: {Variables.activeDroneCount}\n" +
            $"Pattern: {Variables.searchPattern}\n" +
            $"Aggression: {Variables.aggression}\n" +
            $"RTH Threshold: {Variables.rth:F0}%\n" +
            $"Alt Delta Rate: {Variables.altitudeDelateRate} m/s\n" +
            $"Separation: {Variables.seperationDistance} m\n" +
            $"Travel Speed: {Variables.TRAVEL_SPEED_MS * 2.237f:F0} mph\n" +
            $"Search Speed: {Variables.SEARCH_SPEED_MS * 2.237f:F0} mph\n" +
            $"Drone Size: 6\" x 6\"";
    }

    void UpdateDroneStatus()
    {
        if (droneSpawner == null || droneStatusText == null) return;

        var drones = droneSpawner.GetDrones();
        if (drones == null || drones.Count == 0) return;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<b>DRONE STATUS</b>");

        foreach (var drone in drones)
        {
            if (drone == null) continue;
            DroneController dc = drone.GetComponent<DroneController>();
            if (dc == null) continue;

            string role = dc.isLeader ? "LEADER" : "FOLLOWER";
            string mode = dc.missionComplete ? "DONE" :
                            dc.IsTraveling() ? "TRAVEL" : "SEARCH";
            string status = dc.missionComplete ? "?" : "?";

            sb.AppendLine(
                $"{status} Drone {dc.droneId} [{role}] [{mode}]\n" +
                $"   Coverage: {dc.coveragePercent:F1}% | " +
                $"Battery: {dc.batteryLevel:F0}%");
        }

        droneStatusText.text = sb.ToString();
    }

    void UpdateMissionStats()
    {
        if (missionStatsText == null) return;

        int mins = Mathf.FloorToInt(elapsedTime / 60f);
        int secs = Mathf.FloorToInt(elapsedTime % 60f);

        // Average coverage across all drones
        float avgCoverage = 0f;
        int droneCount = 0;
        var drones = droneSpawner?.GetDrones();
        if (drones != null)
        {
            foreach (var d in drones)
            {
                if (d == null) continue;
                DroneController dc = d.GetComponent<DroneController>();
                if (dc == null) continue;
                avgCoverage += dc.coveragePercent;
                droneCount++;
            }
            if (droneCount > 0) avgCoverage /= droneCount;
        }

        missionStatsText.text =
            $"<b>MISSION STATS</b>\n" +
            $"Time: {mins:00}:{secs:00}\n" +
            $"Avg Coverage: {avgCoverage:F1}%\n" +
            $"Waypoints Done: {Variables.totalWaypointsCompleted}\n" +
            $"Detections: {Variables.totalDetections}";
    }

    void UpdateSectorArea()
    {
        if (sectorAreaText == null || sectorManager == null) return;

        var sectors = sectorManager.GetSectors();
        if (sectors == null || sectors.Count == 0) return;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<b>SECTOR AREAS</b>");

        float total = 0f;
        foreach (var s in sectors)
        {
            float area = s.AreaSqFt();
            total += area;
            sb.AppendLine($"Sector {s.droneId + 1}: " +
                $"{area:F0} ft²");
        }
        sb.AppendLine($"Total: {total:F0} ft²");

        sectorAreaText.text = sb.ToString();
    }

    void OnReset()
    {
        geofenceDrawer?.ResetAll();
        missionRunning = false;
        elapsedTime = 0f;
        Variables.missionElapsedTime = 0f;
        Variables.totalWaypointsCompleted = 0;
        Variables.totalDetections = 0;

        var drones = droneSpawner?.GetDrones();
        if (drones != null)
            foreach (var d in drones)
                if (d != null) Destroy(d);

        SetStatus("Reset. Draw geofence again.");
        DisplayVariables();
    }

    void SetStatus(string msg)
    {
        if (statusText != null) statusText.text = msg;
        Debug.Log(msg);
    }
}

