using Microsoft.MixedReality.Toolkit;
using Microsoft.MixedReality.Toolkit.Input;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Renders the 2D vascular topology layout described in the paper.
/// This class is responsible for:
/// 1) loading the precomputed layout JSON,
/// 2) drawing Bezier edges on the 2D backplate,
/// 3) exposing linked-view events for the 3D vessel renderer,
/// 4) providing study-facing selection, undo, redo, and reset helpers.
///
/// This cleanup keeps the original workflow and public API mostly intact,
/// while removing debug residue and making the script easier to release.
/// </summary>
public class TreeLayoutBezierRenderer : MonoBehaviour, IMixedRealityFocusHandler, IMixedRealityPointerHandler
{
    #region Data Models and Events

    public static event Action<List<int>> OnHighlightNodes;
    public static event Action<List<int>> OnUnhighlightNodes;
    public static event Action OnRestoreAllLinesToInitialState;
    public static event Action OnAnySegmentFirstHover;
    public static event Action<bool> OnAnySegmentClicked;
    public static event Action OnTreeBuilt;

    [Serializable]
    public class NodeData
    {
        public string id;
        public float x;
        public float y;
        public string parentId;
        public int type;
        public float radius;
        public List<int> childs;
    }

    [Serializable]
    public class BezierCurve
    {
        public Vector2 source;
        public Vector2 target;
        public Vector2 control1;
        public Vector2 control2;
    }

    [Serializable]
    public class LayoutData
    {
        public List<NodeData> nodes;
        public List<BezierCurve> bezierCurves;
    }

    private struct ClickAction
    {
        public LineRenderer line;
        public bool toSelected;
    }

    private struct EndpointsLocal
    {
        public Vector3 src;
        public Vector3 dst;
    }

    private const int LineSampleCount = 20;
    private const int UidFactor = 1000000;
    private const string CurveLayerName = "CurveLayer";

    private const float InitialAlpha = 0.2f;
    private const float HoverAlpha = 1.0f;
    private const float SelectedAlpha = 1.0f;
    private const float HoverWidth = 0.012f;
    private const float WidthScale = 0.005f;
    private const float LineLocalZ = -0.8f;
    private const float EndpointLocalZOffset = 0.01f;

    [Header("Input")]
    [Tooltip("Layout JSON file name under StreamingAssets.")]
    [SerializeField] private string layoutJsonFileName = "treeLayoutforVesselviewer.json";

    [Header("Scene References")]
    public GameObject nodePrefab;
    public Transform nodeParent;
    public LineRenderer linePrefab;
    public Transform contentBackPlate;

    [Header("Rendering")]
    public bool drawNodes = false;
    public Color[] colors =
    {
        new Color(0.12f, 0.70f, 0.68f),
        new Color(0.22f, 0.10f, 0.80f),
        new Color(0.46f, 0.44f, 0.70f),
        new Color(0.85f, 0.37f, 0.00f),
        new Color(0.40f, 0.65f, 0.11f),
        new Color(0.90f, 0.67f, 0.00f),
        new Color(0.90f, 0.16f, 0.54f),
        new Color(0.65f, 0.46f, 0.11f)
    };

    [Header("Debug")]
    [Tooltip("Export the child-to-line mapping text file after the layout has been built.")]
    [SerializeField] private bool exportBranchMappingsOnBuild = false;

    public bool IsBuilt { get; private set; }

    private readonly Stack<ClickAction> undoStack = new Stack<ClickAction>();
    private readonly Stack<ClickAction> redoStack = new Stack<ClickAction>();

    private readonly Dictionary<int, LineRenderer> lineByChildId = new Dictionary<int, LineRenderer>();
    private readonly Dictionary<long, LineRenderer> lineByParentChild = new Dictionary<long, LineRenderer>();
    private readonly Dictionary<long, int> uidToCurveIndex = new Dictionary<long, int>();
    private readonly Dictionary<long, EndpointsLocal> uidToEndpointsLocal = new Dictionary<long, EndpointsLocal>();
    private readonly Dictionary<LineRenderer, float> initialWidths = new Dictionary<LineRenderer, float>();
    private readonly Dictionary<LineRenderer, float> initialAlphas = new Dictionary<LineRenderer, float>();
    private readonly Dictionary<LineRenderer, List<int>> lineToBranchNodeIds = new Dictionary<LineRenderer, List<int>>();

    private readonly List<LineRenderer> allLines = new List<LineRenderer>();
    private readonly HashSet<LineRenderer> clickedLines = new HashSet<LineRenderer>();

    private LayoutData layoutData;
    private LineRenderer previousHoveredLine;
    private bool firstHoverSent;
    private Vector3 dragStartPosition;

    private Vector3 backPlateSize;
    private float backPlateZ;
    private float minX;
    private float maxX;
    private float minY;
    private float maxY;
    private Vector3 layoutOffset;

    private static long MakeUid(int parent, int firstChild)
    {
        return ((long)parent * UidFactor) + firstChild;
    }

    private static long MakeParentChildKey(int parent, int child)
    {
        return ((long)parent * UidFactor) + child;
    }

    #endregion

    #region Unity Lifecycle

    private void Start()
    {
        BuildLayout();
    }

    private void OnEnable()
    {
        MixedRealityToolkit.InputSystem?.RegisterHandler<IMixedRealityPointerHandler>(this);
        MixedRealityToolkit.InputSystem?.RegisterHandler<IMixedRealityFocusHandler>(this);
    }

    private void OnDisable()
    {
        MixedRealityToolkit.InputSystem?.UnregisterHandler<IMixedRealityPointerHandler>(this);
        MixedRealityToolkit.InputSystem?.UnregisterHandler<IMixedRealityFocusHandler>(this);
    }

    #endregion

    #region Public API

    public bool TryGetLineByChildId(int childId, out LineRenderer line)
    {
        return lineByChildId.TryGetValue(childId, out line);
    }

    public bool TryGetLineByParentChild(int parentId, int childId, out LineRenderer line)
    {
        return lineByParentChild.TryGetValue(MakeParentChildKey(parentId, childId), out line);
    }

    public bool TryGetEndpointsLocalForUid(int uid, out Vector3 sourceLocal, out Vector3 targetLocal)
    {
        sourceLocal = default;
        targetLocal = default;

        if (uidToEndpointsLocal.TryGetValue(uid, out var cachedEndpoints))
        {
            sourceLocal = cachedEndpoints.src;
            targetLocal = cachedEndpoints.dst;
            return true;
        }

        if (uidToCurveIndex.TryGetValue(uid, out int curveIndex) && curveIndex >= 0 && curveIndex < allLines.Count)
        {
            LineRenderer line = allLines[curveIndex];
            if (line != null && line.positionCount >= 2)
            {
                sourceLocal = OffsetLocalEndpoint(line.GetPosition(0));
                targetLocal = OffsetLocalEndpoint(line.GetPosition(line.positionCount - 1));
                uidToEndpointsLocal[uid] = new EndpointsLocal { src = sourceLocal, dst = targetLocal };
                return true;
            }
        }

        int parent = uid / UidFactor;
        int firstChild = uid % UidFactor;
        if (lineByParentChild.TryGetValue(MakeParentChildKey(parent, firstChild), out LineRenderer fallbackLine) &&
            fallbackLine != null && fallbackLine.positionCount >= 2)
        {
            sourceLocal = OffsetLocalEndpoint(fallbackLine.GetPosition(0));
            targetLocal = OffsetLocalEndpoint(fallbackLine.GetPosition(fallbackLine.positionCount - 1));
            uidToEndpointsLocal[uid] = new EndpointsLocal { src = sourceLocal, dst = targetLocal };
            return true;
        }

        return false;
    }

    public bool TryGetLocalEndpointsForUid(int uid, out Vector3 sourceLocal, out Vector3 targetLocal)
    {
        return TryGetEndpointsLocalForUid(uid, out sourceLocal, out targetLocal);
    }

    public void ExportBranchMappingsToTxt()
    {
        var lines = new System.Text.StringBuilder();

        foreach (var entry in lineByChildId)
        {
            int childId = entry.Key;
            LineRenderer line = entry.Value;
            if (line == null || line.positionCount < 2)
            {
                continue;
            }

            Vector3 source = line.GetPosition(0);
            Vector3 target = line.GetPosition(line.positionCount - 1);
            lines.AppendLine($"Child ID: {childId}, Source (local): {source}, Destination (local): {target}");
        }

        string filePath = Path.Combine(Application.dataPath, "treeMappingsOutput.txt");
        File.WriteAllText(filePath, lines.ToString());
        Debug.Log($"[Tree] Exported branch mappings to: {filePath}");
    }

    public void UndoSelection()
    {
        if (undoStack.Count == 0)
        {
            return;
        }

        ClickAction action = undoStack.Pop();
        bool undoToSelected = !action.toSelected;

        ApplySelectionVisual(action.line, undoToSelected);
        if (action.toSelected)
        {
            UnhighlightSegment(action.line);
        }
        else
        {
            HighlightSegment(action.line);
        }

        redoStack.Push(action);
    }

    public void RedoSelection()
    {
        if (redoStack.Count == 0)
        {
            return;
        }

        ClickAction action = redoStack.Pop();

        ApplySelectionVisual(action.line, action.toSelected);
        if (action.toSelected)
        {
            HighlightSegment(action.line);
        }
        else
        {
            UnhighlightSegment(action.line);
        }

        undoStack.Push(action);
    }

    public void ResetSelectionFromOutside()
    {
        RestoreAllLinesToInitialState();
        clickedLines.Clear();
        undoStack.Clear();
        redoStack.Clear();
        firstHoverSent = false;
    }

    #endregion

    #region MRTK Event Handlers

    public void OnFocusEnter(FocusEventData eventData)
    {
        if (!TryGetLineFromPointerTarget(eventData.Pointer?.Result?.CurrentPointerTarget, out LineRenderer line))
        {
            return;
        }

        if (!clickedLines.Contains(line))
        {
            SetLineAlpha(line, HoverAlpha);
            line.widthMultiplier = HoverWidth;
            previousHoveredLine = line;
        }

        if (!firstHoverSent)
        {
            firstHoverSent = true;
            OnAnySegmentFirstHover?.Invoke();
        }
    }

    public void OnFocusExit(FocusEventData eventData)
    {
        if (previousHoveredLine != null && !clickedLines.Contains(previousHoveredLine))
        {
            SetLineAlpha(previousHoveredLine, GetInitialAlpha(previousHoveredLine));
            previousHoveredLine.widthMultiplier = GetInitialWidth(previousHoveredLine);
            previousHoveredLine = null;
        }
    }

    public void OnPointerClicked(MixedRealityPointerEventData eventData)
    {
        if (!TryGetLineFromPointerTarget(eventData.Pointer?.Result?.CurrentPointerTarget, out LineRenderer line))
        {
            return;
        }

        bool toSelected = !clickedLines.Contains(line);
        ApplySelectionVisual(line, toSelected);

        if (toSelected)
        {
            HighlightSegment(line);
        }
        else
        {
            UnhighlightSegment(line);
        }

        undoStack.Push(new ClickAction { line = line, toSelected = toSelected });
        redoStack.Clear();
        OnAnySegmentClicked?.Invoke(toSelected);
    }

    public void OnPointerDragged(MixedRealityPointerEventData eventData)
    {
        // Intentionally left lightweight. Drag-based clearing was removed from the release version
        // because selection state is managed explicitly through click, undo, redo, and reset.
    }

    public void OnPointerUp(MixedRealityPointerEventData eventData)
    {
        dragStartPosition = Vector3.zero;
    }

    public void OnPointerDown(MixedRealityPointerEventData eventData)
    {
        dragStartPosition = eventData.Pointer.Position;
    }

    #endregion

    #region Layout Build Pipeline

    private void BuildLayout()
    {
        if (!ValidateReferences())
        {
            return;
        }

        string filePath = Path.Combine(Application.streamingAssetsPath, layoutJsonFileName);
        if (!File.Exists(filePath))
        {
            Debug.LogError($"[Tree] Layout JSON not found: {filePath}");
            return;
        }

        string json = File.ReadAllText(filePath);
        layoutData = JsonUtility.FromJson<LayoutData>(json);
        if (layoutData == null)
        {
            Debug.LogError($"[Tree] Failed to parse layout JSON: {filePath}");
            return;
        }

        layoutData.nodes = layoutData.nodes ?? new List<NodeData>();
        layoutData.bezierCurves = layoutData.bezierCurves ?? new List<BezierCurve>();

        CacheBackPlateMetrics();

        if (drawNodes)
        {
            BuildDebugNodes();
        }

        for (int lineIndex = 0; lineIndex < layoutData.bezierCurves.Count; lineIndex++)
        {
            BezierCurve curve = layoutData.bezierCurves[lineIndex];
            LineRenderer line = CreateCurveLine(curve, lineIndex);

            NodeData sourceNode = FindNodeByPos(curve.source);
            NodeData targetNode = FindNodeByPos(curve.target);

            ApplyLineStyle(line, sourceNode, targetNode);
            SetLineAlpha(line, InitialAlpha);
            initialAlphas[line] = InitialAlpha;

            CacheBranchNodeIds(lineIndex, line);
            AddCurveCollider(line);
            SetCurveLayer(line.gameObject);
            allLines.Add(line);
            CacheCurveMappings(line, sourceNode, targetNode, lineIndex);
        }

        if (exportBranchMappingsOnBuild)
        {
            ExportBranchMappingsToTxt();
        }

        IsBuilt = true;
        OnTreeBuilt?.Invoke();

        Debug.Log($"[Tree] Built. nodes={layoutData.nodes.Count}, curves={layoutData.bezierCurves.Count}, childMap={lineByChildId.Count}, uidMap={uidToCurveIndex.Count}");
    }

    private bool ValidateReferences()
    {
        if (linePrefab == null)
        {
            Debug.LogError("[Tree] Line prefab is missing.");
            return false;
        }

        if (contentBackPlate == null)
        {
            Debug.LogError("[Tree] Content backplate is missing.");
            return false;
        }

        Renderer renderer = contentBackPlate.GetComponent<Renderer>();
        if (renderer == null)
        {
            Debug.LogError("[Tree] Content backplate requires a Renderer.");
            return false;
        }

        return true;
    }

    private void CacheBackPlateMetrics()
    {
        Renderer renderer = contentBackPlate.GetComponent<Renderer>();
        backPlateSize = renderer.bounds.size;
        backPlateZ = contentBackPlate.position.z;

        minX = float.PositiveInfinity;
        maxX = float.NegativeInfinity;
        minY = float.PositiveInfinity;
        maxY = float.NegativeInfinity;

        foreach (NodeData node in layoutData.nodes)
        {
            minX = Mathf.Min(minX, node.x);
            maxX = Mathf.Max(maxX, node.x);
            minY = Mathf.Min(minY, node.y);
            maxY = Mathf.Max(maxY, node.y);
        }

        if (layoutData.nodes.Count == 0)
        {
            minX = maxX = 0f;
            minY = maxY = 0f;
        }

        // Kept from the original scene calibration. Consider moving this into inspector settings later.
        Vector3 backPlateCenter = contentBackPlate.position;
        layoutOffset = backPlateCenter - new Vector3(0.18f, -0.21f, backPlateZ);
    }

    private void BuildDebugNodes()
    {
        if (nodePrefab == null || nodeParent == null)
        {
            Debug.LogWarning("[Tree] drawNodes is enabled, but nodePrefab or nodeParent is missing.");
            return;
        }

        foreach (NodeData node in layoutData.nodes)
        {
            Vector3 worldPos = ConvertToWorldPosition(new Vector2(node.x, node.y));
            GameObject nodeObject = Instantiate(nodePrefab, nodeParent);
            nodeObject.transform.position = worldPos;
            nodeObject.transform.localScale = Vector3.one * 0.01f;
            nodeObject.name = node.id;
        }
    }

    private LineRenderer CreateCurveLine(BezierCurve curve, int lineIndex)
    {
        LineRenderer line = Instantiate(linePrefab, contentBackPlate);
        line.transform.SetParent(contentBackPlate, false);
        line.useWorldSpace = false;
        line.positionCount = LineSampleCount;
        line.name = lineIndex.ToString();
        line.alignment = LineAlignment.TransformZ;

        for (int i = 0; i < LineSampleCount; i++)
        {
            float t = i / (float)(LineSampleCount - 1);
            Vector2 bezierPoint = CalculateCubicBezierPoint(t, curve.source, curve.control1, curve.control2, curve.target);
            Vector3 worldPos = ConvertToWorldPosition(bezierPoint);
            worldPos.z = backPlateZ;

            Vector3 localPos = contentBackPlate.InverseTransformPoint(worldPos);
            localPos.z = LineLocalZ;
            line.SetPosition(i, localPos);
        }

        return line;
    }

    private void ApplyLineStyle(LineRenderer line, NodeData sourceNode, NodeData targetNode)
    {
        if (sourceNode != null && targetNode != null)
        {
            float widthMultiplier = Mathf.Sqrt(Mathf.Max(targetNode.radius, 0f)) * WidthScale;
            line.widthMultiplier = widthMultiplier;
            initialWidths[line] = widthMultiplier;

            Color lineColor = GetLineColorByType(sourceNode.type);
            line.startColor = lineColor;
            line.endColor = lineColor;
        }
        else
        {
            initialWidths[line] = line.widthMultiplier;
        }
    }

    private void CacheBranchNodeIds(int lineIndex, LineRenderer line)
    {
        int nodeIndex = lineIndex + 1;
        if (nodeIndex >= 0 && nodeIndex < layoutData.nodes.Count && layoutData.nodes[nodeIndex] != null)
        {
            List<int> childIds = layoutData.nodes[nodeIndex].childs;
            if (childIds != null && childIds.Count > 0)
            {
                lineToBranchNodeIds[line] = new List<int>(childIds);
                return;
            }
        }

        lineToBranchNodeIds[line] = new List<int>();
    }

    private void CacheCurveMappings(LineRenderer line, NodeData sourceNode, NodeData targetNode, int lineIndex)
    {
        if (sourceNode == null || targetNode == null || targetNode.childs == null)
        {
            return;
        }

        if (!int.TryParse(sourceNode.id, out int childId) || !int.TryParse(targetNode.id, out int parentId))
        {
            return;
        }

        if (!targetNode.childs.Contains(childId))
        {
            return;
        }

        lineByChildId[childId] = line;
        lineByParentChild[MakeParentChildKey(parentId, childId)] = line;

        if (targetNode.childs.Count > 0 && targetNode.childs[0] == childId)
        {
            long uid = MakeUid(parentId, childId);
            uidToCurveIndex[uid] = lineIndex;

            if (line.positionCount >= 2)
            {
                uidToEndpointsLocal[uid] = new EndpointsLocal
                {
                    src = OffsetLocalEndpoint(line.GetPosition(0)),
                    dst = OffsetLocalEndpoint(line.GetPosition(line.positionCount - 1))
                };
            }
        }
    }

    private Vector3 ConvertToWorldPosition(Vector2 logicalPoint)
    {
        float xRange = Mathf.Max(maxX - minX, Mathf.Epsilon);
        float yRange = Mathf.Max(maxY - minY, Mathf.Epsilon);

        float normalizedX = (logicalPoint.x - minX) / xRange;
        float normalizedY = (logicalPoint.y - minY) / yRange;

        float mappedX = normalizedX * backPlateSize.x * 0.7f;
        float mappedY = normalizedY * backPlateSize.y * 0.9f;
        return new Vector3(mappedX, -mappedY, backPlateZ) + layoutOffset;
    }

    private static Vector3 OffsetLocalEndpoint(Vector3 point)
    {
        point.z += EndpointLocalZOffset;
        return point;
    }

    private void SetLineAlpha(LineRenderer line, float alpha)
    {
        Color startColor = line.startColor;
        Color endColor = line.endColor;

        startColor.a = alpha;
        endColor.a = alpha;

        line.startColor = startColor;
        line.endColor = endColor;
    }

    private Color GetLineColorByType(int type)
    {
        if (type >= 0 && type < colors.Length)
        {
            return colors[type];
        }

        return Color.black;
    }

    private void AddCurveCollider(LineRenderer line)
    {
        GameObject colliderObject = new GameObject("Collider");
        colliderObject.transform.SetParent(line.transform, false);
        SetCurveLayer(colliderObject);

        BoxCollider collider = colliderObject.AddComponent<BoxCollider>();
        collider.isTrigger = true;

        List<Vector3> worldPoints = new List<Vector3>(line.positionCount);
        for (int i = 0; i < line.positionCount; i++)
        {
            Vector3 localPoint = line.GetPosition(i);
            Vector3 worldPoint = contentBackPlate.TransformPoint(localPoint);
            worldPoints.Add(worldPoint);
        }

        Vector3 min = Vector3.positiveInfinity;
        Vector3 max = Vector3.negativeInfinity;
        foreach (Vector3 point in worldPoints)
        {
            min = Vector3.Min(min, point);
            max = Vector3.Max(max, point);
        }

        Vector3 size = max - min;
        Vector3 center = (max + min) * 0.5f;

        size.z = 0.01f;
        size.x += line.startWidth;
        size.y += line.startWidth;

        collider.size = size;
        colliderObject.transform.position = center;
        colliderObject.transform.rotation = line.transform.rotation;
        collider.center = new Vector3(0f, 0f, -size.z * 0.5f);
    }

    #endregion

    #region Selection and Linked-View Synchronization

    private void RestoreAllLinesToInitialState()
    {
        foreach (LineRenderer line in allLines)
        {
            SetLineAlpha(line, GetInitialAlpha(line));
            line.widthMultiplier = GetInitialWidth(line);
        }

        previousHoveredLine = null;
        OnRestoreAllLinesToInitialState?.Invoke();
    }

    private void ApplySelectionVisual(LineRenderer line, bool toSelected)
    {
        if (line == null)
        {
            return;
        }

        if (toSelected)
        {
            clickedLines.Add(line);
            SetLineAlpha(line, SelectedAlpha);
            line.widthMultiplier = GetInitialWidth(line);
        }
        else
        {
            clickedLines.Remove(line);
            SetLineAlpha(line, GetInitialAlpha(line));
            line.widthMultiplier = GetInitialWidth(line);
        }
    }

    private void HighlightSegment(LineRenderer line)
    {
        List<int> nodeIds = GetBranchNodeIdsForLine(line);
        if (nodeIds != null && nodeIds.Count > 0)
        {
            OnHighlightNodes?.Invoke(nodeIds);
        }
    }

    private void UnhighlightSegment(LineRenderer line)
    {
        List<int> nodeIds = GetBranchNodeIdsForLine(line);
        if (nodeIds != null && nodeIds.Count > 0)
        {
            OnUnhighlightNodes?.Invoke(nodeIds);
        }
    }

    private List<int> GetBranchNodeIdsForLine(LineRenderer line)
    {
        if (line == null)
        {
            return null;
        }

        if (lineToBranchNodeIds.TryGetValue(line, out List<int> cachedIds) && cachedIds != null && cachedIds.Count > 0)
        {
            return new List<int>(cachedIds);
        }

        if (int.TryParse(line.name, out int lineIndex))
        {
            int nodeIndex = lineIndex + 1;
            if (nodeIndex >= 0 && nodeIndex < layoutData.nodes.Count)
            {
                List<int> fallbackIds = layoutData.nodes[nodeIndex].childs;
                if (fallbackIds != null && fallbackIds.Count > 0)
                {
                    return new List<int>(fallbackIds);
                }
            }
        }

        return null;
    }

    private float GetInitialWidth(LineRenderer line)
    {
        if (line != null && initialWidths.TryGetValue(line, out float width))
        {
            return width;
        }

        return line != null ? line.widthMultiplier : 0f;
    }

    private float GetInitialAlpha(LineRenderer line)
    {
        if (line != null && initialAlphas.TryGetValue(line, out float alpha))
        {
            return alpha;
        }

        return InitialAlpha;
    }

    #endregion

    #region Utility Methods

    private NodeData FindNodeByPos(Vector2 pos, float epsilon = 0.0001f)
    {
        foreach (NodeData node in layoutData.nodes)
        {
            if (Mathf.Abs(node.x - pos.x) < epsilon && Mathf.Abs(node.y - pos.y) < epsilon)
            {
                return node;
            }
        }

        return null;
    }

    private bool TryGetLineFromPointerTarget(GameObject pointerTarget, out LineRenderer line)
    {
        line = null;
        if (pointerTarget == null)
        {
            return false;
        }

        Collider collider = pointerTarget.GetComponent<Collider>();
        if (collider == null)
        {
            return false;
        }

        line = collider.GetComponentInParent<LineRenderer>();
        return line != null;
    }

    private void SetCurveLayer(GameObject target)
    {
        int curveLayer = LayerMask.NameToLayer(CurveLayerName);
        if (curveLayer >= 0)
        {
            target.layer = curveLayer;
        }
    }

    private static Vector2 CalculateCubicBezierPoint(float t, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3)
    {
        if (Mathf.Approximately(p0.x, p3.x) && Mathf.Approximately(p1.x, p2.x))
        {
            float y = Mathf.Lerp(p0.y, p3.y, t);
            return new Vector2(p0.x, y);
        }

        float u = 1f - t;
        float tt = t * t;
        float uu = u * u;
        float uuu = uu * u;
        float ttt = tt * t;

        Vector2 point = uuu * p0;
        point += 3f * uu * t * p1;
        point += 3f * u * tt * p2;
        point += ttt * p3;
        return point;
    }

    #endregion
}
