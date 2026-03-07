using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Text;

public class DataExporter : MonoBehaviour
{
    [Header("Export Settings")]
    public string fileName = "DroneSwarmData";

    public struct TrialData
    {
        public int trialNumber;
        public int droneCount;
        public string algorithm;
        public string aggression;
        public float rthThreshold;
        public float timeToComplete;
        public float avgCoverage;
        public float avgBatteryRemaining;
        public float totalAreaSqFt;
        public int totalWaypoints;
        public int totalDetections;
        public float searchSpeedMph;
        public float travelSpeedMph;
    }

    private List<TrialData> trials = new List<TrialData>();
    private int trialNumber = 0;

    public void RecordTrial(TrialData data)
    {
        trialNumber++;
        data.trialNumber = trialNumber;
        trials.Add(data);
        Debug.Log($"Trial {trialNumber} recorded. " +
            $"Total trials: {trials.Count}");
    }

    public void ExportToExcel()
    {
        if (trials.Count == 0)
        {
            Debug.LogWarning("No trial data to export.");
            return;
        }

        string path = Path.Combine(
            System.Environment.GetFolderPath(
                System.Environment.SpecialFolder.Desktop),
            $"{fileName}_{System.DateTime.Now:yyyy-MM-dd_HH-mm-ss}.csv");

        StringBuilder sb = new StringBuilder();

        // Header row
        sb.AppendLine(
            "Trial #," +
            "Drone Count," +
            "Algorithm," +
            "Aggression," +
            "RTH Threshold (%)," +
            "Time to Complete (s)," +
            "Avg Coverage (%)," +
            "Avg Battery Remaining (%)," +
            "Total Area (sq ft)," +
            "Total Waypoints," +
            "Total Detections," +
            "Search Speed (mph)," +
            "Travel Speed (mph)");

        // Data rows
        foreach (var t in trials)
        {
            sb.AppendLine(
                $"{t.trialNumber}," +
                $"{t.droneCount}," +
                $"{t.algorithm}," +
                $"{t.aggression}," +
                $"{t.rthThreshold:F1}," +
                $"{t.timeToComplete:F1}," +
                $"{t.avgCoverage:F1}," +
                $"{t.avgBatteryRemaining:F1}," +
                $"{t.totalAreaSqFt:F0}," +
                $"{t.totalWaypoints}," +
                $"{t.totalDetections}," +
                $"{t.searchSpeedMph:F1}," +
                $"{t.travelSpeedMph:F1}");
        }

        File.WriteAllText(path, sb.ToString());
        Debug.Log($"Data exported to: {path}");
    }

    public void ClearTrials()
    {
        trials.Clear();
        trialNumber = 0;
        Debug.Log("Trial data cleared.");
    }

    public int GetTrialCount() => trials.Count;
}