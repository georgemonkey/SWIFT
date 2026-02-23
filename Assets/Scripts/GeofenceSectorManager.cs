using UnityEngine;
using Unity.Mathematics;
using CesiumForUnity;
using System.Collections.Generic;

public class GeofenceSectorManager : MonoBehaviour
{
    [Header("References")]
    public CesiumGeoreference georeference;
    public DroneSpawner droneSpawner;
    public Transform cesiumRoot;

    public Color[] sectorColors = new Color[]
    {
        Color.yellow, Color.green, Color.magenta, Color.cyan,
        Color.red, Color.blue, Color.white, Color.gray,
        new Color(1f,0.5f,0f), new Color(0.5f,0f,1f),
        new Color(0f,1f,0.5f), new Color(1f,0f,0.5f),
        new Color(0.5f,1f,0f), new Color(0f,0.5f,1f),
        new Color(1f,1f,0f), new Color(0f,1f,1f)
    };

    public struct Sector
    {
        public double minLat, maxLat;
        public double minLng, maxLng;
        public int droneId;
        public Color color;
    }

    private List<Sector> sectors = new List<Sector>();
    private List<GameObject> sectorVisuals = new List<GameObject>();
    private double3[] stagingGeoCorners;

    public void SetGeofenceAndSplit(double3[] geoCorners, double3[] stagingCorners)
    {
        stagingGeoCorners = stagingCorners;

        double minLat = double.MaxValue, maxLat = double.MinValue;
        double minLng = double.MaxValue, maxLng = double.MinValue;

        foreach (var c in geoCorners)
        {
            if (c.y < minLat) minLat = c.y;
            if (c.y > maxLat) maxLat = c.y;
            if (c.x < minLng) minLng = c.x;
            if (c.x > maxLng) maxLng = c.x;
        }

        SplitIntoSectors(minLat, maxLat, minLng, maxLng, Variables.droneCount);
    }

    void SplitIntoSectors(double minLat, double maxLat,
        double minLng, double maxLng, int n)
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

        Debug.Log($"Created {sectors.Count} sectors ({cols}x{rows}) " +
            $"for {n} drones.");

        if (droneSpawner == null)
        {
            Debug.LogError("DroneSpawner not assigned!");
            return;
        }

        List<(double lng, double lat)> stagingPositions =
            ComputeStagingPositions(n);

        droneSpawner.SpawnDrones(sectors, stagingPositions, georeference);
    }

    void GetBestGrid(int n, double lngRange, double latRange,
        out int cols, out int rows)
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

    List<(double lng, double lat)> ComputeStagingPositions(int n)
    {
        var positions = new List<(double, double)>();

        if (stagingGeoCorners == null || stagingGeoCorners.Length < 4)
        {
            Debug.LogError("Staging corners not set!");
            return positions;
        }

        double minLat = double.MaxValue, maxLat = double.MinValue;
        double minLng = double.MaxValue, maxLng = double.MinValue;

        foreach (var c in stagingGeoCorners)
        {
            if (c.y < minLat) minLat = c.y;
            if (c.y > maxLat) maxLat = c.y;
            if (c.x < minLng) minLng = c.x;
            if (c.x > maxLng) maxLng = c.x;
        }

        // Convert meters to degrees
        double increment = Variables.seperationDistance / 111320.0;

        double stagingWidth = maxLng - minLng;
        double stagingHeight = maxLat - minLat;

        // Safety clamp if increment too large or invalid
        if (increment <= 0 || increment > stagingWidth / 2.0)
        {
            Debug.LogWarning($"Separation distance {Variables.seperationDistance}m " +
                $"too large or invalid. Using auto spacing.");
            increment = System.Math.Min(stagingWidth, stagingHeight) /
                (System.Math.Sqrt(n) + 1);
        }

        Debug.Log($"Staging bounds: Lat {minLat:F6} to {maxLat:F6}, " +
            $"Lng {minLng:F6} to {maxLng:F6}");
        Debug.Log($"Computed increment: {increment:F8} degrees " +
            $"({Variables.seperationDistance}m)");

        double startLng = minLng + increment;
        double startLat = minLat + increment;

        int cols = Mathf.Max(1,
            Mathf.FloorToInt((float)((stagingWidth - increment) / increment)));

        Debug.Log($"Staging cols: {cols}");

        for (int i = 0; i < n; i++)
        {
            int col = i % cols;
            int row = i / cols;

            double lng = startLng + col * increment;
            double lat = startLat + row * increment;

            lng = System.Math.Min(lng, maxLng - increment * 0.5);
            lat = System.Math.Min(lat, maxLat - increment * 0.5);

            positions.Add((lng, lat));
            Debug.Log($"Staging position {i}: Lat={lat:F6}, Lng={lng:F6}");
        }

        return positions;
    }

    void VisualizeSector(Sector sector)
    {
        GameObject anchorRoot = new GameObject($"Sector_{sector.droneId}_Root");

        // Parent to CesiumGeoreference so anchor works correctly
        if (cesiumRoot != null)
            anchorRoot.transform.SetParent(cesiumRoot);

        CesiumGlobeAnchor anchor = anchorRoot.AddComponent<CesiumGlobeAnchor>();

        double centerLng = (sector.minLng + sector.maxLng) / 2.0;
        double centerLat = (sector.minLat + sector.maxLat) / 2.0;
        anchor.longitudeLatitudeHeight = new double3(centerLng, centerLat, 150);
        anchor.adjustOrientationForGlobeWhenMoving = true;

        SectorRenderer sr = anchorRoot.AddComponent<SectorRenderer>();
        sr.Initialize(sector, georeference, anchor);
        sectorVisuals.Add(anchorRoot);
    }

    public List<Sector> GetSectors() => sectors;
}