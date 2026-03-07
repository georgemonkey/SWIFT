using UnityEngine;

public class RunManager : MonoBehaviour
{
    [Header("References")]
    public DroneSpawner droneSpawner;
    public GeofenceSectorManager sectorManager;
    public DataExporter dataExporter;
    public MissionUI missionUI;

    [Header("Time Control")]
    [Range(1f, 1000f)]
    public float timeScale = 1f;

    private bool runActive = false;
    private float runStartTime = 0f;
    private bool isWarmupRun = false;

    private Unity.Mathematics.double3[] storedGeofenceCorners;
    private Unity.Mathematics.double3[] storedStagingCorners;
    private bool geofenceStored = false;

    void Update()
    {
        Time.timeScale = timeScale;
        Time.fixedDeltaTime = Mathf.Min(0.02f * timeScale, 0.1f);

        if (runActive)
            CheckRunComplete();
    }

    public void StoreGeofence(
        Unity.Mathematics.double3[] geofenceCorners,
        Unity.Mathematics.double3[] stagingCorners)
    {
        storedGeofenceCorners = geofenceCorners;
        storedStagingCorners = stagingCorners;
        geofenceStored = true;
        isWarmupRun = true;

        Debug.Log("Geofence stored. " +
            "First run is warmup — data will not be recorded.");
    }

    public void StartRun()
    {
        if (!geofenceStored)
        {
            Debug.LogWarning("No geofence stored. Draw geofence first.");
            return;
        }

        Time.timeScale = timeScale;
        runActive = true;
        runStartTime = Time.realtimeSinceStartup;

        var existing = droneSpawner.GetDrones();
        foreach (var d in existing)
            if (d != null) Destroy(d);

        sectorManager.SetGeofenceAndSplit(
            storedGeofenceCorners,
            storedStagingCorners);

        int trialNum = isWarmupRun ? 0 : dataExporter.GetTrialCount() + 1;
        Debug.Log(
            $"{(isWarmupRun ? "WARMUP" : "Run " + trialNum)} started. " +
            $"Drones: {Variables.droneCount}, " +
            $"Algorithm: {Variables.searchPattern}");
    }

    void CheckRunComplete()
    {
        var drones = droneSpawner.GetDrones();
        if (drones == null || drones.Count == 0) return;

        bool allLanded = true;
        float totalCov = 0f;
        float totalBat = 0f;
        int count = 0;

        foreach (var drone in drones)
        {
            if (drone == null) continue;
            DroneController dc = drone.GetComponent<DroneController>();
            if (dc == null) continue;

            if (!dc.hasLanded) allLanded = false;
            totalCov += dc.coveragePercent;
            totalBat += dc.batteryLevel;
            count++;
        }

        if (allLanded && count > 0)
        {
            runActive = false;
            float elapsed = Time.realtimeSinceStartup - runStartTime;

            if (isWarmupRun)
            {
                isWarmupRun = false;
                Debug.Log("Warmup complete. " +
                    "Next run will be recorded as Trial 1.");

                if (missionUI != null)
                    missionUI.OnWarmupComplete();
            }
            else
            {
                DataExporter.TrialData data = new DataExporter.TrialData
                {
                    droneCount = Variables.droneCount,
                    algorithm = Variables.searchPattern,
                    aggression = Variables.aggression,
                    rthThreshold = Variables.rth,
                    timeToComplete = elapsed,
                    avgCoverage = totalCov / count,
                    avgBatteryRemaining = totalBat / count,
                    totalAreaSqFt = Variables.totalAreaCoveredSqFt,
                    totalWaypoints = Variables.totalWaypointsCompleted,
                    totalDetections = Variables.totalDetections,
                    searchSpeedMph = Variables.SEARCH_SPEED_MS * 2.237f,
                    travelSpeedMph = Variables.TRAVEL_SPEED_MS * 2.237f
                };

                dataExporter.RecordTrial(data);

                Debug.Log(
                    $"Trial {dataExporter.GetTrialCount()} recorded. " +
                    $"Time: {data.timeToComplete:F1}s, " +
                    $"Coverage: {data.avgCoverage:F1}%, " +
                    $"Battery: {data.avgBatteryRemaining:F1}%");

                if (missionUI != null)
                    missionUI.OnRunComplete(data);
            }
        }
    }

    public void SetTimeScale(float scale)
    {
        timeScale = Mathf.Clamp(scale, 1f, 1000f);
        Time.timeScale = timeScale;
        Debug.Log($"Time scale: {timeScale}x");
    }

    public bool IsRunActive() => runActive;
    public bool HasGeofence() => geofenceStored;
    public bool IsWarmupRun() => isWarmupRun;
}