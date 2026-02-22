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

    [Header("Takeoff Settings")]
    public float takeoffInterval = 3f;

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

        var allControllers = new List<DroneController>();
        DroneController leader = null;

        for (int i = 0; i < sectors.Count; i++)
        {
            var sector = sectors[i];

            double startLng = i < stagingPositions.Count
                ? stagingPositions[i].lng
                : (sector.minLng + sector.maxLng) / 2.0;
            double startLat = i < stagingPositions.Count
                ? stagingPositions[i].lat
                : sector.minLat;

            double3 ecef = CesiumWgs84Ellipsoid
                .LongitudeLatitudeHeightToEarthCenteredEarthFixed(
                    new double3(startLng, startLat, droneAltitude));
            double3 unityPos = georeference
                .TransformEarthCenteredEarthFixedPositionToUnity(ecef);
            Vector3 spawnPos = new Vector3(
                (float)unityPos.x, (float)unityPos.y, (float)unityPos.z);

            GameObject drone = dronePrefab != null
                ? Instantiate(dronePrefab, spawnPos, Quaternion.identity)
                : CreatePlaceholder(spawnPos, sector.color);

            drone.name = $"Drone_{i + 1}";
            drone.SetActive(false);

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

            allControllers.Add(controller);
            if (controller.droneId == 1) leader = controller;

            spawnedDrones.Add(drone);
        }

        // Wire up leader-follower
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
        for (int i = 0; i < drones.Count; i++)
        {
            drones[i].SetActive(true);

            DroneController controller =
                drones[i].GetComponent<DroneController>();
            if (controller != null)
                controller.StartMission();

            Debug.Log($"Drone {i + 1} launched.");
            yield return new WaitForSeconds(takeoffInterval);
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