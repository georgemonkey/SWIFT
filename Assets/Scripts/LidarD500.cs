using System.IO.Ports;
using System.Threading;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class LidarD500 : MonoBehaviour
{
    const string PORT      = "/dev/tty.usbserial-0001";
    const int    BAUD      = 230400;
    const float  SCALE     = 0.001f;
    const float  MAX_RANGE = 5f;        // tighter range = more zoomed in
    const int    TEX_SIZE  = 1400;      // bigger texture = more detail
    const int    HISTORY   = 4;

    SerialPort    serial;
    Thread        readThread;
    volatile bool running = false;

    struct Point { public float x, z, dist; }
    Point[]        backBuf  = new Point[0];
    Point[]        frontBuf = new Point[0];
    readonly object bufLock  = new object();
    bool           newScan  = false;

    Texture2D tex;
    Color32[] pixels;
    Color32[] staticGrid;   // pre-baked grid so we don't redraw every frame
    int   cx, cy;
    float ppm;

    Queue<Point[]> history  = new Queue<Point[]>();
    float sweepAngle = 0f;

    int   ptCount  = 0;
    float nearest  = 0f;
    float farthest = 0f;
    float scanHz   = 0f;
    float lastScanT= 0f;

    GUIStyle labelStyle, dimStyle, titleStyle, alertStyle;
    bool stylesBuilt = false;

    // ─────────────────────────────────────────────────────────────────────────
    void Start()
    {
        SetupCamera();
        SetupTexture();
        SetupUI();

        try
        {
            serial = new SerialPort(PORT, BAUD) { ReadTimeout = 500 };
            serial.Open();
            Debug.Log("[LiDAR] Connected: " + PORT);
        }
        catch (System.Exception e)
        {
            Debug.LogError("[LiDAR] " + e.Message);
            return;
        }

        running    = true;
        readThread = new Thread(ReadLoop) { IsBackground = true };
        readThread.Start();
    }

    void SetupCamera()
    {
        var cam = Camera.main;
        cam.orthographic     = true;
        cam.orthographicSize = 1f;
        cam.backgroundColor  = new Color(0.02f, 0.04f, 0.02f);
        cam.clearFlags       = CameraClearFlags.SolidColor;
        cam.transform.position = new Vector3(0, 10, 0);
        cam.transform.rotation = Quaternion.Euler(90, 0, 0);
    }

    void SetupTexture()
    {
        tex            = new Texture2D(TEX_SIZE, TEX_SIZE, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        pixels         = new Color32[TEX_SIZE * TEX_SIZE];
        cx             = TEX_SIZE / 2;
        cy             = TEX_SIZE / 2;
        ppm            = (TEX_SIZE / 2f) / MAX_RANGE;

        BakeStaticGrid();
        System.Array.Copy(staticGrid, pixels, pixels.Length);
        PushTexture();
    }

    void SetupUI()
    {
        var canvasGO = new GameObject("Canvas");
        var canvas   = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGO.AddComponent<CanvasScaler>();
        canvasGO.AddComponent<GraphicRaycaster>();

        // Map takes up full screen minus a left panel
        int panelW = 240;
        int pad    = 6;

        var imgGO   = new GameObject("MapImage");
        imgGO.transform.SetParent(canvasGO.transform, false);
        var img     = imgGO.AddComponent<RawImage>();
        img.texture = tex;

        var rt = img.rectTransform;
        rt.anchorMin        = Vector2.zero;
        rt.anchorMax        = Vector2.one;
        rt.offsetMin        = new Vector2(panelW + pad, pad);
        rt.offsetMax        = new Vector2(-pad, -pad);
    }

    // ── Pre-bake the static grid once ────────────────────────────────────────
    void BakeStaticGrid()
    {
        staticGrid = new Color32[TEX_SIZE * TEX_SIZE];
        Color32 bg = new Color32(6, 11, 6, 255);
        for (int i = 0; i < staticGrid.Length; i++) staticGrid[i] = bg;

        // Axes
        Color32 axis = new Color32(22, 55, 22, 255);
        for (int i = 0; i < TEX_SIZE; i++)
        {
            GridPx(staticGrid, i,  cy, axis);
            GridPx(staticGrid, cx, i,  axis);
        }

        // Diagonal guide lines
        Color32 diag = new Color32(14, 34, 14, 255);
        int half = TEX_SIZE / 2;
        for (int i = -half; i < half; i++)
        {
            GridPx(staticGrid, cx + i, cy + i, diag);
            GridPx(staticGrid, cx + i, cy - i, diag);
        }

        // Range rings
        float[] rings  = { 0.5f, 1f, 1.5f, 2f, 2.5f, 3f, 4f, 5f };
        foreach (float r in rings)
        {
            if (r > MAX_RANGE) continue;
            bool   major = (r % 1f == 0f);
            Color32 rc   = major
                ? new Color32(30, 75, 30, 255)
                : new Color32(16, 40, 16, 255);

            float  rad  = r * ppm;
            int    segs = Mathf.Max(512, (int)(rad * Mathf.PI * 2f));
            for (int s = 0; s < segs; s++)
            {
                float a  = s / (float)segs * Mathf.PI * 2f;
                int   px = cx + Mathf.RoundToInt(Mathf.Cos(a) * rad);
                int   py = cy + Mathf.RoundToInt(Mathf.Sin(a) * rad);
                GridPx(staticGrid, px, py, rc);
            }
        }

        // Compass tick marks every 10°
        for (int deg = 0; deg < 360; deg += 10)
        {
            float a      = deg * Mathf.Deg2Rad;
            bool  major  = (deg % 90 == 0);
            bool  medium = (deg % 45 == 0);
            float inner  = MAX_RANGE * ppm * (major ? 0.93f : medium ? 0.96f : 0.98f);
            float outer  = MAX_RANGE * ppm;
            Color32 tc   = major
                ? new Color32(50, 130, 50, 255)
                : new Color32(30, 75, 30, 255);

            for (float t = inner; t <= outer; t += 0.5f)
            {
                int px = cx + Mathf.RoundToInt(Mathf.Cos(a) * t);
                int py = cy + Mathf.RoundToInt(Mathf.Sin(a) * t);
                GridPx(staticGrid, px, py, tc);
            }
        }

        // Outer border circle
        float borderR = MAX_RANGE * ppm - 1f;
        int borderSegs = 2048;
        for (int s = 0; s < borderSegs; s++)
        {
            float a  = s / (float)borderSegs * Mathf.PI * 2f;
            int   px = cx + Mathf.RoundToInt(Mathf.Cos(a) * borderR);
            int   py = cy + Mathf.RoundToInt(Mathf.Sin(a) * borderR);
            GridPx(staticGrid, px, py, new Color32(40, 100, 40, 255));
        }

        // Origin dot
        for (int dx = -5; dx <= 5; dx++)
            for (int dy = -5; dy <= 5; dy++)
                if (dx * dx + dy * dy <= 25)
                    GridPx(staticGrid, cx + dx, cy + dy, new Color32(0, 255, 120, 255));
    }

    void GridPx(Color32[] buf, int x, int y, Color32 c)
    {
        if ((uint)x >= TEX_SIZE || (uint)y >= TEX_SIZE) return;
        buf[y * TEX_SIZE + x] = c;
    }

    // ─────────────────────────────────────────────────────────────────────────
    void ReadLoop()
    {
        while (running)
        {
            try
            {
                while (serial.ReadByte() != 0x54) {}

                int    lenType  = serial.ReadByte();
                int    nPts     = lenType & 0x1F;
                int    pktSize  = 2 + 2 + nPts * 3 + 2 + 2 + 1;
                byte[] pkt      = new byte[pktSize];
                int    got      = 0;
                while (got < pktSize)
                    got += serial.Read(pkt, got, pktSize - got);

                float startA = (pkt[3] << 8 | pkt[2]) / 100f * Mathf.Deg2Rad;
                float endA   = (pkt[4 + nPts * 3 + 1] << 8 | pkt[4 + nPts * 3]) / 100f * Mathf.Deg2Rad;
                if (endA < startA) endA += Mathf.PI * 2f;
                float step = nPts > 1 ? (endA - startA) / (nPts - 1) : 0f;

                var pts = new List<Point>(nPts);
                for (int i = 0; i < nPts; i++)
                {
                    int   off  = 4 + i * 3;
                    float dist = (pkt[off + 1] << 8 | pkt[off]) * SCALE;
                    if (dist < 0.05f || dist > MAX_RANGE) continue;
                    float a = startA + step * i;
                    pts.Add(new Point { x = Mathf.Cos(a) * dist, z = Mathf.Sin(a) * dist, dist = dist });
                }

                lock (bufLock) { backBuf = pts.ToArray(); newScan = true; }
            }
            catch (System.TimeoutException) {}
            catch (System.Exception e) { Debug.LogError("[LiDAR] " + e.Message); }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    void Update()
    {
        sweepAngle = (sweepAngle + Time.deltaTime * 150f) % 360f;

        if (!newScan) return;
        lock (bufLock) { frontBuf = backBuf; newScan = false; }

        // Stats
        ptCount  = frontBuf.Length;
        nearest  = 999f;
        farthest = 0f;
        foreach (var p in frontBuf)
        {
            if (p.dist < nearest)  nearest  = p.dist;
            if (p.dist > farthest) farthest = p.dist;
        }
        if (nearest > 900f) nearest = 0f;
        float now = Time.time;
        scanHz    = 1f / Mathf.Max(0.001f, now - lastScanT);
        lastScanT = now;

        // Restore static grid
        System.Array.Copy(staticGrid, pixels, pixels.Length);

        // Sweep glow — wide fan
        float sweepRad = sweepAngle * Mathf.Deg2Rad;
        int   sweepLen = Mathf.RoundToInt(MAX_RANGE * ppm);
        for (float spread = -3f; spread <= 0f; spread += 0.5f)
        {
            float sr = sweepRad + spread * Mathf.Deg2Rad;
            for (int s = 2; s < sweepLen; s++)
            {
                float alpha = (1f - s / (float)sweepLen) * (1f - Mathf.Abs(spread) / 4f);
                byte  g     = (byte)(alpha * 70);
                int   px    = cx + Mathf.RoundToInt(Mathf.Cos(sr) * s);
                int   py    = cy + Mathf.RoundToInt(Mathf.Sin(sr) * s);
                BlendPx(px, py, 0, g, (byte)(g / 3), (byte)(alpha * 200));
            }
        }

        // History with fade
        history.Enqueue(frontBuf);
        if (history.Count > HISTORY) history.Dequeue();

        int fi = 0;
        foreach (var scan in history)
        {
            float age   = fi / (float)(HISTORY);
            byte  alpha = (byte)(255 * (1f - age * 0.8f));
            foreach (var p in scan) PlotPoint(p, alpha);
            fi++;
        }

        PushTexture();
    }

    void PlotPoint(Point p, byte alpha)
    {
        int px = cx + Mathf.RoundToInt(p.x * ppm);
        int py = cy + Mathf.RoundToInt(p.z * ppm);

        Color32 col = DistColor(p.dist);
        col.a = alpha;

        // Bright core
        SetPx(px, py, col);

        // Bigger bloom for closer objects
        int bloom = p.dist < 1f ? 2 : 1;
        for (int dx = -bloom; dx <= bloom; dx++)
        for (int dy = -bloom; dy <= bloom; dy++)
        {
            if (dx == 0 && dy == 0) continue;
            float falloff = 1f - (Mathf.Abs(dx) + Mathf.Abs(dy)) / (float)(bloom * 2 + 1);
            BlendPx(px + dx, py + dy,
                (byte)(col.r * falloff * 0.5f),
                (byte)(col.g * falloff * 0.5f),
                (byte)(col.b * falloff * 0.5f),
                (byte)(alpha * falloff * 0.6f));
        }
    }

    Color32 DistColor(float d)
    {
        float t = Mathf.Clamp01(d / MAX_RANGE);
        if (t < 0.2f) {
            return new Color32(0, 255, 255, 255);                                              // cyan
        } else if (t < 0.4f) {
            float u = (t - 0.2f) / 0.2f;
            return new Color32(0, 255, (byte)(255*(1-u)), 255);                                // cyan→green
        } else if (t < 0.6f) {
            float u = (t - 0.4f) / 0.2f;
            return new Color32((byte)(255*u), 255, 0, 255);                                    // green→yellow
        } else if (t < 0.8f) {
            float u = (t - 0.6f) / 0.2f;
            return new Color32(255, (byte)(255*(1-u*0.5f)), 0, 255);                           // yellow→orange
        } else {
            float u = (t - 0.8f) / 0.2f;
            return new Color32(255, (byte)(120*(1-u)), 0, 255);                                // orange→red
        }
    }

    void SetPx(int x, int y, Color32 c)
    {
        if ((uint)x >= TEX_SIZE || (uint)y >= TEX_SIZE) return;
        pixels[y * TEX_SIZE + x] = c;
    }

    void BlendPx(int x, int y, byte r, byte g, byte b, byte a)
    {
        if ((uint)x >= TEX_SIZE || (uint)y >= TEX_SIZE) return;
        int   i = y * TEX_SIZE + x;
        float t = a / 255f;
        pixels[i] = new Color32(
            (byte)Mathf.Max(pixels[i].r, r * t),
            (byte)Mathf.Max(pixels[i].g, g * t),
            (byte)Mathf.Max(pixels[i].b, b * t),
            255);
    }

    void PushTexture() { tex.SetPixels32(pixels); tex.Apply(false); }

    // ── Side panel GUI ────────────────────────────────────────────────────────
    void BuildStyles()
    {
        labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold };
        labelStyle.normal.textColor = new Color(0f, 0.9f, 0.45f);
        dimStyle   = new GUIStyle(labelStyle);
        dimStyle.normal.textColor   = new Color(0f, 0.45f, 0.22f);
        titleStyle = new GUIStyle(labelStyle) { fontSize = 17 };
        titleStyle.normal.textColor = new Color(0f, 1f, 0.5f);
        alertStyle = new GUIStyle(labelStyle) { fontSize = 14 };
        alertStyle.normal.textColor = new Color(1f, 0.25f, 0f);
        stylesBuilt = true;
    }

    void OnGUI()
    {
        if (!stylesBuilt) BuildStyles();

        int pw = 232;
        GUI.color = new Color(0.03f, 0.06f, 0.03f, 0.98f);
        GUI.DrawTexture(new Rect(0, 0, pw, Screen.height), Texture2D.whiteTexture);
        GUI.color = new Color(0f, 0.45f, 0.22f);
        GUI.DrawTexture(new Rect(pw - 1, 0, 1, Screen.height), Texture2D.whiteTexture);
        GUI.color = Color.white;

        float y = 16f, lh = 22f;

        GUI.Label(new Rect(12, y, pw, 30), "◈ LIDAR D500", titleStyle); y += 36;
        Divider(y); y += 14;

        GUI.Label(new Rect(12, y, pw, lh), "CONNECTION", dimStyle); y += lh;
        GUI.Label(new Rect(12, y, pw, lh), "● LIVE", labelStyle); y += lh;
        GUI.Label(new Rect(12, y, pw, lh * 1.5f), PORT, dimStyle); y += lh * 2f;

        Divider(y); y += 14;
        GUI.Label(new Rect(12, y, pw, lh), "SCAN", dimStyle); y += lh;
        Row("Points",   ptCount.ToString(),        ref y, lh);
        Row("Rate",     $"{scanHz:F1} Hz",          ref y, lh);
        Row("Nearest",  nearest  > 0 ? $"{nearest:F2} m"  : "--", ref y, lh);
        Row("Farthest", farthest > 0 ? $"{farthest:F2} m" : "--", ref y, lh);
        Row("Range",    $"{MAX_RANGE} m",           ref y, lh);
        y += 8;

        Divider(y); y += 14;
        GUI.Label(new Rect(12, y, pw, lh), "DISTANCE", dimStyle); y += lh;
        ColorBar(12, y, pw - 24, 14); y += 22;
        GUIStyle tiny = new GUIStyle(dimStyle) { fontSize = 10 };
        GUI.Label(new Rect(12,      y, 30, lh), "0m",              tiny);
        GUI.Label(new Rect(pw/2-16, y, 40, lh), $"{MAX_RANGE/2}m", tiny);
        GUI.Label(new Rect(pw - 36, y, 30, lh), $"{MAX_RANGE}m",   tiny);
        y += lh * 1.5f;

        Divider(y); y += 14;
        GUI.Label(new Rect(12, y, pw, lh), "COMPASS", dimStyle); y += lh;
        string[] dirs = { "↑  N   0°", "→  E  90°", "↓  S  180°", "←  W  270°" };
        foreach (var d in dirs) { GUI.Label(new Rect(12, y, pw, lh), d, labelStyle); y += lh; }
        y += 10;

        if (nearest > 0 && nearest < 0.6f)
        {
            Divider(y); y += 10;
            GUI.Label(new Rect(12, y, pw, lh * 2), $"⚠ OBSTACLE\n  {nearest:F2} m", alertStyle);
        }
    }

    void Row(string label, string val, ref float y, float lh)
    {
        GUI.Label(new Rect(12,  y, 130, lh), label, dimStyle);
        GUI.Label(new Rect(145, y,  80, lh), val,   labelStyle);
        y += lh;
    }

    void Divider(float y)
    {
        GUI.color = new Color(0f, 0.3f, 0.15f, 0.7f);
        GUI.DrawTexture(new Rect(8, y, 216, 1), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    void ColorBar(float x, float y, float w, float h)
    {
        int steps = 10;
        float sw = w / steps;
        for (int i = 0; i < steps; i++)
        {
            Color32 c = DistColor((i / (float)(steps - 1)) * MAX_RANGE);
            GUI.color = new Color(c.r / 255f, c.g / 255f, c.b / 255f);
            GUI.DrawTexture(new Rect(x + i * sw, y, sw + 1, h), Texture2D.whiteTexture);
        }
        GUI.color = Color.white;
    }

    void OnDestroy()
    {
        running = false;
        Thread.Sleep(100);
        readThread?.Join(500);
        if (serial != null && serial.IsOpen) serial.Close();
    }
}