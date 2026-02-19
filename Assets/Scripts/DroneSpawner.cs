using UnityEngine;
using Unity.Mathematics;
using CesiumForUnity;
using System.Collections.Generic;

public class DroneSpawner : MonoBehaviour
{
    [Header("Drone Settings")]
    public GameObject dronePrefab;
    public float droneAltitudeOffset = 5f;

    private List<GameObject> spawnedDrones = new List<GameObject>();
    private CesiumGeoreference georeference;

    public void SpawnDrones(List<GeofenceSectorManager.Sector> sectors, CesiumGeoreference georef)
    {
        georeference = georef;

        // Clear old drones
        foreach (var d in spawnedDrones)
            Destroy(d);
        spawnedDrones.Clear();

        float baseAltitude = 50f;

        for (int i = 0; i < sectors.Count; i++)
        {
            var sector = sectors[i];

            double startLng = (sector.minLng + sector.maxLng) / 2.0;
            double startLat = sector.minLat;
            double altitude = baseAltitude + i * droneAltitudeOffset;

            double3 ecef = CesiumWgs84Ellipsoid.LongitudeLatitudeHeightToEarthCenteredEarthFixed(
                new double3(startLng, startLat, altitude)
            );
            double3 unityPos = georeference.TransformEarthCenteredEarthFixedPositionToUnity(ecef);
            Vector3 spawnPos = new Vector3((float)unityPos.x, (float)unityPos.y, (float)unityPos.z);

            if (dronePrefab != null)
            {
                GameObject drone = Instantiate(dronePrefab, spawnPos, Quaternion.identity);
                drone.name = $"Drone_{i + 1}";
                spawnedDrones.Add(drone);
                Debug.Log($"Spawned Drone {i + 1} at Lat={startLat:F5}, Lng={startLng:F5}, Alt={altitude}m");
            }
            else
            {
                // No prefab yet — spawn a placeholder sphere
                GameObject placeholder = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                placeholder.name = $"Drone_{i + 1}_Placeholder";
                placeholder.transform.position = spawnPos;
                placeholder.transform.localScale = Vector3.one * 2f;

                Renderer rend = placeholder.GetComponent<Renderer>();
                rend.material.color = sector.color;

                spawnedDrones.Add(placeholder);
                Debug.Log($"Spawned placeholder Drone {i + 1} at Lat={startLat:F5}, Lng={startLng:F5}, Alt={altitude}m");
            }
        }
    }

    public List<GameObject> GetDrones() => spawnedDrones;
}