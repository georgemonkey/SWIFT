using UnityEngine;
using Unity.Mathematics;
using CesiumForUnity;
using System.Collections.Generic;

public class LidarSimulator : MonoBehaviour
{
    [Header("LIDAR Settings")]
    public float lidarRange = 11f;
    public int rayCount = 36;
    public LayerMask obstacleLayer;
    public bool visualizeRays = true;
    public Color rayColor = new Color(1f, 0f, 0f, 0.3f);

    [Header("Detection")]
    public bool targetDetected = false;
    public Vector3 lastDetectionPosition;

    private CesiumGeoreference georeference;
    private List<GameObject> detectedObjects = new List<GameObject>();

    public void Initialize(CesiumGeoreference geo)
    {
        georeference = geo;
    }

    public void Scan(double droneLng, double droneLat)
    {
        targetDetected = false;

        for (int i = 0; i < rayCount; i++)
        {
            float angle = (360f / rayCount) * i;
            Vector3 direction = Quaternion.Euler(0, angle, 0) * transform.forward;

            if (Physics.Raycast(transform.position, direction,
                out RaycastHit hit, lidarRange, obstacleLayer))
            {
                targetDetected = true;
                lastDetectionPosition = hit.point;

                if (!detectedObjects.Contains(hit.collider.gameObject))
                {
                    detectedObjects.Add(hit.collider.gameObject);
                    OnObjectDetected(hit.collider.gameObject,
                        hit.point, droneLng, droneLat);
                }

                if (visualizeRays)
                    Debug.DrawRay(transform.position,
                        direction * hit.distance, Color.red, 0.1f);
            }
            else
            {
                if (visualizeRays)
                    Debug.DrawRay(transform.position,
                        direction * lidarRange, rayColor, 0.1f);
            }
        }
    }

    void OnObjectDetected(GameObject obj, Vector3 worldPos,
        double droneLng, double droneLat)
    {
        Debug.Log($"LIDAR Detection at Lat={droneLat:F5}, " +
            $"Lng={droneLng:F5}: {obj.name}");

        if (georeference != null)
        {
            double3 ecef = georeference
                .TransformUnityPositionToEarthCenteredEarthFixed(
                    new double3(worldPos.x, worldPos.y, worldPos.z));
            double3 llh = CesiumWgs84Ellipsoid
                .EarthCenteredEarthFixedToLongitudeLatitudeHeight(ecef);

            Debug.Log($"Detection geo: Lat={llh.y:F6}, " +
                $"Lng={llh.x:F6}, Alt={llh.z:F1}m");
        }
    }

    public List<GameObject> GetDetectedObjects() => detectedObjects;
}