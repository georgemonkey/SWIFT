using UnityEngine;

public class DroneTrail : MonoBehaviour
{
    [Header("Trail Settings")]
    public float trailTime = 8f;       // seconds before trail fades
    public float trailWidth = 0.5f;
    public Color trailColor = Color.cyan;

    private TrailRenderer trail;
    private DroneController controller;

    void Start()
    {
        controller = GetComponent<DroneController>();

        trail = gameObject.AddComponent<TrailRenderer>();
        trail.time = trailTime;
        trail.startWidth = trailWidth;
        trail.endWidth = 0f;
        trail.minVertexDistance = 0.1f;
        trail.autodestruct = false;

        // Fading gradient
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(trailColor, 0f),
                new GradientColorKey(trailColor, 1f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(1f, 0f),  // fully visible at head
                new GradientAlphaKey(0f, 1f)   // fully faded at tail
            }
        );
        trail.colorGradient = gradient;

        // Material
        trail.material = new Material(
            Shader.Find("Universal Render Pipeline/Unlit")
            ?? Shader.Find("Unlit/Color"));
        trail.material.color = trailColor;

        // Start with trail disabled — enable on takeoff
        trail.enabled = false;
    }

    void Update()
    {
        if (controller == null) return;

        // Enable trail once drone starts moving
        if (!trail.enabled && controller.missionComplete == false)
        {
            bool isMoving = controller.IsTraveling() ||
                controller.coveragePercent > 0f;
            if (isMoving) trail.enabled = true;
        }

        // Change trail color based on mode
        if (trail.enabled)
        {
            Color targetColor = controller.IsTraveling()
                ? Color.yellow   // yellow when traveling to sector
                : trailColor;    // cyan when searching

            Gradient g = new Gradient();
            g.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(targetColor, 0f),
                    new GradientColorKey(targetColor, 1f)
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(0.8f, 0f),
                    new GradientAlphaKey(0f,   1f)
                }
            );
            trail.colorGradient = g;
        }
    }

    public void ResetTrail()
    {
        if (trail != null)
        {
            trail.Clear();
            trail.enabled = false;
        }
    }
}