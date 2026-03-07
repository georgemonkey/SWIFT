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

        // Area in square feet
        public float AreaSqFt()
        {
            double latDiff = maxLat - minLat;
            double lngDiff = maxLng - minLng;
            double latM = latDiff * 111320.0;
            double lngM = lngDiff * 111320.0
                * System.Math.Cos((minLat + maxLat) / 2.0
                    * System.Math.PI / 180.0);
            double sqM = latM * lngM;
            return (float)(sqM * 10.7639); // m² to ft²
        }
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

        // Compute total area for stats
        float totalSqFt = 0f;
        foreach (var s in sectors) totalSqFt += s.AreaSqFt();
        Variables.totalAreaCoveredSqFt = totalSqFt;

        Debug.Log($"Created {sectors.Count} sectors ({cols}x{rows}). " +
            $"Total area: {totalSqFt:F0} sq ft");

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

        double stagingWidth = maxLng - minLng;
        double stagingHeight = maxLat - minLat;

        // Grid layout: evenly space drones inside staging zone
        GetBestGrid(n, stagingWidth, stagingHeight,
            out int cols, out int rows);

        double cellW = stagingWidth / cols;
        double cellH = stagingHeight / rows;

        Debug.Log($"Staging grid: {cols}x{rows}, " +
            $"cellW={cellW:F6}, cellH={cellH:F6}");

        int id = 0;
        for (int r = 0; r < rows && id < n; r++)
        {
            for (int c = 0; c < cols && id < n; c++)
            {
                // Center of each cell
                double lng = minLng + (c + 0.5) * cellW;
                double lat = minLat + (r + 0.5) * cellH;
                positions.Add((lng, lat));
                Debug.Log($"Staging pos {id}: Lat={lat:F6}, Lng={lng:F6}");
                id++;
            }
        }

        return positions;
    }

    void VisualizeSector(Sector sector)
    {
        GameObject anchorRoot = new GameObject($"Sector_{sector.droneId}_Root");

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