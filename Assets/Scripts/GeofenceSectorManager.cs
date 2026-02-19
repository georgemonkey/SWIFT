using UnityEngine;
using Unity.Mathematics;
using CesiumForUnity;
using System.Collections.Generic;

public class GeofenceSectorManager : MonoBehaviour
{
    [Header("References")]
    public CesiumGeoreference georeference;

    [Header("Settings")]
    [Range(1, 16)]
    public int numberOfDrones = 4;

    public Color[] sectorColors = new Color[]
    {
        Color.yellow,
        Color.green,
        Color.magenta,
        Color.cyan,
        Color.red,
        Color.blue,
        Color.white,
        Color.gray
    };

    public struct Sector
    {
        public double minLat, maxLat;
        public double minLng, maxLng;
        public int droneId;
        public Color color;
    }

    private List<Sector> sectors = new List<Sector>();  
    public DroneSpawner droneSpawner;
    private List<GameObject> sectorVisuals = new List<GameObject>();

    void Awake()
    {
        droneSpawner = GetComponent<DroneSpawner>();

        if (droneSpawner == null)
            Debug.LogError("DroneSpawner not found on this GameObject!");

        if (georeference == null)
            Debug.LogError("CesiumGeoreference not assigned!");
    }

    public void SetGeofenceAndSplit(double3[] geoCorners)
    {
        double minLat = double.MaxValue, maxLat = double.MinValue;
        double minLng = double.MaxValue, maxLng = double.MinValue;

        foreach (var c in geoCorners)
        {
            if (c.y < minLat) minLat = c.y;
            if (c.y > maxLat) maxLat = c.y;
            if (c.x < minLng) minLng = c.x;
            if (c.x > maxLng) maxLng = c.x;
        }

        Debug.Log($"Geofence bounds: Lat {minLat:F6} to {maxLat:F6}, Lng {minLng:F6} to {maxLng:F6}");
        SplitIntoSectors(minLat, maxLat, minLng, maxLng, numberOfDrones);
    }

    void SplitIntoSectors(double minLat, double maxLat, double minLng, double maxLng, int n)
    {
        sectors.Clear();
        foreach (var v in sectorVisuals) Destroy(v);
        sectorVisuals.Clear();

        double latRange = maxLat - minLat;
        double lngRange = maxLng - minLng;

        GetBestGrid(n, lngRange, latRange, out int cols, out int rows);

        double sectorLat = latRange / rows;
        double sectorLng = lngRange / cols;

        int id = 0;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                if (id >= n) break;

                Sector sector = new Sector
                {
                    minLat = minLat + r * sectorLat,
                    maxLat = minLat + (r + 1) * sectorLat,
                    minLng = minLng + c * sectorLng,
                    maxLng = minLng + (c + 1) * sectorLng,
                    droneId = id,
                    color = sectorColors[id % sectorColors.Length]
                };

                sectors.Add(sector);
                VisualizeSector(sector);
                id++;
            }
        }

        Debug.Log($"Created {sectors.Count} sectors in a {cols}x{rows} grid.");
        droneSpawner.SpawnDrones(sectors, georeference);
    }

    void GetBestGrid(int n, double lngRange, double latRange, out int cols, out int rows)
    {
        cols = 1;
        rows = n;
        float bestScore = float.MaxValue;

        for (int c = 1; c <= n; c++)
        {
            if (n % c != 0) continue;
            int r = n / c;

            float cellAspect = (float)((lngRange / c) / (latRange / r));
            float score = Mathf.Abs(1f - cellAspect);

            if (score < bestScore)
            {
                bestScore = score;
                cols = c;
                rows = r;
            }
        }
    }

    void VisualizeSector(Sector sector)
    {
        double3[] geoPoints = new double3[]
        {
        new double3(sector.minLng, sector.minLat, 200),
        new double3(sector.maxLng, sector.minLat, 200),
        new double3(sector.maxLng, sector.maxLat, 200),
        new double3(sector.minLng, sector.maxLat, 200),
        };

        Vector3[] worldPoints = new Vector3[4];
        for (int i = 0; i < 4; i++)
        {
            double3 ecef = CesiumWgs84Ellipsoid
                .LongitudeLatitudeHeightToEarthCenteredEarthFixed(geoPoints[i]);
            double3 unity = georeference
                .TransformEarthCenteredEarthFixedPositionToUnity(ecef);
            worldPoints[i] = new Vector3((float)unity.x, (float)unity.y, (float)unity.z);
            Debug.Log($"Sector {sector.droneId} world point {i}: {worldPoints[i]}");
        }

        // Use a 5 point loop (close the rectangle)
        Vector3[] loopPoints = new Vector3[]
        {
        worldPoints[0],
        worldPoints[1],
        worldPoints[2],
        worldPoints[3],
        worldPoints[0], // close the loop manually
        };

        GameObject outlineObj = new GameObject($"Sector_{sector.droneId}_Outline");
        LineRenderer lr = outlineObj.AddComponent<LineRenderer>();
        lr.loop = false;
        lr.positionCount = 5;
        lr.SetPositions(loopPoints);
        lr.widthMultiplier = 2f;
        lr.useWorldSpace = true;

        // Try every possible shader
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Unlit/Color")
                     ?? Shader.Find("GUI/Text Shader")
                     ?? Shader.Find("Sprites/Default");

        if (shader == null)
        {
            Debug.LogError("No valid shader found! Check your render pipeline.");
            return;
        }

        Material mat = new Material(shader);
        mat.color = sector.color;
        lr.material = mat;
        lr.startColor = sector.color;
        lr.endColor = sector.color;

        sectorVisuals.Add(outlineObj);
    }

    public List<Sector> GetSectors() => sectors;
}