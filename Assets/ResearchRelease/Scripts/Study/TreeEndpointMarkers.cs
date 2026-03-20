using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Optional per-task override for manually placing 2D start and end markers.
/// Positions are expressed in ContentBackPlate local space.
/// </summary>
[System.Serializable]
public class TaskOverride
{
    [Tooltip("Whether to manually place the start marker for this task.")]
    public bool placeStart = false;

    [Tooltip("Start marker position in ContentBackPlate local space.")]
    public Vector3 startLocal;

    [Tooltip("Whether to manually place the end marker for this task.")]
    public bool placeEnd = false;

    [Tooltip("End marker position in ContentBackPlate local space.")]
    public Vector3 endLocal;
}

/// <summary>
/// Places 2D start/end markers on the topology backplate for the current task.
///
/// The component supports two strategies:
/// 1. Manual per-task overrides stored in the Inspector.
/// 2. Automatic endpoint lookup from TreeLayoutBezierRenderer.
///
/// If the tree is not yet built when a task is loaded, placement is retried for a short period.
/// This cleanup preserves the original public API and runtime behavior used by StudyController.
/// </summary>
public class TreeEndpointMarkers : MonoBehaviour
{
    [Header("References")]
    public TreeLayoutBezierRenderer tree;
    public Transform contentBackPlate;
    public GameObject start2DPrefab;
    public GameObject end2DPrefab;

    [Header("Appearance")]
    [Tooltip("Marker diameter in ContentBackPlate local space.")]
    public float markerLocalDiameter = 0.018f;

    [Header("Manual Overrides (Per Task)")]
    [Tooltip("Should match the StudyController task count. If a task enables placeStart/placeEnd, the local coordinates below are used instead of auto placement.")]
    public List<TaskOverride> manualPerTask = new List<TaskOverride>();

    [Header("Debug")]
    public bool verboseLogs = true;
    public int maxRetryFrames = 60;

    private readonly List<GameObject> spawnedMarkers = new List<GameObject>();
    private List<int> pendingUids;
    private int pendingTaskIndex = -1;
    private Coroutine retryRoutine;

    private void OnEnable()
    {
        TreeLayoutBezierRenderer.OnTreeBuilt += OnTreeBuilt;
    }

    private void OnDisable()
    {
        TreeLayoutBezierRenderer.OnTreeBuilt -= OnTreeBuilt;
        Clear();
    }

    private void OnTreeBuilt()
    {
        if (verboseLogs)
        {
            Debug.Log("[Tree2D] OnTreeBuilt received.");
        }

        if (pendingUids != null && pendingUids.Count > 0)
        {
            TryPlaceForTask(pendingUids, pendingTaskIndex);
        }
    }

    /// <summary>
    /// Expands the override list so it can safely index the specified task count.
    /// </summary>
    public void EnsureManualSize(int taskCount)
    {
        while (manualPerTask.Count < taskCount)
        {
            manualPerTask.Add(new TaskOverride());
        }
    }

    /// <summary>
    /// Displays 2D task markers for the provided ordered branch UID list.
    /// </summary>
    public void ShowForTask(IList<int> branchUids, int taskIndex)
    {
        if (verboseLogs)
        {
            int count = branchUids == null ? -1 : branchUids.Count;
            Debug.Log($"[Tree2D] ShowForTask uids={count} taskIndex={taskIndex}");
        }

        Clear();

        if (tree == null || contentBackPlate == null || branchUids == null || branchUids.Count == 0)
        {
            if (verboseLogs)
            {
                Debug.LogWarning("[Tree2D] Abort: missing references or empty branch list.");
            }
            return;
        }

        pendingUids = new List<int>(branchUids);
        pendingTaskIndex = taskIndex;

        if (!tree.IsBuilt)
        {
            StartRetry();
            return;
        }

        TryPlaceForTask(pendingUids, pendingTaskIndex);
    }

    /// <summary>
    /// Removes all spawned 2D markers and stops any retry coroutine.
    /// </summary>
    public void Clear()
    {
        if (retryRoutine != null)
        {
            StopCoroutine(retryRoutine);
            retryRoutine = null;
        }

        for (int i = 0; i < spawnedMarkers.Count; i++)
        {
            if (spawnedMarkers[i] != null)
            {
                Destroy(spawnedMarkers[i]);
            }
        }

        spawnedMarkers.Clear();
    }

    private void StartRetry()
    {
        if (retryRoutine != null)
        {
            StopCoroutine(retryRoutine);
        }

        retryRoutine = StartCoroutine(RetryUntilReady());
    }

    private IEnumerator RetryUntilReady()
    {
        int frames = 0;
        while (frames < maxRetryFrames)
        {
            frames++;

            if (tree != null && tree.IsBuilt)
            {
                break;
            }

            yield return null;
        }

        retryRoutine = null;

        if (tree != null && tree.IsBuilt && pendingUids != null && pendingUids.Count > 0)
        {
            TryPlaceForTask(pendingUids, pendingTaskIndex);
        }
    }

    private void TryPlaceForTask(List<int> uids, int taskIndex)
    {
        if (TryPlaceManualOverride(taskIndex))
        {
            return;
        }

        int firstUid = uids[0];
        int lastUid = uids[uids.Count - 1];

        bool startPlaced = TryPlaceStart(firstUid);
        bool endPlaced = TryPlaceEnd(firstUid, lastUid);

        if ((!startPlaced || !endPlaced) && retryRoutine == null)
        {
            if (verboseLogs)
            {
                Debug.Log("[Tree2D] Endpoints not ready; scheduling a short retry.");
            }

            retryRoutine = StartCoroutine(DelayedRecheck(10));
        }
    }

    private bool TryPlaceManualOverride(int taskIndex)
    {
        if (taskIndex < 0 || taskIndex >= manualPerTask.Count)
        {
            return false;
        }

        TaskOverride taskOverride = manualPerTask[taskIndex];
        bool usedAnyOverride = false;

        if (taskOverride.placeStart)
        {
            SpawnMarkerAtLocal(taskOverride.startLocal, start2DPrefab, $"TreeStart_T{taskIndex}");
            usedAnyOverride = true;
        }

        if (taskOverride.placeEnd)
        {
            SpawnMarkerAtLocal(taskOverride.endLocal, end2DPrefab, $"TreeEnd_T{taskIndex}");
            usedAnyOverride = true;
        }

        if (usedAnyOverride && verboseLogs)
        {
            Debug.Log($"[Tree2D] Used manual override for task #{taskIndex}.");
        }

        return usedAnyOverride;
    }

    private bool TryPlaceStart(int firstUid)
    {
        if (tree.TryGetEndpointsLocalForUid(firstUid, out Vector3 sourceLocal, out _))
        {
            SpawnMarkerAtLocal(sourceLocal, start2DPrefab, $"TreeStart_{firstUid}");
            return true;
        }

        if (verboseLogs)
        {
            Debug.LogWarning($"[Tree2D] Start UID {firstUid}: no endpoints available.");
        }

        return false;
    }

    private bool TryPlaceEnd(int firstUid, int lastUid)
    {
        if (lastUid == firstUid)
        {
            if (verboseLogs)
            {
                Debug.Log("[Tree2D] Single-UID task: only the start marker is required.");
            }
            return true;
        }

        if (tree.TryGetEndpointsLocalForUid(lastUid, out _, out Vector3 targetLocal))
        {
            SpawnMarkerAtLocal(targetLocal, end2DPrefab, $"TreeEnd_{lastUid}");
            return true;
        }

        if (verboseLogs)
        {
            Debug.LogWarning($"[Tree2D] End UID {lastUid}: no endpoints available.");
        }

        return false;
    }

    private IEnumerator DelayedRecheck(int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            yield return null;
        }

        retryRoutine = null;

        if (pendingUids != null && pendingUids.Count > 0)
        {
            TryPlaceForTask(pendingUids, pendingTaskIndex);
        }
    }

    private void SpawnMarkerAtLocal(Vector3 localPos, GameObject prefab, string objectName)
    {
        if (prefab == null)
        {
            Debug.LogWarning("[Tree2D] Marker prefab is missing.");
            return;
        }

        GameObject marker = Instantiate(prefab, contentBackPlate, false);
        marker.name = objectName;
        marker.transform.localPosition = localPos;
        marker.transform.localRotation = Quaternion.identity;
        marker.transform.localScale = Vector3.one * markerLocalDiameter;
        marker.layer = contentBackPlate.gameObject.layer;

        spawnedMarkers.Add(marker);

        if (verboseLogs)
        {
            Debug.Log($"[Tree2D] Spawned {objectName} at {localPos} under {contentBackPlate.name}.");
        }
    }

    [ContextMenu("Debug/Capture auto endpoints into manual for pending task")]
    private void CaptureAutoEndpointsIntoManual()
    {
        if (pendingUids == null || pendingUids.Count == 0 || pendingTaskIndex < 0)
        {
            Debug.LogWarning("[Tree2D] No pending task available.");
            return;
        }

        EnsureManualSize(pendingTaskIndex + 1);
        TaskOverride taskOverride = manualPerTask[pendingTaskIndex];

        if (tree.TryGetEndpointsLocalForUid(pendingUids[0], out Vector3 startLocal, out _))
        {
            taskOverride.placeStart = true;
            taskOverride.startLocal = startLocal;
        }

        if (tree.TryGetEndpointsLocalForUid(pendingUids[pendingUids.Count - 1], out _, out Vector3 endLocal))
        {
            taskOverride.placeEnd = true;
            taskOverride.endLocal = endLocal;
        }

        manualPerTask[pendingTaskIndex] = taskOverride;

        Debug.Log($"[Tree2D] Captured auto endpoints into manual override for task #{pendingTaskIndex}: start={taskOverride.startLocal}, end={taskOverride.endLocal}");
    }
}
