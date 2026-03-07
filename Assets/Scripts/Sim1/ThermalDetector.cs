using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Simulates a thermal imaging sensor.
/// Detection radius derived from real sensor physics:
///   detectionRadius = altitude * tan(FOV/2)
///   = 10m * tan(27.5deg) = 5.2m
///
/// Scans for GameObjects tagged "Survivor" within radius each frame.
/// Reports detections to DroneAgent for data export.
/// </summary>
public class ThermalDetector : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────
    // PUBLIC STATE
    // ─────────────────────────────────────────────────────────────

    public int   TotalDetections   { get; private set; }
    public float DetectionRadius   => Sim1Variables.ThermalDetectionRadius;

    // ─────────────────────────────────────────────────────────────
    // PRIVATE
    // ─────────────────────────────────────────────────────────────

    private HashSet<GameObject> detectedSurvivors = new HashSet<GameObject>();

    // ─────────────────────────────────────────────────────────────
    // SCAN — called by DroneAgent each FixedUpdate
    // ─────────────────────────────────────────────────────────────

    public bool Scan()
    {
        float   radius    = Sim1Variables.ThermalDetectionRadius;
        Collider[] hits   = Physics.OverlapSphere(transform.position, radius);
        bool    newDetect = false;

        foreach (Collider col in hits)
        {
            if (!col.CompareTag("Survivor")) continue;
            if (detectedSurvivors.Contains(col.gameObject)) continue;

            detectedSurvivors.Add(col.gameObject);
            TotalDetections++;
            newDetect = true;

            Debug.Log($"[Thermal] SURVIVOR DETECTED: {col.gameObject.name} | Total: {TotalDetections}");
            Debug.DrawLine(transform.position, col.transform.position, Color.magenta, 2f);
        }

        return newDetect;
    }

    public void ResetDetections()
    {
        detectedSurvivors.Clear();
        TotalDetections = 0;
    }

    // ─────────────────────────────────────────────────────────────
    // GIZMOS
    // ─────────────────────────────────────────────────────────────

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.15f);
        Gizmos.DrawWireSphere(transform.position, Sim1Variables.ThermalDetectionRadius);
    }
}
