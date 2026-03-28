using UnityEngine;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// Live metrics overlay displayed during Sim2 runs.
/// Shows:
///   - Area coverage %
///   - Time to reach 80% coverage
///   - Total distance traveled
///   - Energy consumption (battery used %)
///   - Redundant coverage %
///
/// Attach to MissionManager alongside existing components.
/// Call Initialize() when run starts.
/// Call UpdateMetrics() each frame during run.
/// </summary>
public class MetricsOverlay : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────
    // INSPECTOR
    // ─────────────────────────────────────────────────────────────

    [Header("UI References")]
    public TMP_Text MetricsText;

    [Header("Panel")]
    public GameObject MetricsPanel;

    // ─────────────────────────────────────────────────────────────
    // PRIVATE STATE
    // ─────────────────────────────────────────────────────────────

    private List<DroneController> drones = new List<DroneController>();
    private GeofenceSectorManager sectorManager;

    private float missionStartTime;
    private float timeToEightyPercent = -1f;
    private bool eightyPercentReached = false;
    private bool initialized = false;
    private string currentAlgorithm = "";

    // Redundant coverage tracking
    // Counts how many times each cell has been visited
    private Dictionary<string, int> cellVisitCount = new Dictionary<string, int>();
    private int totalVisits = 0;
    private int redundantVisits = 0;

    // ─────────────────────────────────────────────────────────────
    // INIT
    // ─────────────────────────────────────────────────────────────

    void Awake()
    {
        sectorManager = GetComponent<GeofenceSectorManager>();
    }

    public void Initialize(List<DroneController> activeDrones, string algorithm)
    {
        drones = activeDrones;
        currentAlgorithm = algorithm;
        missionStartTime = Time.unscaledTime;
        timeToEightyPercent = -1f;
        eightyPercentReached = false;
        cellVisitCount.Clear();
        totalVisits = 0;
        redundantVisits = 0;
        initialized = true;

        if (MetricsPanel != null) MetricsPanel.SetActive(true);
        if (MetricsText != null) MetricsText.text = $"Algorithm: {algorithm}\nInitializing...";

        ResetDisplays();
        Debug.Log($"[MetricsOverlay] Initialized | Algorithm: {algorithm} | Drones: {drones.Count}");
    }

    public void StopTracking()
    {
        initialized = false;
    }

    // ─────────────────────────────────────────────────────────────
    // UPDATE — call from MissionUI.Update() or RunManager
    // ─────────────────────────────────────────────────────────────

    public void UpdateMetrics()
    {
        if (!initialized || drones == null || drones.Count == 0) return;

        float elapsed = Time.unscaledTime - missionStartTime;

        // ── Coverage % ──────────────────────────────────────────
        float avgCoverage = GetAverageCoverage();

        // Check 80% threshold
        if (!eightyPercentReached && avgCoverage >= 80f)
        {
            eightyPercentReached = true;
            timeToEightyPercent = elapsed;
            Debug.Log($"[MetricsOverlay] 80% coverage reached at {elapsed:F1}s");
        }

        // ── Distance traveled ────────────────────────────────────
        float totalDistance = GetTotalDistanceTraveled();

        // ── Energy consumption ───────────────────────────────────
        float avgBatteryUsed = GetAverageBatteryUsed();

        // ── Redundant coverage ───────────────────────────────────
        float redundantPct = totalVisits > 0
            ? (float)redundantVisits / totalVisits * 100f
            : 0f;

        // ── Update UI ────────────────────────────────────────────
        UpdateUI(avgCoverage, elapsed, totalDistance, avgBatteryUsed, redundantPct);
    }

    // ─────────────────────────────────────────────────────────────
    // REDUNDANT COVERAGE TRACKING
    // Called from DroneController when a waypoint cell is visited
    // ─────────────────────────────────────────────────────────────

    public void RecordCellVisit(string cellKey)
    {
        totalVisits++;

        if (cellVisitCount.ContainsKey(cellKey))
        {
            cellVisitCount[cellKey]++;
            redundantVisits++; // Already visited by any drone = redundant
        }
        else
        {
            cellVisitCount[cellKey] = 1;
        }
    }

    // ─────────────────────────────────────────────────────────────
    // DATA GATHERING
    // ─────────────────────────────────────────────────────────────

    private float GetAverageCoverage()
    {
        if (drones.Count == 0) return 0f;
        float total = 0f;
        foreach (DroneController d in drones)
            total += d.coveragePercent;
        return total / drones.Count;
    }

    private float GetTotalDistanceTraveled()
    {
        float total = 0f;
        foreach (DroneController d in drones)
            total += d.totalDistanceTraveled;
        return total;
    }

    private float GetAverageBatteryUsed()
    {
        if (drones.Count == 0) return 0f;
        float total = 0f;
        foreach (DroneController d in drones)
            total += (100f - d.batteryLevel);
        return total / drones.Count;
    }

    // ─────────────────────────────────────────────────────────────
    // UI UPDATE
    // ─────────────────────────────────────────────────────────────

    private void UpdateUI(
        float coverage,
        float elapsed,
        float distance,
        float batteryUsed,
        float redundantPct)
    {
        if (MetricsText == null) return;

        string timeToEighty = eightyPercentReached
            ? $"{timeToEightyPercent:F1}s"
            : $"{elapsed:F1}s (running)";

        MetricsText.text =
            $"Algorithm:    {currentAlgorithm}\n" +
            $"Coverage:     {coverage:F1}%\n" +
            $"Time to 80%:  {timeToEighty}\n" +
            $"Distance:     {distance:F0}m\n" +
            $"Energy Used:  {batteryUsed:F1}%\n" +
            $"Redundant:    {redundantPct:F1}%";
    }

    private void ResetDisplays()
    {
        if (MetricsText == null) return;
        MetricsText.text =
            $"Algorithm:    {currentAlgorithm}\n" +
            $"Coverage:     0%\n" +
            $"Time to 80%:  --\n" +
            $"Distance:     0m\n" +
            $"Energy Used:  0%\n" +
            $"Redundant:    0%";
    }

    // ─────────────────────────────────────────────────────────────
    // PUBLIC GETTERS (for DataExporter)
    // ─────────────────────────────────────────────────────────────

    public float GetTimeToEightyPercent() => timeToEightyPercent;
    public float GetRedundantCoveragePct() =>
        totalVisits > 0 ? (float)redundantVisits / totalVisits * 100f : 0f;
}