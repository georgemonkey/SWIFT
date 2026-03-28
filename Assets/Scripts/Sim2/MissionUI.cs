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

    [Header("UI")]
    public Button resetButton;
    public Button startRunButton;
    public Button exportButton;
    public Slider timeScaleSlider;

    [Header("Text Boxes")]
    public TMP_Text leftPanelText;      // Parameters + Drone Status
    public TMP_Text rightPanelText;     // Mission Stats + Sector Areas + Trial Info
    public TMP_Text statusText;         // Bottom center status bar

    [Header("Between-Run Variables")]
    public TMP_InputField droneCountInput;
    public TMP_Dropdown algorithmDropdown;

    private float elapsedTime = 0f;
    private bool missionRunning = false;
    private string lastRunSummary = "";

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
        UpdateAllPanels();
    }

    void Update()
    {
        if (missionRunning)
        {
            elapsedTime += Time.unscaledDeltaTime;
            Variables.missionElapsedTime = elapsedTime;
        }

        UpdateAllPanels();

        var drones = droneSpawner?.GetDrones();
        if (drones != null && drones.Count > 0 && !missionRunning)
        {
            missionRunning = true;
            elapsedTime = 0f;
        }
    }

    // ?????????????????????????????????????????????????????????????
    // PANEL UPDATES — everything in two text boxes
    // ?????????????????????????????????????????????????????????????

    void UpdateAllPanels()
    {
        UpdateLeftPanel();
        UpdateRightPanel();
    }

    void UpdateLeftPanel()
    {
        if (leftPanelText == null) return;

        var sb = new System.Text.StringBuilder();

        // ?? Parameters ???????????????????????????????????????????
        sb.AppendLine("<b>PARAMETERS</b>");
        sb.AppendLine($"Drones:   {Variables.droneCount}");
        sb.AppendLine($"Pattern:  {Variables.searchPattern}");
        sb.AppendLine($"Aggress:  {Variables.aggression}");
        sb.AppendLine($"RTH:      {Variables.rth:F0}%");
        sb.AppendLine($"Travel:   {Variables.TRAVEL_SPEED_MS * 2.237f:F0} mph");
        sb.AppendLine($"Search:   {Variables.SEARCH_SPEED_MS * 2.237f:F0} mph");
        sb.AppendLine($"Size:     6\" x 6\"");
        sb.AppendLine($"Speed:    {(runManager != null ? runManager.timeScale : 1f):F0}x");

        sb.AppendLine();

        // ?? Drone Status ?????????????????????????????????????????
        sb.AppendLine("<b>DRONE STATUS</b>");

        var drones = droneSpawner?.GetDrones();
        if (drones != null && drones.Count > 0)
        {
            foreach (var drone in drones)
            {
                if (drone == null) continue;
                DroneController dc = drone.GetComponent<DroneController>();
                if (dc == null) continue;

                string role = dc.isLeader ? "LDR" : "FLW";
                string mode = dc.hasLanded ? "LAND" :
                              dc.missionComplete ? "RTH" :
                              dc.isTraveling ? "TRVL" : "SRCH";

                sb.AppendLine(
                    $"D{dc.droneId} [{role}][{mode}] " +
                    $"{dc.coveragePercent:F0}% | {dc.batteryLevel:F0}%");
            }
        }
        else
        {
            sb.AppendLine("No drones active");
        }

        leftPanelText.text = sb.ToString();
    }

    void UpdateRightPanel()
    {
        if (rightPanelText == null) return;

        var sb = new System.Text.StringBuilder();

        // ?? Mission Stats ????????????????????????????????????????
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

        sb.AppendLine("<b>MISSION STATS</b>");
        sb.AppendLine($"{trialLabel}");
        sb.AppendLine($"Time:       {mins:00}:{secs:00}");
        sb.AppendLine($"Coverage:   {avgCoverage:F1}%");
        sb.AppendLine($"Waypoints:  {Variables.totalWaypointsCompleted}");
        sb.AppendLine($"Detections: {Variables.totalDetections}");

        sb.AppendLine();

        // ?? Sector Areas ?????????????????????????????????????????
        if (sectorManager != null)
        {
            var sectors = sectorManager.GetSectors();
            if (sectors != null && sectors.Count > 0)
            {
                sb.AppendLine("<b>SECTOR AREAS</b>");
                float total = 0f;
                foreach (var s in sectors)
                {
                    float area = s.AreaSqFt();
                    total += area;
                    sb.AppendLine($"S{s.droneId + 1}: {area:F0} ft²");
                }
                sb.AppendLine($"Total: {total:F0} ft²");
                sb.AppendLine();
            }
        }

        // ?? Last Run Summary ?????????????????????????????????????
        if (!string.IsNullOrEmpty(lastRunSummary))
        {
            sb.AppendLine("<b>LAST RUN</b>");
            sb.AppendLine(lastRunSummary);
            sb.AppendLine();
        }

        // ?? Trial Count ??????????????????????????????????????????
        if (dataExporter != null)
        {
            string trialCount = runManager != null && runManager.IsWarmupRun()
                ? "Warmup"
                : $"Trials: {dataExporter.GetTrialCount()}";
            sb.AppendLine(trialCount);
        }

        rightPanelText.text = sb.ToString();
    }

    // ?????????????????????????????????????????????????????????????
    // RUN CONTROLS
    // ?????????????????????????????????????????????????????????????

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
        lastRunSummary = "WARMUP COMPLETE\nData not recorded.\nPress Start Run for Trial 1.";
        SetStatus("Warmup complete. Change variables if needed, then press Start Run for Trial 1.");
    }

    public void OnRunComplete(DataExporter.TrialData data)
    {
        missionRunning = false;

        lastRunSummary =
            $"Trial {dataExporter.GetTrialCount()}\n" +
            $"Drones: {data.droneCount}\n" +
            $"Algo: {data.algorithm}\n" +
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
        SetStatus($"Exported {dataExporter.GetTrialCount()} trials to Desktop.");
    }

    // ?????????????????????????????????????????????????????????????
    // BETWEEN-RUN VARIABLE CHANGES
    // ?????????????????????????????????????????????????????????????

    void OnDroneCountChanged(string value)
    {
        if (int.TryParse(value, out int count) && count >= 1 && count <= 16)
        {
            Variables.droneCount = count;
            Variables.activeDroneCount = count;
            SetStatus($"Drone count set to {count}. Press Start Run to apply.");
        }
        else
            SetStatus("Enter drone count 1-16.");
    }

    void OnAlgorithmChanged(int index)
    {
        string[] algorithms = { "Lawnmower", "Spiral", "Expanding Square", "Random Walk" };
        if (index < algorithms.Length)
        {
            Variables.searchPattern = algorithms[index];
            SetStatus($"Algorithm set to {Variables.searchPattern}. Press Start Run to apply.");
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
        lastRunSummary = "";

        var drones = droneSpawner?.GetDrones();
        if (drones != null)
            foreach (var d in drones)
                if (d != null) Destroy(d);

        Time.timeScale = 1f;
        if (timeScaleSlider != null) timeScaleSlider.value = 1f;

        SetStatus("Reset. Draw geofence again.");
        UpdateAllPanels();
    }

    // ?????????????????????????????????????????????????????????????
    // STATUS
    // ?????????????????????????????????????????????????????????????

    void SetStatus(string msg)
    {
        if (statusText != null) statusText.text = msg;
        Debug.Log(msg);
    }
}