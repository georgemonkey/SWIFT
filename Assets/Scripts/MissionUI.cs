using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class MissionUI : MonoBehaviour
{
    [Header("References")]
    public GeofenceDrawer geofenceDrawer;
    public DroneSpawner droneSpawner;

    [Header("UI Elements")]
    public Button resetButton;
    public TMP_Text statusText;
    public TMP_Text droneStatusText;

    void Start()
    {
        if (resetButton != null)
            resetButton.onClick.AddListener(OnReset);

        SetStatus("Step 1: Draw search geofence. " +
            "Step 2: Draw staging zone.");

        Debug.Log($"Mission loaded — " +
            $"Drones: {Variables.droneCount}, " +
            $"Active: {Variables.activeDroneCount}, " +
            $"Pattern: {Variables.searchPattern}, " +
            $"Aggression: {Variables.aggression}, " +
            $"RTH: {Variables.rth}%, " +
            $"Separation: {Variables.seperationDistance}, " +
            $"Alt Rate: {Variables.altitudeDelateRate}");
    }

    void Update()
    {
        UpdateDroneStatus();
    }

    void UpdateDroneStatus()
    {
        if (droneSpawner == null || droneStatusText == null) return;

        var drones = droneSpawner.GetDrones();
        if (drones == null || drones.Count == 0) return;

        var sb = new System.Text.StringBuilder();
        foreach (var drone in drones)
        {
            if (drone == null) continue;
            DroneController dc = drone.GetComponent<DroneController>();
            if (dc != null)
            {
                sb.AppendLine(
                    $"Drone {dc.droneId} " +
                    $"[{(dc.isLeader ? "LEADER" : "FOLLOWER")}] " +
                    $"Coverage: {dc.coveragePercent:F1}% " +
                    $"Battery: {dc.batteryLevel:F0}% " +
                    $"{(dc.missionComplete ? "DONE" : "ACTIVE")}");
            }
        }
        droneStatusText.text = sb.ToString();
    }

    void OnReset()
    {
        geofenceDrawer?.ResetAll();
        var drones = droneSpawner?.GetDrones();
        if (drones != null)
            foreach (var d in drones)
                if (d != null) Destroy(d);

        SetStatus("Reset. Draw geofence again.");
    }

    void SetStatus(string msg)
    {
        if (statusText != null) statusText.text = msg;
        Debug.Log(msg);
    }
}