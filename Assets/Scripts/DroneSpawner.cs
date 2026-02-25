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

    [Header("Trail Settings")]
    public float trailTime = 8f;
    public float trailWidth = 0.5f;

    [Header("Cesium Root")]
    public Transform cesiumRoot;

    private List<GameObject> spawnedDrones = new List<GameObject>();
    private CesiumGeoreference georeference;

    public void SpawnDrones(
        List<GeofenceSectorManager.Sector> sectors,
        List<(double lng, double lat)> stagingPositions,
        CesiumGeoreference georef)
    {
        georeference = georef;

        foreach (var d in spawnedDrones) Destroy(d);
        spawnedDrones.Clear();

        Variables.missionElapsedTime = 0f;
        Variables.totalAreaCoveredSqFt = 0f;
        Variables.totalDetections = 0;
        Variables.totalWaypointsCompleted = 0;

        var allControllers = new List<DroneController>();
        DroneController leader = null;

        int spawnCount = Mathf.Min(Variables.activeDroneCount, sectors.Count);

        for (int i = 0; i < spawnCount; i++)
        {
            var sector = sectors[i];

            double startLng, startLat;
            if (i < stagingPositions.Count)
            {
                startLng = stagingPositions[i].lng;
                startLat = stagingPositions[i].lat;
            }
            else
            {
                startLng = (sector.minLng + sector.maxLng) / 2.0;
                startLat = sector.minLat;
                Debug.LogWarning($"Drone {i + 1} using fallback position.");
            }

            double3 ecef = CesiumWgs84Ellipsoid
                .LongitudeLatitudeHeightToEarthCenteredEarthFixed(
                    new double3(startLng, startLat, droneAltitude));
            double3 unityPos = georeference
                .TransformEarthCenteredEarthFixedPositionToUnity(ecef);
            Vector3 spawnPos = new Vector3(
                (float)unityPos.x, (float)unityPos.y, (float)unityPos.z);

            GameObject drone = dronePrefab != null
                ? Instantiate(dronePrefab, spawnPos, Quaternion.identity)
                : CreateDronePlaceholder(spawnPos, sector.color);

            drone.name = $"Drone_{i + 1}";
            drone.SetActive(true);

            if (cesiumRoot != null)
                drone.transform.SetParent(cesiumRoot);

            // Anchor
            DroneAnchor anchor = drone.AddComponent<DroneAnchor>();
            anchor.Initialize(startLng, startLat, droneAltitude, georeference);

            // Controller
            DroneController controller = drone.GetComponent<DroneController>()
                ?? drone.AddComponent<DroneController>();
            controller.Initialize(sector, georeference, i + 1);

            // LIDAR
            LidarSimulator lidar = drone.GetComponent<LidarSimulator>()
                ?? drone.AddComponent<LidarSimulator>();
            lidar.Initialize(georeference);

            // Trail — color matches sector
            DroneTrail trail = drone.AddComponent<DroneTrail>();
            trail.trailColor = sector.color;
            trail.trailTime = trailTime;
            trail.trailWidth = trailWidth;

            allControllers.Add(controller);
            if (controller.droneId == 1) leader = controller;

            spawnedDrones.Add(drone);

            Debug.Log($"Drone {i + 1} placed at staging: " +
                $"Lat={startLat:F6}, Lng={startLng:F6}");
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
        float interval;
        switch (Variables.aggression.ToLower().Trim())
        {
            case "aggressive": interval = 1f; break;
            case "moderate": interval = 2f; break;
            case "gentle": interval = 4f; break;
            default: interval = 3f; break;
        }

        for (int i = 0; i < drones.Count; i++)
        {
            DroneController controller =
                drones[i].GetComponent<DroneController>();
            if (controller != null)
                controller.StartMission();

            Debug.Log($"Drone {i + 1} launched. Next in {interval}s.");
            yield return new WaitForSeconds(interval);
        }
    }

    GameObject CreateDronePlaceholder(Vector3 pos, Color color)
    {
        GameObject drone = new GameObject("DronePlaceholder");
        drone.transform.position = pos;

        // Body — 6 inches = 0.1524m
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.transform.SetParent(drone.transform);
        body.transform.localPosition = Vector3.zero;
        body.transform.localScale = new Vector3(
            Variables.DRONE_SIZE_M,
            Variables.DRONE_SIZE_M * 0.2f,
            Variables.DRONE_SIZE_M);

        Renderer rend = body.GetComponent<Renderer>();
        Material mat = new Material(
            Shader.Find("Universal Render Pipeline/Lit") ??
            Shader.Find("Standard"));
        mat.color = color;
        rend.material = mat;

        // 4 arms
        for (int i = 0; i < 4; i++)
        {
            float angle = i * 90f;
            GameObject arm = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            arm.transform.SetParent(drone.transform);
            arm.transform.localScale = new Vector3(0.01f, 0.06f, 0.01f);
            arm.transform.localRotation = Quaternion.Euler(0f, angle, 90f);
            arm.transform.localPosition = new Vector3(
                Mathf.Cos(angle * Mathf.Deg2Rad) * Variables.DRONE_SIZE_M * 0.4f,
                0f,
                Mathf.Sin(angle * Mathf.Deg2Rad) * Variables.DRONE_SIZE_M * 0.4f);

            Renderer armRend = arm.GetComponent<Renderer>();
            Material armMat = new Material(
                Shader.Find("Universal Render Pipeline/Lit") ??
                Shader.Find("Standard"));
            armMat.color = Color.gray;
            armRend.material = armMat;
        }

        return drone;
    }

    public List<GameObject> GetDrones() => spawnedDrones;
}