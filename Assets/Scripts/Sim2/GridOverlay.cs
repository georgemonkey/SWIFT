using UnityEngine;
using Unity.Mathematics;
using CesiumForUnity;
using System.Collections;

public class GridOverlay : MonoBehaviour
{
    [Header("Grid Settings")]
    public float cellSizeDeg = 0.000081f;
    public float gridAltitude = 150f;

    [Header("Colors")]
    public Color unsearchedFill = new Color(1f, 1f, 1f, 0.03f);
    public Color unsearchedOutline = new Color(1f, 1f, 1f, 0.95f);
    public Color lawnmowerColor = new Color(0.2f, 0.6f, 1.0f, 0.55f);
    public Color spiralColor = new Color(1.0f, 0.5f, 0.1f, 0.55f);
    public Color expandingSquareColor = new Color(0.2f, 1.0f, 0.3f, 0.55f);
    public Color randomWalkColor = new Color(1.0f, 0.2f, 0.8f, 0.55f);

    [Header("References")]
    public CesiumGeoreference georeference;
    public Transform cesiumRoot;

    private GeofenceSectorManager sectorManager;

    private int lngCells, latCells;
    private float minLng, maxLng, minLat, maxLat;

    private MeshRenderer[,] fillRenderers;
    private bool[,] cellSearched;

    private Mesh fillMesh;
    private Mesh outlineMesh;
    private Color currentSearchedColor;
    private Transform gridParent;
    private bool initialized = false;

    void Awake()
    {
        sectorManager = GetComponent<GeofenceSectorManager>();
        if (georeference == null)
            georeference = FindObjectOfType<CesiumGeoreference>();
        CreateMeshes();
    }

    // ─────────────────────────────────────────────────────────────
    // INITIALIZE
    // ─────────────────────────────────────────────────────────────

    public void Initialize(string algorithm)
    {
        if (sectorManager == null)
        {
            Debug.LogError("[GridOverlay] GeofenceSectorManager not found");
            return;
        }

        minLng = (float)sectorManager.minLng;
        maxLng = (float)sectorManager.maxLng;
        minLat = (float)sectorManager.minLat;
        maxLat = (float)sectorManager.maxLat;

        SetAlgorithmColor(algorithm);

        lngCells = Mathf.Max(1, Mathf.RoundToInt((maxLng - minLng) / cellSizeDeg));
        latCells = Mathf.Max(1, Mathf.RoundToInt((maxLat - minLat) / cellSizeDeg));

        Debug.Log($"[GridOverlay] Grid: {lngCells}x{latCells} = {lngCells * latCells} cells | Alt: {gridAltitude}");

        ClearGrid();
        StartCoroutine(BuildGridNextFrame());
    }

    private IEnumerator BuildGridNextFrame()
    {
        yield return null;
        BuildGrid();
        initialized = true;
        Debug.Log("[GridOverlay] Grid built and visible");
    }

    // ─────────────────────────────────────────────────────────────
    // BUILD GRID
    // ─────────────────────────────────────────────────────────────

    private void BuildGrid()
    {
        GameObject parentObj = new GameObject("GridOverlay_Cells");
        if (cesiumRoot != null)
            parentObj.transform.SetParent(cesiumRoot);
        gridParent = parentObj.transform;

        fillRenderers = new MeshRenderer[lngCells, latCells];
        cellSearched = new bool[lngCells, latCells];

        // Standard shader configured for transparency
        Shader shader = Shader.Find("Standard");

        float centerLat = (minLat + maxLat) * 0.5f;
        float cellWidthM = cellSizeDeg * 111320f * Mathf.Cos(centerLat * Mathf.Deg2Rad);
        float cellHeightM = cellSizeDeg * 111320f;

        for (int x = 0; x < lngCells; x++)
        {
            for (int y = 0; y < latCells; y++)
            {
                double cellLng = minLng + (x + 0.5) * cellSizeDeg;
                double cellLat = minLat + (y + 0.5) * cellSizeDeg;

                // Anchor root
                GameObject anchorRoot = new GameObject($"Cell_{x}_{y}");
                anchorRoot.transform.SetParent(gridParent);

                CesiumGlobeAnchor anchor = anchorRoot.AddComponent<CesiumGlobeAnchor>();
                anchor.longitudeLatitudeHeight = new double3(cellLng, cellLat, gridAltitude);
                anchor.adjustOrientationForGlobeWhenMoving = true;

                // Fill quad
                GameObject fill = new GameObject("Fill");
                fill.transform.SetParent(anchorRoot.transform);
                fill.transform.localPosition = Vector3.zero;
                fill.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                fill.transform.localScale = new Vector3(cellWidthM, cellHeightM, 1f);

                MeshFilter fillMF = fill.AddComponent<MeshFilter>();
                MeshRenderer fillMR = fill.AddComponent<MeshRenderer>();
                fillMF.sharedMesh = fillMesh;

                Material fillMat = MakeTransparentMaterial(shader, unsearchedFill);
                fillMR.material = fillMat;
                fillMR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                fillMR.receiveShadows = false;

                // Outline quad — slightly larger scale so it shows behind fill
                GameObject outline = new GameObject("Outline");
                outline.transform.SetParent(anchorRoot.transform);
                outline.transform.localPosition = Vector3.zero;
                outline.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                outline.transform.localScale = new Vector3(cellWidthM, cellHeightM, 1f);

                MeshFilter outMF = outline.AddComponent<MeshFilter>();
                MeshRenderer outMR = outline.AddComponent<MeshRenderer>();
                outMF.sharedMesh = outlineMesh;

                Material outMat = MakeTransparentMaterial(shader, unsearchedOutline);
                outMat.renderQueue = 2999; // render behind fill
                outMR.material = outMat;
                outMR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                outMR.receiveShadows = false;

                fillRenderers[x, y] = fillMR;
                cellSearched[x, y] = false;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────
    // MATERIAL HELPER
    // ─────────────────────────────────────────────────────────────

    private Material MakeTransparentMaterial(Shader shader, Color color)
    {
        Material mat = new Material(shader);
        mat.SetFloat("_Mode", 3); // Transparent
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = 3000;
        mat.color = color;
        return mat;
    }

    // ─────────────────────────────────────────────────────────────
    // MARK CELL SEARCHED
    // ─────────────────────────────────────────────────────────────

    public void MarkCellSearched(float lat, float lng)
    {
        if (!initialized) return;

        int x = Mathf.RoundToInt((lng - minLng) / cellSizeDeg - 0.5f);
        int y = Mathf.RoundToInt((lat - minLat) / cellSizeDeg - 0.5f);

        x = Mathf.Clamp(x, 0, lngCells - 1);
        y = Mathf.Clamp(y, 0, latCells - 1);

        if (cellSearched[x, y]) return;
        cellSearched[x, y] = true;

        if (fillRenderers[x, y] != null)
            fillRenderers[x, y].material.color = currentSearchedColor;
    }

    // ─────────────────────────────────────────────────────────────
    // RESET / CLEAR
    // ─────────────────────────────────────────────────────────────

    public void ResetGrid()
    {
        if (!initialized) return;
        for (int x = 0; x < lngCells; x++)
            for (int y = 0; y < latCells; y++)
            {
                cellSearched[x, y] = false;
                if (fillRenderers[x, y] != null)
                    fillRenderers[x, y].material.color = unsearchedFill;
            }
        initialized = false;
    }

    public void ClearGrid()
    {
        StopAllCoroutines();
        if (gridParent != null) Destroy(gridParent.gameObject);
        initialized = false;
    }

    // ─────────────────────────────────────────────────────────────
    // ALGORITHM COLOR
    // ─────────────────────────────────────────────────────────────

    public void SetAlgorithmColor(string algorithm)
    {
        switch (algorithm.ToLower().Replace(" ", ""))
        {
            case "lawnmower": currentSearchedColor = lawnmowerColor; break;
            case "spiral": currentSearchedColor = spiralColor; break;
            case "expandingsquare": currentSearchedColor = expandingSquareColor; break;
            case "randomwalk": currentSearchedColor = randomWalkColor; break;
            default: currentSearchedColor = lawnmowerColor; break;
        }
    }

    // ─────────────────────────────────────────────────────────────
    // MESHES
    // ─────────────────────────────────────────────────────────────

    private void CreateMeshes()
    {
        // Fill — solid quad
        fillMesh = new Mesh();
        fillMesh.name = "CellFill";
        fillMesh.vertices = new Vector3[]
        {
            new Vector3(-0.5f, -0.5f, 0),
            new Vector3( 0.5f, -0.5f, 0),
            new Vector3( 0.5f,  0.5f, 0),
            new Vector3(-0.5f,  0.5f, 0)
        };
        fillMesh.triangles = new int[] { 0, 2, 1, 0, 3, 2 };
        fillMesh.uv = new Vector2[]
        {
            new Vector2(0,0), new Vector2(1,0),
            new Vector2(1,1), new Vector2(0,1)
        };
        fillMesh.RecalculateNormals();

        // Outline — 4 border strips
        float t = 0.12f; // border thickness as fraction of cell size
        float h = 0.5f;

        outlineMesh = new Mesh();
        outlineMesh.name = "CellOutline";
        outlineMesh.vertices = new Vector3[]
        {
            // Bottom strip
            new Vector3(-h,   -h,   0), new Vector3( h,   -h,   0),
            new Vector3( h,   -h+t, 0), new Vector3(-h,   -h+t, 0),
            // Top strip
            new Vector3(-h,    h-t, 0), new Vector3( h,    h-t, 0),
            new Vector3( h,    h,   0), new Vector3(-h,    h,   0),
            // Left strip
            new Vector3(-h,   -h,   0), new Vector3(-h+t, -h,   0),
            new Vector3(-h+t,  h,   0), new Vector3(-h,    h,   0),
            // Right strip
            new Vector3( h-t, -h,   0), new Vector3( h,   -h,   0),
            new Vector3( h,    h,   0), new Vector3( h-t,  h,   0),
        };
        outlineMesh.triangles = new int[]
        {
            0,2,1,  0,3,2,
            4,6,5,  4,7,6,
            8,10,9, 8,11,10,
            12,14,13, 12,15,14
        };
        outlineMesh.uv = new Vector2[16];
        for (int i = 0; i < 16; i++)
            outlineMesh.uv[i] = Vector2.zero;
        outlineMesh.RecalculateNormals();
    }

    public float GetMinLng() => minLng;
    public float GetMinLat() => minLat;

    void OnDestroy()
    {
        ClearGrid();
        if (fillMesh != null) Destroy(fillMesh);
        if (outlineMesh != null) Destroy(outlineMesh);
    }
}