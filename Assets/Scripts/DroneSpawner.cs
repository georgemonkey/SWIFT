using UnityEngine;
using Unity.Mathematics;
using CesiumForUnity;
using System.Collections;
using System.Collections.Generic;

public class DroneSpawner : MonoBehaviour
{
    [Header("Drone Settings")]
    public GameObject dronePrefab;
    public double droneAltitude = 150.0;

    private List<GameObject> spawnedDrones = new List<GameObject>();
    private CesiumGeoreference georeference;

    float GetTakeoffInterval()
    {
        switch (Variables.aggression)
        {
            case "Aggressive": return 1f;
            case "Moderate": return 2f;
            case "Gentle": return 4f;
            default: return 3f;
        }
    }

    float GetMoveSpeed()
    {
        switch (Variables.aggression)
        {
            case "Aggressive": return 20f;
            case "Moderate": return 12f;
            case "Gentle": return 6f;
            default: return 10f;
        }
    }

    public void SpawnDrones(
        List<GeofenceSectorManager.Sector> sectors,
        List<(double lng, double lat)> stagingPositions,
        CesiumGeoreference georef)
    {
        georeference = georef;

        foreach (var d in spawnedDrones) Destroy(d);
        spawnedDrones.Clear();

        // Debug staging info
        Debug.Log($"Staging positions count: {stagingPositions.Count}");
        Debug.Log($"Sectors count: {sectors.Count}");
        Debug.Log($"Separation distance: {Variables.seperationDistance}");

        for (int i = 0; i < stagingPositions.Count; i++)
        {
            Debug.Log($"Staging pos {i}: " +
                $"Lat={stagingPositions[i].lat:F6}, " +
                $"Lng={stagingPositions[i].lng:F6}");
        }

        var allControllers = new List<DroneController>();
        DroneController leader = null;

        int spawnCount = Mathf.Min(Variables.activeDroneCount, sectors.Count);
        Debug.Log($"Spawning {spawnCount} drones. " +
            $"Aggression: {Variables.aggression}, " +
            $"Pattern: {Variables.searchPattern}, " +
            $"RTH at: {Variables.rth}%");

        for (int i = 0; i < spawnCount; i++)
        {
            var sector = sectors[i];

            double startLng, startLat;

            if (i < stagingPositions.Count)
            {
                startLng = stagingPositions[i].lng;
                startLat = stagingPositions[i].lat;
                Debug.Log($"Drone {i + 1} using staging position: " +
                    $"Lat={startLat:F6}, Lng={startLng:F6}");
            }
            else
            {
                // Fallback to sector center
                startLng = (sector.minLng + sector.maxLng) / 2.0;
                startLat = sector.minLat;
                Debug.LogWarning($"Drone {i + 1} falling back to sector position: " +
                    $"Lat={startLat:F6}, Lng={startLng:F6}");
            }

            double3 ecef = CesiumWgs84Ellipsoid
                .LongitudeLatitudeHeightToEarthCenteredEarthFixed(
                    new double3(startLng, startLat, droneAltitude));
            double3 unityPos = georeference
                .TransformEarthCenteredEarthFixedPositionToUnity(ecef);
            Vector3 spawnPos = new Vector3(
                (float)unityPos.x, (float)unityPos.y, (float)unityPos.z);

            Debug.Log($"Drone {i + 1} Unity spawn pos: {spawnPos}");

            GameObject drone = dronePrefab != null
                ? Instantiate(dronePrefab, spawnPos, Quaternion.identity)
                : CreatePlaceholder(spawnPos, sector.color);

            drone.name = $"Drone_{i + 1}";
            drone.SetActive(false);

            // Anchor — keeps drone fixed to geo position
            DroneAnchor anchor = drone.AddComponent<DroneAnchor>();
            anchor.Initialize(startLng, startLat, droneAltitude, georeference);

            // Controller
            DroneController controller = drone.GetComponent<DroneController>()
                ?? drone.AddComponent<DroneController>();
            controller.Initialize(sector, georeference, i + 1, GetMoveSpeed());

            // LIDAR
            LidarSimulator lidar = drone.GetComponent<LidarSimulator>()
                ?? drone.AddComponent<LidarSimulator>();
            lidar.Initialize(georeference);

            allControllers.Add(controller);
            if (controller.droneId == 1) leader = controller;

            spawnedDrones.Add(drone);
        }

        // Wire leader-follower
        if (leader != null)
        {
            foreach (var dc in allControllers)
                if (!dc.isLeader) leader.followers.Add(dc);

            Debug.Log($"Drone 1 is leader with " +
                $"{leader.followers.Count} followers.");
        }

        StartCoroutine(SequentialTakeoff(spawnedDrones));
    }

    IEnumerator SequentialTakeoff(List<GameObject> drones)
    {
        float interval = GetTakeoffInterval();

        for (int i = 0; i < drones.Count; i++)
        {
            drones[i].SetActive(true);

            DroneController controller =
                drones[i].GetComponent<DroneController>();
            if (controller != null)
                controller.StartMission();

            Debug.Log($"Drone {i + 1} launched. Next in {interval}s.");
            yield return new WaitForSeconds(interval);
        }
    }

    GameObject CreatePlaceholder(Vector3 pos, Color color)
    {
        GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        obj.transform.position = pos;
        obj.transform.localScale = Vector3.one * 2f;
        Renderer rend = obj.GetComponent<Renderer>();
        Material mat = new Material(
            Shader.Find("Universal Render Pipeline/Lit") ??
            Shader.Find("Standard"));
        mat.color = color;
        rend.material = mat;
        return obj;
    }

    public List<GameObject> GetDrones() => spawnedDrones;
}