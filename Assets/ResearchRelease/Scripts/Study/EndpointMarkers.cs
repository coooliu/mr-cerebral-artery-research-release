using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns 3D start and end markers for the current study task.
///
/// This helper queries the branch path from VesselViewer using the task UIDs,
/// resolves the terminal node for the first and last branch, and attaches the marker prefabs
/// to the corresponding sphere objects in the vessel model.
///
/// The cleanup preserves the original external API so it remains compatible with
/// StudyController and the existing Unity scene bindings.
/// </summary>
public class EndpointMarkers : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Vessel viewer used to resolve branch paths and node spheres.")]
    public VesselViewer vessel;

    [Tooltip("Prefab used for the start landmark.")]
    public GameObject startMarkerPrefab;

    [Tooltip("Prefab used for the end landmark.")]
    public GameObject endMarkerPrefab;

    [Header("Appearance")]
    [Tooltip("Desired marker diameter in world space, independent of the host sphere size.")]
    public float markerWorldDiameter = 0.04f;

    // Runtime cache: nodeId -> spawned marker instance.
    private readonly Dictionary<int, GameObject> activeMarkers = new Dictionary<int, GameObject>();

    /// <summary>
    /// Displays the task start/end markers for an ordered list of branch UIDs.
    /// </summary>
    public void ShowForTask(IList<int> branchUids)
    {
        Clear();

        if (vessel == null || branchUids == null || branchUids.Count == 0)
        {
            return;
        }

        int firstUid = branchUids[0];
        int lastUid = branchUids[branchUids.Count - 1];

        int startNodeId = GetEndNodeFromUid(firstUid);
        if (startNodeId != -1)
        {
            SpawnMarker(startNodeId, startMarkerPrefab, "Start");
        }

        if (lastUid != firstUid)
        {
            int endNodeId = GetEndNodeFromUid(lastUid);
            if (endNodeId != -1)
            {
                SpawnMarker(endNodeId, endMarkerPrefab, "End");
            }
        }
    }

    /// <summary>
    /// Removes all currently spawned markers.
    /// </summary>
    public void Clear()
    {
        foreach (KeyValuePair<int, GameObject> pair in activeMarkers)
        {
            if (pair.Value != null)
            {
                Destroy(pair.Value);
            }
        }

        activeMarkers.Clear();
    }

    private void OnDisable()
    {
        Clear();
    }

    /// <summary>
    /// Resolves the terminal node of the branch represented by a UID.
    /// </summary>
    private int GetEndNodeFromUid(int uid)
    {
        List<int> branch = vessel.GetBranchNodesByUid(uid);
        if (branch == null || branch.Count == 0)
        {
            return -1;
        }

        return branch[branch.Count - 1];
    }

    /// <summary>
    /// Instantiates a marker under the sphere representing the specified node.
    /// </summary>
    private void SpawnMarker(int nodeId, GameObject prefab, string role)
    {
        GameObject hostSphere = vessel.GetSphereGO(nodeId);
        if (hostSphere == null || prefab == null)
        {
            return;
        }

        GameObject marker = Instantiate(prefab, hostSphere.transform);
        marker.name = $"EndpointMarker_{role}_{nodeId}";
        marker.transform.localPosition = Vector3.zero;
        marker.transform.localRotation = Quaternion.identity;

        float hostWorldDiameter = GetHostWorldDiameter(hostSphere);
        float scaleFactor = hostWorldDiameter > 0f ? markerWorldDiameter / hostWorldDiameter : 1f;
        marker.transform.localScale = Vector3.one * scaleFactor;
        marker.layer = hostSphere.layer;

        activeMarkers[nodeId] = marker;
    }

    private static float GetHostWorldDiameter(GameObject hostSphere)
    {
        Renderer renderer = hostSphere.GetComponent<Renderer>();
        return renderer != null ? renderer.bounds.size.x : 0.01f;
    }
}
