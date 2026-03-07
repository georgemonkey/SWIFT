using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System;
using System.Text;

/// <summary>
/// Exports Sim1 trial data to a timestamped CSV on the Desktop.
/// Records per-drone stats and aggregate mission stats for each trial.
/// Used to quantify APF and leader-follower contribution.
///
/// Columns:
///   Trial, DroneCount, MissionTime, TotalDetections, TotalReassignments,
///   SuccessRate, AvgBattery, AvgPathLength, AvgStuckEvents, AvgWallFollowCount,
///   APFActive, LeaderFollowerActive
/// </summary>
public class Sim1DataExporter : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────
    // DATA STRUCTURES
    // ─────────────────────────────────────────────────────────────

    public class TrialRecord
    {
        public int    TrialNumber;
        public int    DroneCount;
        public float  MissionTimeSeconds;
        public int    TotalDetections;
        public int    TotalReassignments;
        public float  SuccessRate;          // % of drones that reached target
        public float  AvgBatteryRemaining;
        public float  AvgPathLength;
        public float  AvgStuckEvents;
        public float  AvgWallFollowCount;
        public bool   APFActive;
        public bool   LeaderFollowerActive;
        public bool   Timedout;
        public string Notes;
    }

    // ─────────────────────────────────────────────────────────────
    // PRIVATE
    // ─────────────────────────────────────────────────────────────

    private List<TrialRecord> records = new List<TrialRecord>();
    private int trialCounter = 0;

    // ─────────────────────────────────────────────────────────────
    // RECORD TRIAL
    // ─────────────────────────────────────────────────────────────

    public void RecordTrial(
        List<DroneAgent> drones,
        float            missionTime,
        int              totalReassignments,
        bool             apfActive,
        bool             leaderFollowerActive,
        bool             timedout = false,
        string           notes = "")
    {
        trialCounter++;

        int   totalDetections  = 0;
        float totalBattery     = 0f;
        float totalPathLength  = 0f;
        float totalStuck       = 0f;
        float totalWallFollow  = 0f;
        int   successCount     = 0;

        foreach (DroneAgent d in drones)
        {
            totalDetections += d.GetTotalDetections();
            totalBattery    += d.GetBatteryLevel();
            totalPathLength += d.GetPathLength();
            totalStuck      += d.GetStuckEventCount();
            totalWallFollow += d.GetWallFollowCount();
            if (d.MissionComplete) successCount++;
        }

        int count = Mathf.Max(drones.Count, 1);

        TrialRecord record = new TrialRecord
        {
            TrialNumber          = trialCounter,
            DroneCount           = drones.Count,
            MissionTimeSeconds   = missionTime,
            TotalDetections      = totalDetections,
            TotalReassignments   = totalReassignments,
            SuccessRate          = (float)successCount / drones.Count * 100f,
            AvgBatteryRemaining  = totalBattery    / count,
            AvgPathLength        = totalPathLength  / count,
            AvgStuckEvents       = totalStuck       / count,
            AvgWallFollowCount   = totalWallFollow  / count,
            APFActive            = apfActive,
            LeaderFollowerActive = leaderFollowerActive,
            Timedout             = timedout,
            Notes                = notes
        };

        records.Add(record);

        Debug.Log($"[DataExporter] Trial {trialCounter} recorded | Success: {record.SuccessRate:F1}% | Detections: {totalDetections} | Reassignments: {totalReassignments}");
    }

    // ─────────────────────────────────────────────────────────────
    // EXPORT TO CSV
    // ─────────────────────────────────────────────────────────────

    public void ExportCSV()
    {
        if (records.Count == 0)
        {
            Debug.LogWarning("[DataExporter] No records to export");
            return;
        }

        string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string filename  = $"Sim1_APF_LeaderFollower_{timestamp}.csv";
        string desktop   = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        string path      = Path.Combine(desktop, filename);

        StringBuilder sb = new StringBuilder();

        // Header
        sb.AppendLine(
            "Trial,DroneCount,MissionTime(s),TotalDetections,TotalReassignments," +
            "SuccessRate(%),AvgBattery(%),AvgPathLength(m),AvgStuckEvents,AvgWallFollowCount," +
            "APFActive,LeaderFollowerActive,Timeout,Notes"
        );

        // Rows
        foreach (TrialRecord r in records)
        {
            sb.AppendLine(
                $"{r.TrialNumber}," +
                $"{r.DroneCount}," +
                $"{r.MissionTimeSeconds:F2}," +
                $"{r.TotalDetections}," +
                $"{r.TotalReassignments}," +
                $"{r.SuccessRate:F1}," +
                $"{r.AvgBatteryRemaining:F1}," +
                $"{r.AvgPathLength:F2}," +
                $"{r.AvgStuckEvents:F2}," +
                $"{r.AvgWallFollowCount:F2}," +
                $"{r.APFActive}," +
                $"{r.LeaderFollowerActive}," +
                $"{r.Timedout}," +
                $"\"{r.Notes.Replace("\"", "\"\"")}\""
            );
        }

        File.WriteAllText(path, sb.ToString());
        Debug.Log($"[DataExporter] Exported {records.Count} trials → {path}");
    }
}
