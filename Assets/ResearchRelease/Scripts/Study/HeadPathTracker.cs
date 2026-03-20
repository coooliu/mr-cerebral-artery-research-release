using System.Collections;
using UnityEngine;

/// <summary>
/// Tracks cumulative head movement distance during a study task.
///
/// This research-release cleanup keeps the original runtime behavior and public API:
/// - samples the referenced head transform at a fixed interval,
/// - accumulates traveled distance while tracking is active,
/// - exposes start, stop, and reset methods for StudyController.
///
/// If no head transform is assigned in the Inspector, the script falls back to Camera.main.
/// </summary>
public class HeadPathTracker : MonoBehaviour
{
    [Header("Tracking Source")]
    [SerializeField]
    [Tooltip("Head or camera transform to sample. If left empty, Camera.main will be used when available.")]
    private Transform head;

    [Header("Sampling")]
    [SerializeField]
    [Tooltip("Sampling frequency used for head-path accumulation.")]
    private float sampleRateHz = 30f;

    /// <summary>
    /// Indicates whether distance accumulation is currently enabled.
    /// </summary>
    public bool Active { get; private set; } = false;

    /// <summary>
    /// Cumulative traveled distance in meters.
    /// </summary>
    public float TotalDistance { get; private set; }

    /// <summary>
    /// Public-facing runtime flag used by the study controller.
    /// </summary>
    public bool IsTracking { get; private set; }

    private Vector3 lastPosition;
    private bool hasLastPosition;
    private Coroutine samplingRoutine;

    private void Awake()
    {
        if (head == null && Camera.main != null)
        {
            head = Camera.main.transform;
        }
    }

    private void OnEnable()
    {
        samplingRoutine = StartCoroutine(SampleHeadPath());
    }

    private void OnDisable()
    {
        if (samplingRoutine != null)
        {
            StopCoroutine(samplingRoutine);
            samplingRoutine = null;
        }

        hasLastPosition = false;
    }

    private IEnumerator SampleHeadPath()
    {
        float safeRate = Mathf.Max(1f, sampleRateHz);
        WaitForSeconds wait = new WaitForSeconds(1f / safeRate);

        while (true)
        {
            if (Active && head != null)
            {
                Vector3 currentPosition = head.position;

                if (hasLastPosition)
                {
                    TotalDistance += Vector3.Distance(currentPosition, lastPosition);
                }

                lastPosition = currentPosition;
                hasLastPosition = true;
            }
            else
            {
                hasLastPosition = false;
            }

            yield return wait;
        }
    }

    /// <summary>
    /// Clears the accumulated distance and the last sampled position cache.
    /// </summary>
    public void ResetDistance()
    {
        TotalDistance = 0f;
        hasLastPosition = false;
    }

    /// <summary>
    /// Starts head-path accumulation.
    /// </summary>
    public void StartTracking()
    {
        Active = true;
        IsTracking = true;
    }

    /// <summary>
    /// Stops head-path accumulation.
    /// </summary>
    public void StopTracking()
    {
        Active = false;
        IsTracking = false;
    }
}
