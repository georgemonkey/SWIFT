using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class MissionUI : MonoBehaviour
{
    [Header("References")]
    public GeofenceDrawer geofenceDrawer;
    public DroneSpawner droneSpawner;
    public GeofenceSectorManager sectorManager;
    public RunManager runManager;
    public DataExporter dataExporter;

    [Header("Status UI")]
    public Button resetButton;
    public TMP_Text statusText;
    public TMP_Text droneStatusText;
    public TMP_Text missionStatsText;
    public TMP_Text sectorAreaText;
    public TMP_Text variablesText;

    [Header("Run Controls")]
    public Button startRunButton;
    public Button exportButton;
    public Slider timeScaleSlider;
    public TMP_Text timeScaleText;
    public TMP_Text trialCountText;

    [Header("Between-Run Variables")]
    public TMP_InputField droneCountInput;
    public TMP_Dropdown algorithmDropdown;
    public TMP_Text lastRunSummaryText;

    private float elapsedTime = 0f;
    private bool missionRunning = false;

    void Start()
    {
        if (resetButton != null)
            resetButton.onClick.AddListener(OnReset);

        if (startRunButton != null)
            startRunButton.onClick.AddListener(OnStartRun);

        if (exportButton != null)
            exportButton.onClick.AddListener(OnExport);

        if (timeScaleSlider != null)
        {
            timeScaleSlider.minValue = 1f;
            timeScaleSlider.maxValue = 1000f;
            timeScaleSlider.value = 1f;
            timeScaleSlider.onValueChanged.AddListener(OnTimeScaleChanged);
        }

        if (droneCountInput != null)
            droneCountInput.onEndEdit.AddListener(OnDroneCountChanged);

        if (algorithmDropdown != null)
        {
            algorithmDropdown.ClearOptions();
            algorithmDropdown.AddOptions(
                new System.Collections.Generic.List<string>
                {
                    "Lawnmower",
                    "Spiral",
                    "Expanding Square",
                    "Random Walk"
                });
            algorithmDropdown.onValueChanged.AddListener(OnAlgorithmChanged);
        }

        SetStatus("Draw search geofence, then staging zone.");
        DisplayVariables();
    }

    void Update()
    {
        if (missionRunning)
        {
            elapsedTime += Time.unscaledDeltaTime;
            Variables.missionElapsedTime = elapsedTime;
        }

        UpdateDroneStatus();
        UpdateMissionStats();
        UpdateSectorArea();
        UpdateTimeScaleDisplay();
        UpdateTrialCount();
        DisplayVariables();

        var drones = droneSpawner?.GetDrones();
        if (drones != null && drones.Count > 0 && !missionRunning)
        {
            missionRunning = true;
            elapsedTime = 0f;
        }
    }

    // ?? Run Controls ??????????????????????????????????????????????

    void OnStartRun()
    {
        if (runManager == null) return;

        if (!runManager.HasGeofence())
        {
            SetStatus("Draw geofence first.");
            return;
        }

        missionRunning = false;
        elapsedTime = 0f;

        runManager.StartRun();

        string label = runManager.IsWarmupRun()
            ? "Warmup run started — data will not be recorded."
            : $"Trial {dataExporter.GetTrialCount() + 1} started. " +
              $"Drones: {Variables.droneCount}, " +
              $"Algorithm: {Variables.searchPattern}";

        SetStatus(label);
    }

    public void OnWarmupComplete()
    {
        missionRunning = false;
        SetStatus("Warmup complete. " +
            "Change variables if needed, then press Start Run for Trial 1.");

        if (lastRunSummaryText != null)
            lastRunSummaryText.text =
                "<b>WARMUP COMPLETE</b>\n" +
                "Data not recorded.\n" +
                "Press Start Run to begin Trial 1.";
    }

    public void OnRunComplete(DataExporter.TrialData data)
    {
        missionRunning = false;

        if (lastRunSummaryText != null)
            lastRunSummaryText.text =
                $"<b>TRIAL {dataExporter.GetTrialCount()}</b>\n" +
                $"Drones: {data.droneCount}\n" +
                $"Algorithm: {data.algorithm}\n" +
                $"Time: {data.timeToComplete:F1}s\n" +
                $"Coverage: {data.avgCoverage:F1}%\n" +
                $"Battery: {data.avgBatteryRemaining:F1}%";

        SetStatus(
            $"Trial {dataExporter.GetTrialCount()} complete. " +
            $"Coverage: {data.avgCoverage:F1}%. " +
            $"Change variables and press Start Run.");
    }

    void OnExport()
    {
        if (dataExporter == null) return;
        dataExporter.ExportToExcel();
        SetStatus($"Exported {dataExporter.GetTrialCount()} " +
            $"trials to Desktop.");
    }

    // ?? Between-Run Variable Changes ??????????????????????????????

    void OnDroneCountChanged(string value)
    {
        if (int.TryParse(value, out int count) &&
            count >= 1 && count <= 16)
        {
            Variables.droneCount = count;
            Variables.activeDroneCount = count;
            SetStatus($"Drone count set to {count}. " +
                $"Press Start Run to apply.");
        }
        else
            SetStatus("Enter drone count 1-16.");
    }

    void OnAlgorithmChanged(int index)
    {
        string[] algorithms =
        {
            "Lawnmower", "Spiral", "Expanding Square", "Random Walk"
        };
        if (index < algorithms.Length)
        {
            Variables.searchPattern = algorithms[index];
            SetStatus($"Algorithm set to {Variables.searchPattern}. " +
                $"Press Start Run to apply.");
        }
    }

    void OnTimeScaleChanged(float value)
    {
        if (runManager != null)
            runManager.SetTimeScale(value);
    }

    void OnReset()
    {
        geofenceDrawer?.ResetAll();
        missionRunning = false;
        elapsedTime = 0f;

        var drones = droneSpawner?.GetDrones();
        if (drones != null)
            foreach (var d in drones)
                if (d != null) Destroy(d);

        Time.timeScale = 1f;
        if (timeScaleSlider != null) timeScaleSlider.value = 1f;

        SetStatus("Reset. Draw geofence again.");
        DisplayVariables();
    }

    // ?? Display Updates ???????????????????????????????????????????

    void DisplayVariables()
    {
        if (variablesText == null) return;

        variablesText.text =
            $"<b>PARAMETERS</b>\n" +
            $"Drones: {Variables.droneCount}\n" +
            $"Pattern: {Variables.searchPattern}\n" +
            $"Aggression: {Variables.aggression}\n" +
            $"RTH: {Variables.rth:F0}%\n" +
            $"Travel: {Variables.TRAVEL_SPEED_MS * 2.237f:F0} mph\n" +
            $"Search: {Variables.SEARCH_SPEED_MS * 2.237f:F0} mph\n" +
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
            string mode = dc.hasLanded ? "LANDED" :
                            dc.missionComplete ? "RTH" :
                            dc.isTraveling ? "TRAVEL" : "SEARCH";
            string status = dc.hasLanded ? "?" : "?";

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

        float avgCoverage = 0f;
        int count = 0;
        var drones = droneSpawner?.GetDrones();
        if (drones != null)
        {
            foreach (var d in drones)
            {
                if (d == null) continue;
                DroneController dc = d.GetComponent<DroneController>();
                if (dc == null) continue;
                avgCoverage += dc.coveragePercent;
                count++;
            }
            if (count > 0) avgCoverage /= count;
        }

        string trialLabel = runManager != null && runManager.IsWarmupRun()
            ? "WARMUP"
            : $"Trial {dataExporter?.GetTrialCount() + 1}";

        missionStatsText.text =
            $"<b>MISSION STATS</b>\n" +
            $"{trialLabel}\n" +
            $"Time: {mins:00}:{secs:00}\n" +
            $"Avg Coverage: {avgCoverage:F1}%\n" +
            $"Waypoints: {Variables.totalWaypointsCompleted}\n" +
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
            sb.AppendLine($"Sector {s.droneId + 1}: {area:F0} ft²");
        }
        sb.AppendLine($"Total: {total:F0} ft²");

        sectorAreaText.text = sb.ToString();
    }

    void UpdateTimeScaleDisplay()
    {
        if (timeScaleText == null || runManager == null) return;
        timeScaleText.text = $"Speed: {runManager.timeScale:F0}x";
    }

    void UpdateTrialCount()
    {
        if (trialCountText == null || dataExporter == null) return;
        string label = runManager != null && runManager.IsWarmupRun()
            ? "Warmup"
            : $"Trials: {dataExporter.GetTrialCount()}";
        trialCountText.text = label;
    }

    void SetStatus(string msg)
    {
        if (statusText != null) statusText.text = msg;
        Debug.Log(msg);
    }
}