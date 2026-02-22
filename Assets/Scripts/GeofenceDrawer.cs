using UnityEngine;
using Unity.Mathematics;
using CesiumForUnity;
using System.Collections.Generic;

public class GeofenceDrawer : MonoBehaviour
{
    [Header("References")]
    public CesiumGeoreference georeference;
    public Camera mainCamera;
    public GeofenceSectorManager sectorManager;

    [Header("Visuals")]
    public Color geofenceColor = new Color(0f, 1f, 1f, 0.3f);
    public Color outlineColor = Color.cyan;
    public Color stagingColor = new Color(1f, 0.5f, 0f, 0.3f);
    public Color stagingOutlineColor = Color.yellow;

    [Header("Drawing Mode")]
    public bool drawingGeofence = true;

    private Vector3 worldCornerA;
    private Vector3 worldCornerB;
    private bool firstPointSet = false;
    private bool geofenceFinalized = false;

    private Vector3 stagingCornerA;
    private Vector3 stagingCornerB;
    private bool stagingFirstPointSet = false;
    private bool stagingFinalized = false;

    private GameObject geofenceQuad;
    private GameObject stagingQuad;
    private LineRenderer outlineRenderer;
    private LineRenderer stagingPreviewRenderer;

    public double3[] GeofenceGeoCorners { get; private set; }
    public double3[] StagingGeoCorners { get; private set; }

    void Update()
    {
        if (sectorManager == null)
            sectorManager = GetComponent<GeofenceSectorManager>();

        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                if (drawingGeofence && !geofenceFinalized)
                    HandleGeofenceClick(hit.point);
                else if (!drawingGeofence && !stagingFinalized)
                    HandleStagingClick(hit.point);
            }
            else
            {
                Debug.LogWarning("Raycast missed terrain.");
            }
        }

        // Live preview geofence
        if (firstPointSet && !geofenceFinalized && drawingGeofence)
        {
            Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
                DrawPreview(worldCornerA, hit.point,
                    ref outlineRenderer, "GeofencePreview", outlineColor);
        }

        // Live preview staging
        if (stagingFirstPointSet && !stagingFinalized && !drawingGeofence)
        {
            Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
                DrawPreview(stagingCornerA, hit.point,
                    ref stagingPreviewRenderer, "StagingPreview", stagingOutlineColor);
        }
    }

    void HandleGeofenceClick(Vector3 point)
    {
        if (!firstPointSet)
        {
            worldCornerA = point;
            firstPointSet = true;
            Debug.Log("Geofence first corner set.");
        }
        else
        {
            worldCornerB = point;
            FinalizeGeofence();
        }
    }

    void HandleStagingClick(Vector3 point)
    {
        if (!stagingFirstPointSet)
        {
            stagingCornerA = point;
            stagingFirstPointSet = true;
            Debug.Log("Staging zone first corner set.");
        }
        else
        {
            stagingCornerB = point;
            FinalizeStaging();
        }
    }

    void DrawPreview(Vector3 a, Vector3 b, ref LineRenderer lr,
        string objName, Color color)
    {
        Vector3[] corners = GetRectCorners(a, b);
        if (lr == null)
        {
            GameObject lineObj = new GameObject(objName);
            lr = lineObj.AddComponent<LineRenderer>();
            lr.loop = true;
            lr.positionCount = 4;
            lr.widthMultiplier = 0.5f;
            lr.material = new Material(GetShader());
            lr.startColor = color;
            lr.endColor = color;
            lr.useWorldSpace = true;
        }
        lr.SetPositions(corners);
    }

    void FinalizeGeofence()
    {
        geofenceFinalized = true;

        if (outlineRenderer != null)
            Destroy(outlineRenderer.gameObject);

        Vector3[] corners = GetRectCorners(worldCornerA, worldCornerB);
        GeofenceGeoCorners = ConvertToGeoCoordinates(corners);

        DrawZoneMesh(corners, GeofenceGeoCorners, ref geofenceQuad,
            "GeofenceMesh", outlineColor, geofenceColor, true);

        Debug.Log("Geofence finalized. Now draw the staging zone.");
        drawingGeofence = false;
    }

    void FinalizeStaging()
    {
        stagingFinalized = true;

        if (stagingPreviewRenderer != null)
            Destroy(stagingPreviewRenderer.gameObject);

        Vector3[] corners = GetRectCorners(stagingCornerA, stagingCornerB);
        StagingGeoCorners = ConvertToGeoCoordinates(corners);

        DrawZoneMesh(corners, StagingGeoCorners, ref stagingQuad,
            "StagingMesh", stagingOutlineColor, stagingColor, false);

        Debug.Log("Staging zone finalized. Starting mission planning.");

        if (sectorManager != null)
            sectorManager.SetGeofenceAndSplit(GeofenceGeoCorners, StagingGeoCorners);
        else
            Debug.LogError("SectorManager not found!");
    }

    void DrawZoneMesh(Vector3[] corners, double3[] geoCorners,
        ref GameObject quad, string name, Color outline,
        Color fill, bool isGeofence)
    {
        if (quad != null) Destroy(quad);

        double centerLng = 0, centerLat = 0;
        foreach (var c in geoCorners)
        {
            centerLng += c.x;
            centerLat += c.y;
        }
        centerLng /= 4.0;
        centerLat /= 4.0;

        quad = new GameObject(name);
        CesiumGlobeAnchor anchor = quad.AddComponent<CesiumGlobeAnchor>();
        anchor.longitudeLatitudeHeight = new double3(centerLng, centerLat, 5);
        anchor.adjustOrientationForGlobeWhenMoving = true;

        GeofenceRenderer gr = quad.AddComponent<GeofenceRenderer>();
        gr.Initialize(geoCorners, georeference, outline);
    }

    Vector3[] GetRectCorners(Vector3 a, Vector3 b)
    {
        float minX = Mathf.Min(a.x, b.x);
        float maxX = Mathf.Max(a.x, b.x);
        float minZ = Mathf.Min(a.z, b.z);
        float maxZ = Mathf.Max(a.z, b.z);
        float y = (a.y + b.y) / 2f + 0.5f;

        return new Vector3[]
        {
            new Vector3(minX, y, minZ),
            new Vector3(maxX, y, minZ),
            new Vector3(maxX, y, maxZ),
            new Vector3(minX, y, maxZ),
        };
    }

    double3[] ConvertToGeoCoordinates(Vector3[] corners)
    {
        double3[] result = new double3[corners.Length];
        for (int i = 0; i < corners.Length; i++)
        {
            double3 unityPos = new double3(
                corners[i].x, corners[i].y, corners[i].z);
            double3 ecef = georeference
                .TransformUnityPositionToEarthCenteredEarthFixed(unityPos);
            result[i] = CesiumWgs84Ellipsoid
                .EarthCenteredEarthFixedToLongitudeLatitudeHeight(ecef);
            Debug.Log($"Corner {i}: Lon={result[i].x:F6}, " +
                $"Lat={result[i].y:F6}");
        }
        return result;
    }

    Shader GetShader()
    {
        return Shader.Find("Universal Render Pipeline/Unlit")
            ?? Shader.Find("Unlit/Color")
            ?? Shader.Find("Sprites/Default");
    }

    public void ResetAll()
    {
        firstPointSet = false;
        geofenceFinalized = false;
        stagingFirstPointSet = false;
        stagingFinalized = false;
        drawingGeofence = true;

        if (geofenceQuad != null) Destroy(geofenceQuad);
        if (stagingQuad != null) Destroy(stagingQuad);
        if (outlineRenderer != null) Destroy(outlineRenderer.gameObject);
        if (stagingPreviewRenderer != null)
            Destroy(stagingPreviewRenderer.gameObject);

        foreach (var n in new[] {
            "GeofencePreview", "GeofenceMesh",
            "StagingPreview", "StagingMesh" })
        {
            GameObject obj = GameObject.Find(n);
            if (obj != null) Destroy(obj);
        }

        Debug.Log("Reset complete.");
    }
}