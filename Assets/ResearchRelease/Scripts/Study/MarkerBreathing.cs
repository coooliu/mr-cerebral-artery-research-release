using UnityEngine;

/// <summary>
/// Applies a simple breathing-scale animation to a marker object.
///
/// The animation scales the marker around a configurable base diameter using a sine wave:
/// scale = markerLocalDiameter * (1 + pulseAmplitude * sin(time * pulseSpeed + phase))
///
/// This script is intended for lightweight visual emphasis on 2D or 3D landmark prefabs.
/// </summary>
[DisallowMultipleComponent]
public class MarkerBreathing : MonoBehaviour
{
    [Header("Base Size")]
    [Tooltip("Base marker diameter expressed in the object's local scale space.")]
    public float markerLocalDiameter = 0.02f;

    [Header("Pulse Settings")]
    [Tooltip("Pulse amplitude. For example, 0.1 means ¡À10% around the base size.")]
    public float pulseAmplitude = 0.10f;

    [Tooltip("Pulse speed multiplier.")]
    public float pulseSpeed = 1.5f;

    [Tooltip("Whether to use unscaled time so the animation ignores Time.timeScale.")]
    public bool useUnscaledTime = false;

    [Header("Phase")]
    [Tooltip("Randomizes the phase so multiple markers do not breathe in perfect sync.")]
    public bool randomizePhase = true;

    [Tooltip("Fixed phase used when randomizePhase is disabled.")]
    public float phase = 0f;

    private float initialPhase;

    private void Start()
    {
        initialPhase = randomizePhase ? Random.Range(0f, Mathf.PI * 2f) : phase;
        ApplyScale(GetCurrentTime());
    }

    private void Update()
    {
        ApplyScale(GetCurrentTime());
    }

    private float GetCurrentTime()
    {
        return useUnscaledTime ? Time.unscaledTime : Time.time;
    }

    private void ApplyScale(float timeValue)
    {
        float scaleFactor = 1f + pulseAmplitude * Mathf.Sin(timeValue * pulseSpeed + initialPhase);
        float safeDiameter = Mathf.Max(0.0001f, markerLocalDiameter * scaleFactor);
        transform.localScale = Vector3.one * safeDiameter;
    }

    private void OnDrawGizmosSelected()
    {
#if UNITY_EDITOR
        UnityEditor.Handles.color = Color.cyan;
        float diameter = Mathf.Max(0.0001f, markerLocalDiameter);
        UnityEditor.Handles.DrawWireDisc(transform.position, transform.forward, diameter * 0.5f);
#endif
    }
}
