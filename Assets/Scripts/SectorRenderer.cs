using UnityEngine;
using Unity.Mathematics;
using CesiumForUnity;

public class SectorRenderer : MonoBehaviour
{
    private GeofenceSectorManager.Sector sector;
    private CesiumGeoreference georeference;
    private LineRenderer lineRenderer;

    private const double FLAT_ALTITUDE = 150.0;

    public void Initialize(GeofenceSectorManager.Sector s,
        CesiumGeoreference geo, CesiumGlobeAnchor a)
    {
        sector = s;
        georeference = geo;

        lineRenderer = gameObject.AddComponent<LineRenderer>();
        lineRenderer.loop = false;
        lineRenderer.positionCount = 5;
        lineRenderer.widthMultiplier = 2f;
        lineRenderer.useWorldSpace = true;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Unlit/Color");

        Material mat = new Material(shader);
        mat.color = sector.color;
        lineRenderer.material = mat;
        lineRenderer.startColor = sector.color;
        lineRenderer.endColor = sector.color;
    }

    void Update()
    {
        if (georeference == null || lineRenderer == null) return;

        double[,] corners = new double[,]
        {
            { sector.minLng, sector.minLat },
            { sector.maxLng, sector.minLat },
            { sector.maxLng, sector.maxLat },
            { sector.minLng, sector.maxLat },
        };

        Vector3[] worldPoints = new Vector3[4];
        for (int i = 0; i < 4; i++)
        {
            double3 ecef = CesiumWgs84Ellipsoid
                .LongitudeLatitudeHeightToEarthCenteredEarthFixed(
                    new double3(corners[i, 0], corners[i, 1], FLAT_ALTITUDE));
            double3 unity = georeference
                .TransformEarthCenteredEarthFixedPositionToUnity(ecef);

            worldPoints[i] = new Vector3(
                (float)unity.x, (float)unity.y, (float)unity.z);
        }

        lineRenderer.SetPositions(new Vector3[]
        {
            worldPoints[0], worldPoints[1],
            worldPoints[2], worldPoints[3],
            worldPoints[0],
        });
    }
}