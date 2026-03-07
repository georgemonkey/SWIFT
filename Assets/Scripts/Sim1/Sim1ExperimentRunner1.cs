using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Automated experiment runner for Sim1.
/// Runs a full 40-trial experiment with one keypress (press X).
///
/// EXPERIMENTAL DESIGN — 40 trials total:
///   4 difficulty tiers  × 2 APF/LF configs × 5 repeats = 40 trials
///
///   Tier 1 — Easy:    3–5  obstacles, wide spacing
///   Tier 2 — Medium:  6–9  obstacles, moderate spacing
///   Tier 3 — Hard:    10–14 obstacles, narrow corridors
///   Tier 4 — Extreme: 15–20 obstacles, dense field
///
///   Config A: APF=ON,  LeaderFollower=ON   (full system)
///   Config B: APF=ON,  LeaderFollower=OFF  (APF only, no coordination)
///
///   Within each tier, obstacle positions are randomized per repeat.
///   Trials are shuffled so difficulty doesn't always increase linearly.
///
/// Obstacle spawning:
///   - Spawns cubes tagged "Obstacle" between staging area and targets
///   - Minimum clearance from drone spawn zone and target zone
///   - Minimum spacing between obstacles
///   - Size scales slightly with difficulty
///   - All obstacles destroyed and re-spawned between trials
///
/// Attach alongside Sim1Manager on the same GameObject.
/// Assign: obstaclePrefab (a cube with "Obstacle" tag) in Inspector.
/// Press X to run full experiment. Press E to export when done.
/// </summary>
public class Sim1ExperimentRunner : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────
    // INSPECTOR
    // ─────────────────────────────────────────────────────────────

    [Header("Prefabs")]
    [Tooltip("Cube prefab tagged 'Obstacle' — must have a Collider")]
    public GameObject obstaclePrefab;

    [Header("Play Area — must match your scene layout")]
    [Tooltip("Centre of the area obstacles can spawn in (X,Z only)")]
    public Vector2 playAreaCenter = new Vector2(0f, 5f);

    [Tooltip("Half-extents of the obstacle spawn zone (X,Z)")]
    public Vector2 playAreaHalfExtents = new Vector2(18f, 18f);

    [Tooltip("Height to spawn obstacle bases at (Y)")]
    public float obstacleBaseY = 0f;

    [Tooltip("No obstacles within this radius of any staging position")]
    public float stagingClearance = 8f;

    [Tooltip("No obstacles within this radius of any target position")]
    public float targetClearance = 8f;

    [Tooltip("Minimum distance between any two obstacles")]
    public float minObstacleSpacing = 3.5f;

    [Header("Experiment Settings")]
    [Tooltip("Repeats per difficulty tier per config (total = tiers x configs x repeats)")]
    public int repeatsPerCell = 5;

    [Tooltip("Max seconds per trial before forcing RTH and moving on")]
    public float trialTimeoutSeconds = 120f;

    [Header("Simulation Speed")]
    [Tooltip("Time scale multiplier during experiment. 1=normal, 4=4x faster. Max ~6 before physics degrades.")]
    [Range(1f, 6f)]
    public float simulationSpeed = 4f;

    [Tooltip("Seconds to wait between trials for physics to settle")]
    public float trialCooldown = 1.5f;

    [Tooltip("Shuffle trial order so difficulty doesn't always ramp linearly")]
    public bool shuffleTrials = true;

    // ─────────────────────────────────────────────────────────────
    // PUBLIC STATE
    // ─────────────────────────────────────────────────────────────

    public bool ExperimentRunning { get; private set; }
    public int CurrentTrial { get; private set; }
    public int TotalTrials { get; private set; }

    // ─────────────────────────────────────────────────────────────
    // PRIVATE
    // ─────────────────────────────────────────────────────────────

    private Sim1Manager manager;
    private Sim1DataExporter exporter;
    private Sim1UI ui;

    private float originalTimeScale;
    private float originalFixedDeltaTime;

    private List<GameObject> spawnedObstacles = new List<GameObject>();

    // ─────────────────────────────────────────────────────────────
    // DIFFICULTY TIERS
    // ─────────────────────────────────────────────────────────────

    private struct DifficultyTier
    {
        public string name;
        public int minObstacles;
        public int maxObstacles;
        public float minObstacleWidth;   // X scale
        public float maxObstacleWidth;
        public float minObstacleHeight;  // Y scale
        public float maxObstacleHeight;
    }

    private readonly DifficultyTier[] tiers = new DifficultyTier[]
    {
        new DifficultyTier { name = "Easy",    minObstacles = 3,  maxObstacles = 5,
                             minObstacleWidth = 1.5f, maxObstacleWidth = 3f,
                             minObstacleHeight = 4f,  maxObstacleHeight = 8f  },

        new DifficultyTier { name = "Medium",  minObstacles = 6,  maxObstacles = 9,
                             minObstacleWidth = 2f,   maxObstacleWidth = 4f,
                             minObstacleHeight = 5f,  maxObstacleHeight = 12f },

        new DifficultyTier { name = "Hard",    minObstacles = 10, maxObstacles = 14,
                             minObstacleWidth = 2.5f, maxObstacleWidth = 5f,
                             minObstacleHeight = 6f,  maxObstacleHeight = 15f },

        new DifficultyTier { name = "Extreme", minObstacles = 15, maxObstacles = 20,
                             minObstacleWidth = 3f,   maxObstacleWidth = 6f,
                             minObstacleHeight = 6f,  maxObstacleHeight = 18f },
    };

    // ─────────────────────────────────────────────────────────────
    // INIT
    // ─────────────────────────────────────────────────────────────

    void Start()
    {
        manager = FindObjectOfType<Sim1Manager>();
        exporter = FindObjectOfType<Sim1DataExporter>();
        ui = FindObjectOfType<Sim1UI>();

        if (manager == null)
        {
            Debug.LogError("[ExperimentRunner] Could not find Sim1Manager in scene — disabling");
            enabled = false;
            return;
        }

        TotalTrials = tiers.Length * 2 * repeatsPerCell; // 4 tiers x 2 configs x 5 = 40
        Debug.Log($"[ExperimentRunner] Ready | {TotalTrials} trials planned | Press X to start");
    }

    // ─────────────────────────────────────────────────────────────
    // INPUT
    // ─────────────────────────────────────────────────────────────

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.X) && !ExperimentRunning)
            StartCoroutine(RunExperiment());
    }

    // ─────────────────────────────────────────────────────────────
    // EXPERIMENT LOOP
    // ─────────────────────────────────────────────────────────────

    private IEnumerator RunExperiment()
    {
        ExperimentRunning = true;
        CurrentTrial = 0;

        // ── Speed up simulation ───────────────────────────────────
        // Scale fixedDeltaTime proportionally so physics substeps stay
        // the same relative to simulated time — quality is unchanged
        originalTimeScale = Time.timeScale;
        originalFixedDeltaTime = Time.fixedDeltaTime;

        Time.timeScale = simulationSpeed;
        Time.fixedDeltaTime = originalFixedDeltaTime * simulationSpeed;
        Sim1Variables.SafeDeltaTimeCap = 0.05f * simulationSpeed;

        Debug.Log($"[ExperimentRunner] ═══ EXPERIMENT START — {TotalTrials} trials | Speed: {simulationSpeed}x ═══");

        // Build trial list
        List<TrialSpec> trials = BuildTrialList();

        foreach (TrialSpec spec in trials)
        {
            CurrentTrial++;

            Debug.Log($"[ExperimentRunner] ── Trial {CurrentTrial}/{TotalTrials} | " +
                      $"Tier: {spec.tier.name} | Obstacles: {spec.obstacleCount} | " +
                      $"APF: {spec.apfOn} | LF: {spec.lfOn}");

            // 1. Spawn obstacles for this trial
            SpawnObstacles(spec);

            // 2. Configure manager for this trial
            manager.apfActive = spec.apfOn;
            manager.leaderFollowerActive = spec.lfOn;
            manager.trialNotes = $"{spec.tier.name}_obs{spec.obstacleCount}_APF{(spec.apfOn ? 1 : 0)}_LF{(spec.lfOn ? 1 : 0)}";

            // 3. Run the trial — wait for it to complete
            manager.StartRun();
            yield return StartCoroutine(WaitForTrialComplete());

            // 4. Cooldown between trials
            yield return new WaitForSeconds(trialCooldown);

            // 5. Destroy obstacles
            ClearObstacles();
        }

        // Export automatically when done
        exporter.ExportCSV();

        // ── Restore simulation speed ──────────────────────────────
        Time.timeScale = originalTimeScale;
        Time.fixedDeltaTime = originalFixedDeltaTime;
        Sim1Variables.SafeDeltaTimeCap = 0.05f;

        ExperimentRunning = false;
        Debug.Log($"[ExperimentRunner] ═══ EXPERIMENT COMPLETE — {TotalTrials} trials recorded ═══");

        if (ui != null)
            ui.OnMissionComplete(0f); // update status text
    }

    // ─────────────────────────────────────────────────────────────
    // WAIT FOR TRIAL — polls Sim1Manager, enforces timeout
    // ─────────────────────────────────────────────────────────────

    private IEnumerator WaitForTrialComplete()
    {
        // Wait for mission to actually start (StartRun is async)
        yield return new WaitForSeconds(0.2f);

        float elapsed = 0f;

        while (manager.MissionRunning)
        {
            yield return new WaitForSeconds(0.5f);
            elapsed += 0.5f;

            if (elapsed >= trialTimeoutSeconds)
            {
                Debug.LogWarning($"[ExperimentRunner] Trial {CurrentTrial} TIMED OUT at {elapsed:F0}s — forcing RTH");
                manager.ForceEndMission();

                // Wait for drones to RTH and land after forced end
                // Give them up to 30s extra to reach home
                float rthWait = 0f;
                while (manager.MissionRunning && rthWait < 30f)
                {
                    yield return new WaitForSeconds(0.5f);
                    rthWait += 0.5f;
                }

                // If still not done, something is very wrong — bail anyway
                if (manager.MissionRunning)
                    Debug.LogError($"[ExperimentRunner] Trial {CurrentTrial} still running after RTH timeout — skipping");

                break;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────
    // TRIAL LIST BUILDER
    // ─────────────────────────────────────────────────────────────

    private struct TrialSpec
    {
        public DifficultyTier tier;
        public int obstacleCount;
        public bool apfOn;
        public bool lfOn;
    }

    private List<TrialSpec> BuildTrialList()
    {
        List<TrialSpec> list = new List<TrialSpec>();

        foreach (DifficultyTier tier in tiers)
        {
            for (int repeat = 0; repeat < repeatsPerCell; repeat++)
            {
                int count = Random.Range(tier.minObstacles, tier.maxObstacles + 1);

                // Config A: full system
                list.Add(new TrialSpec { tier = tier, obstacleCount = count, apfOn = true, lfOn = true });

                // Config B: APF only, no leader-follower
                list.Add(new TrialSpec { tier = tier, obstacleCount = count, apfOn = true, lfOn = false });
            }
        }

        if (shuffleTrials)
            Shuffle(list);

        return list;
    }

    // ─────────────────────────────────────────────────────────────
    // OBSTACLE SPAWNING
    // ─────────────────────────────────────────────────────────────

    private void SpawnObstacles(TrialSpec spec)
    {
        ClearObstacles();

        if (obstaclePrefab == null)
        {
            Debug.LogWarning("[ExperimentRunner] obstaclePrefab not assigned — spawning default cubes");
        }

        // Collect exclusion zones (staging + targets)
        List<Vector2> exclusionPoints = new List<Vector2>();
        foreach (Transform t in manager.stagingPositions)
            exclusionPoints.Add(new Vector2(t.position.x, t.position.z));
        foreach (Transform t in manager.targetObjects)
            exclusionPoints.Add(new Vector2(t.position.x, t.position.z));

        // Track placed obstacle positions to enforce spacing
        List<Vector2> placedPositions = new List<Vector2>();

        int placed = 0;
        int attempts = 0;
        int maxAttempts = spec.obstacleCount * 50; // give up after this many tries

        while (placed < spec.obstacleCount && attempts < maxAttempts)
        {
            attempts++;

            // Random position within play area
            float x = playAreaCenter.x + Random.Range(-playAreaHalfExtents.x, playAreaHalfExtents.x);
            float z = playAreaCenter.y + Random.Range(-playAreaHalfExtents.y, playAreaHalfExtents.y);
            Vector2 candidate = new Vector2(x, z);

            // Check staging and target clearance
            bool tooClose = false;
            foreach (Vector2 ep in exclusionPoints)
            {
                if (Vector2.Distance(candidate, ep) < Mathf.Max(stagingClearance, targetClearance))
                {
                    tooClose = true;
                    break;
                }
            }
            if (tooClose) continue;

            // Check spacing between obstacles
            foreach (Vector2 pp in placedPositions)
            {
                if (Vector2.Distance(candidate, pp) < minObstacleSpacing)
                {
                    tooClose = true;
                    break;
                }
            }
            if (tooClose) continue;

            // Spawn
            float w = Random.Range(spec.tier.minObstacleWidth, spec.tier.maxObstacleWidth);
            float h = Random.Range(spec.tier.minObstacleHeight, spec.tier.maxObstacleHeight);
            float d = Random.Range(spec.tier.minObstacleWidth, spec.tier.maxObstacleWidth);

            Vector3 spawnPos = new Vector3(x, obstacleBaseY + h * 0.5f, z);

            GameObject obs = obstaclePrefab != null
                ? Instantiate(obstaclePrefab, spawnPos, Quaternion.Euler(0, Random.Range(0f, 360f), 0))
                : GameObject.CreatePrimitive(PrimitiveType.Cube);

            if (obstaclePrefab == null)
            {
                obs.transform.position = spawnPos;
                obs.transform.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
                obs.tag = "Obstacle";
            }

            obs.transform.localScale = new Vector3(w, h, d);
            obs.name = $"Obstacle_T{CurrentTrial}_{placed}";

            // Ensure tag is set even on prefab instances
            obs.tag = "Obstacle";

            // Make static for better physics performance
            obs.isStatic = true;

            spawnedObstacles.Add(obs);
            placedPositions.Add(candidate);
            placed++;
        }

        if (placed < spec.obstacleCount)
            Debug.LogWarning($"[ExperimentRunner] Only placed {placed}/{spec.obstacleCount} obstacles after {attempts} attempts — increase play area or reduce obstacle count");
        else
            Debug.Log($"[ExperimentRunner] Spawned {placed} obstacles | Tier: {spec.tier.name}");
    }

    private void ClearObstacles()
    {
        foreach (GameObject obs in spawnedObstacles)
            if (obs != null) Destroy(obs);
        spawnedObstacles.Clear();
    }

    // ─────────────────────────────────────────────────────────────
    // FISHER-YATES SHUFFLE
    // ─────────────────────────────────────────────────────────────

    private void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            T tmp = list[i];
            list[i] = list[j];
            list[j] = tmp;
        }
    }

    // ─────────────────────────────────────────────────────────────
    // CLEANUP
    // ─────────────────────────────────────────────────────────────

    void OnDestroy()
    {
        ClearObstacles();

        // Always restore timescale if experiment is interrupted
        if (ExperimentRunning)
        {
            Time.timeScale = originalTimeScale > 0 ? originalTimeScale : 1f;
            Time.fixedDeltaTime = originalFixedDeltaTime > 0 ? originalFixedDeltaTime : 0.02f;
            Sim1Variables.SafeDeltaTimeCap = 0.05f;
        }
    }
}