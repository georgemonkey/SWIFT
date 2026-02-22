using UnityEngine;
using Unity.Mathematics;
using CesiumForUnity;

public class GeofenceRenderer : MonoBehaviour
{
    private double3[] geoCorners;
    private CesiumGeoreference georeference;
    private LineRenderer lineRenderer;
    private Color outlineColor;

    private const double FLAT_ALTITUDE = 150.0;

    public void Initialize(double3[] corners, CesiumGeoreference geo, Color color)
    {
        geoCorners = corners;
        georeference = geo;
        outlineColor = color;

        lineRenderer = gameObject.AddComponent<LineRenderer>();
        lineRenderer.loop = false;
        lineRenderer.positionCount = 5;
        lineRenderer.widthMultiplier = 3f;
        lineRenderer.useWorldSpace = true;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                     ?? Shader.Find("Unlit/Color");

        Material mat = new Material(shader);
        mat.color = outlineColor;
        lineRenderer.material = mat;
        lineRenderer.startColor = outlineColor;
        lineRenderer.endColor = outlineColor;
    }

    void Update()
    {
        if (georeference == null || geoCorners == null || lineRenderer == null)
            return;

        Vector3[] worldPoints = new Vector3[4];
        for (int i = 0; i < 4; i++)
        {
            double3 ecef = CesiumWgs84Ellipsoid
                .LongitudeLatitudeHeightToEarthCenteredEarthFixed(
                    new double3(geoCorners[i].x, geoCorners[i].y, FLAT_ALTITUDE));
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