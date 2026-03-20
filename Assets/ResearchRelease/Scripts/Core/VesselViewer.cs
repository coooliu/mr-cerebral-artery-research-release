using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.MixedReality.Toolkit;
using Microsoft.MixedReality.Toolkit.Input;
using Microsoft.MixedReality.Toolkit.UI;
using UnityEngine;

/// <summary>
/// VesselViewer is the 3D vessel renderer used in the research release.
///
/// Responsibilities:
/// 1. Parse centerline data from an SWC file.
/// 2. Build an explicit 3D vessel model using spheres and frustums.
/// 3. Support segment / branch level hover and selection in MRTK.
/// 4. Synchronize branch-level state with the 2D topology renderer.
///
/// Notes:
/// - This script is intentionally kept close to the original implementation.
/// - The goal of this release version is readability and reproducibility,
///   not a full architectural rewrite.
/// - Resource names are preserved to minimize scene migration work.
/// </summary>
public class VesselViewer : MonoBehaviour, IMixedRealityPointerHandler, IMixedRealityFocusHandler
{
    #region Types

    public enum InteractionMode
    {
        Single,
        Branch
    }

    public enum DefaultColorMode
    {
        Gray,
        TypeColor
    }

    [Serializable]
    public class NodeData
    {
        public int id;
        public List<int> childs;
    }

    [Serializable]
    public class TreeLayoutData
    {
        public List<NodeData> nodes;
    }

    public class SWCNode
    {
        public int id;
        public int type;
        public Vector3 position;
        public float radius;
        public int parent;

        public GameObject associatedGameObject;
        public GameObject outlineShell;
    }

    #endregion

    #region Inspector Fields

    public InteractionMode interactionMode = InteractionMode.Branch;
    public DefaultColorMode defaultColorMode = DefaultColorMode.Gray;

    public bool allowManipulations = true;
    public bool showCones = true;
    public int centerNode = 1;

    public LayerMask selectableLayer;

    public GameObject neuronContainer;

    public Material defaultMaterial;
    public Material selectedMaterial;
    public Material transparentMaterial;
    public Material outlineMaterial;

    public Color[] colors =
    {
        new Color(0.90f, 0.16f, 0.54f),
        new Color(0.90f, 0.67f, 0.00f),
        new Color(0.12f, 0.70f, 0.68f),
        new Color(0.65f, 0.46f, 0.11f),
        new Color(0.40f, 0.65f, 0.11f),
        new Color(0.46f, 0.44f, 0.70f),
        new Color(0.22f, 0.10f, 0.80f),
        new Color(0.85f, 0.37f, 0.00f),
        new Color(0.8745f, 0.00f, 0.00f)
    };

    #endregion

    #region Public Events / State

    /// <summary>
    /// Fired when a full branch is toggled. Parameters: (branchUid, isSelected).
    /// </summary>
    public event Action<int, bool> OnBranchToggled;

    /// <summary>
    /// Fired when a branch is hovered. Parameter: branchUid.
    /// </summary>
    public event Action<int> OnBranchHovered;

    public Dictionary<int, SWCNode> swc = new Dictionary<int, SWCNode>();

    #endregion

    #region Private Fields

    private const int UID_FACTOR = 1_000_000;

    private readonly Dictionary<int, List<int>> branchMappings = new Dictionary<int, List<int>>();
    private readonly Dictionary<int, List<int>> parentMappings = new Dictionary<int, List<int>>();
    private readonly Dictionary<GameObject, Material> originalMaterials = new Dictionary<GameObject, Material>();
    private readonly Dictionary<GameObject, bool> selectionStates = new Dictionary<GameObject, bool>();
    private readonly Dictionary<int, Material> transparentByType = new Dictionary<int, Material>();

    private ObjectManipulator manipulator;
    private Collider boundingBoxCollider;

    private bool manipulationEnabled = true;
    private float lastClickTime;
    private const float ClickCooldown = 0.2f;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        int selectableLayerId = LayerMask.NameToLayer("Selectable");
        selectableLayer = selectableLayerId >= 0 ? (1 << selectableLayerId) : 0;
    }

    private void OnEnable()
    {
        CoreServices.InputSystem?.RegisterHandler<IMixedRealityPointerHandler>(this);
        CoreServices.InputSystem?.RegisterHandler<IMixedRealityFocusHandler>(this);

        TreeLayoutBezierRenderer.OnHighlightNodes += HighlightNodes;
        TreeLayoutBezierRenderer.OnUnhighlightNodes += UnhighlightNodes;
        TreeLayoutBezierRenderer.OnRestoreAllLinesToInitialState += resetAllNodes;
    }

    private void OnDisable()
    {
        CoreServices.InputSystem?.UnregisterHandler<IMixedRealityPointerHandler>(this);
        CoreServices.InputSystem?.UnregisterHandler<IMixedRealityFocusHandler>(this);

        TreeLayoutBezierRenderer.OnHighlightNodes -= HighlightNodes;
        TreeLayoutBezierRenderer.OnUnhighlightNodes -= UnhighlightNodes;
        TreeLayoutBezierRenderer.OnRestoreAllLinesToInitialState -= resetAllNodes;
    }

    private void Start()
    {
        CreateDefaultMaterials();
        LoadInitialNeuronStructure();
        LoadBranchMappings();
        ExportBranchMappingsToTxt();
    }

    #endregion

    #region Initialization

    private void LoadInitialNeuronStructure()
    {
        TextAsset swcFile = Resources.Load<TextAsset>("demo_case");
        if (swcFile == null)
        {
            Debug.LogError("[VesselViewer] SWC file not found: #easyBA_rotated_adjusted");
            return;
        }

        ParseSWC(swcFile.text);
        Init();
    }

    public void Init()
    {
        if (neuronContainer != null)
        {
            Destroy(neuronContainer);
        }

        neuronContainer = new GameObject("VesselContainer");
        neuronContainer.transform.SetParent(transform, false);

        if (allowManipulations)
        {
            AddManipulationHandler(neuronContainer);
        }

        foreach (var pair in swc)
        {
            SWCNode node = pair.Value;
            GameObject sphere = GenerateSphere(node);
            sphere.transform.SetParent(neuronContainer.transform, true);

            if (!showCones || node.parent == -1 || !swc.ContainsKey(node.parent))
            {
                continue;
            }

            SWCNode parentNode = swc[node.parent];
            GameObject cone = GenerateCone(node, parentNode);
            cone.transform.SetParent(neuronContainer.transform, true);
        }

        AdjustPositionAndScale();
        CreateBoundingBox();
    }

    private void AddManipulationHandler(GameObject target)
    {
        manipulator = target.GetComponent<ObjectManipulator>();
        if (manipulator == null)
        {
            manipulator = target.AddComponent<ObjectManipulator>();
        }

        manipulator.SmoothingActive = true;
        manipulator.MoveLerpTime = 0.1f;
        manipulator.RotateLerpTime = 0.1f;
        manipulator.ScaleLerpTime = 0.1f;
    }

    private void AdjustPositionAndScale()
    {
        // Scene-specific placement preserved from the original prototype.
        neuronContainer.transform.position = new Vector3(-0.3f, 1.065f, 0.17f);
    }

    private void CreateBoundingBox()
    {
        Bounds bounds = new Bounds(neuronContainer.transform.position, Vector3.zero);

        foreach (Transform child in neuronContainer.transform)
        {
            Renderer childRenderer = child.GetComponent<Renderer>();
            if (childRenderer != null)
            {
                bounds.Encapsulate(childRenderer.bounds);
            }
        }

        GameObject boundingBox = new GameObject("BoundingBox");
        boundingBox.transform.SetParent(neuronContainer.transform, true);
        boundingBox.transform.position = bounds.center;

        BoxCollider boxCollider = boundingBox.AddComponent<BoxCollider>();
        boxCollider.center = Vector3.zero;
        boxCollider.size = bounds.size;
        boundingBoxCollider = boxCollider;
    }

    #endregion

    #region Data Loading

    private void ParseSWC(string swcFileContent)
    {
        swc.Clear();

        string[] lines = swcFileContent.Split('\n');
        foreach (string rawLine in lines)
        {
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                continue;
            }

            string line = rawLine.Trim();
            if (line.StartsWith("#"))
            {
                continue;
            }

            string[] tokens = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 7)
            {
                Debug.LogWarning($"[VesselViewer] Invalid SWC line skipped: {line}");
                continue;
            }

            try
            {
                int id = int.Parse(tokens[0], CultureInfo.InvariantCulture);
                int type = int.Parse(tokens[1], CultureInfo.InvariantCulture);
                float x = float.Parse(tokens[2], CultureInfo.InvariantCulture) * 0.001f;
                float y = float.Parse(tokens[3], CultureInfo.InvariantCulture) * 0.001f;
                float z = float.Parse(tokens[4], CultureInfo.InvariantCulture) * 0.001f;
                float radius = float.Parse(tokens[5], CultureInfo.InvariantCulture) * 0.001f;
                int parent = int.Parse(tokens[6], CultureInfo.InvariantCulture);

                swc[id] = new SWCNode
                {
                    id = id,
                    type = type,
                    position = new Vector3(x, y, z),
                    radius = radius,
                    parent = parent
                };
            }
            catch (Exception ex)
            {
                Debug.LogError($"[VesselViewer] Failed to parse SWC line: {line} | {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Load branch definitions exported from the 2D layout side.
    /// UID convention: parentId * 1,000,000 + firstChildId.
    /// </summary>
    private void LoadBranchMappings()
    {
        TextAsset jsonAsset = Resources.Load<TextAsset>("treeLayoutforVesselviewer");
        if (jsonAsset == null)
        {
            Debug.LogError("[VesselViewer] treeLayoutforVesselviewer.json not found.");
            return;
        }

        TreeLayoutData treeData = JsonUtility.FromJson<TreeLayoutData>(jsonAsset.text);
        if (treeData == null || treeData.nodes == null)
        {
            Debug.LogError("[VesselViewer] Failed to parse tree layout JSON.");
            return;
        }

        parentMappings.Clear();
        branchMappings.Clear();

        foreach (NodeData node in treeData.nodes)
        {
            if (node.childs == null)
            {
                continue;
            }

            foreach (int childId in node.childs)
            {
                if (!parentMappings.TryGetValue(childId, out List<int> parents))
                {
                    parents = new List<int>();
                    parentMappings[childId] = parents;
                }

                if (!parents.Contains(node.id))
                {
                    parents.Add(node.id);
                }
            }
        }

        int loadedCount = 0;
        foreach (NodeData node in treeData.nodes)
        {
            if (node.childs == null || node.childs.Count == 0)
            {
                continue;
            }

            int firstChild = node.childs[0];
            int canonicalParent = GetCanonicalParentForFirst(firstChild);
            if (canonicalParent == -1)
            {
                Debug.LogWarning($"[VesselViewer] Missing canonical parent for branch starting at {firstChild}.");
                continue;
            }

            int uid = MakeBranchUid(canonicalParent, firstChild);
            branchMappings[uid] = new List<int>(node.childs);
            loadedCount++;
        }

        Debug.Log($"[VesselViewer] Loaded {loadedCount} branch mappings.");
    }

    private void ExportBranchMappingsToTxt()
    {
        StringBuilder sb = new StringBuilder();

        foreach (var pair in branchMappings)
        {
            int uid = pair.Key;
            int parent = UidParent(uid);
            int first = UidFirst(uid);
            string children = string.Join(", ", pair.Value);
            sb.AppendLine($"UID {uid}  (parent {parent} -> first {first})  : [ {children} ]");
        }

        string filePath = Path.Combine(Application.dataPath, "BranchMappingsOutput.txt");
        File.WriteAllText(filePath, sb.ToString());
        Debug.Log($"[VesselViewer] Branch mappings exported to: {filePath}");
    }

    #endregion

    #region Material Setup

    private void CreateDefaultMaterials()
    {
        if (defaultMaterial == null)
        {
            Shader shader = defaultColorMode == DefaultColorMode.Gray
                ? Shader.Find("Custom/TransparentZWrite")
                : Shader.Find("Standard");

            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            defaultMaterial = new Material(shader);
            SetColorWithAlpha(defaultMaterial, new Color(0.5f, 0.5f, 0.5f, 1f), 0.2f);
            SetTransparent(defaultMaterial);
        }

        // Kept for compatibility with the original script.
        transparentMaterial = defaultMaterial;

        if (selectedMaterial == null)
        {
            selectedMaterial = new Material(Shader.Find("Standard"));
            selectedMaterial.color = Color.red;
            SetOpaque(selectedMaterial);
        }

        if (outlineMaterial == null)
        {
            Shader shader = Shader.Find("Custom/OutlinedOnly");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            outlineMaterial = new Material(shader);

            if (outlineMaterial.HasProperty("_OutlineColor"))
            {
                outlineMaterial.SetColor("_OutlineColor", Color.yellow);
            }
            else if (outlineMaterial.HasProperty("_Color"))
            {
                outlineMaterial.SetColor("_Color", Color.yellow);
            }
        }

        ConfigureOutlineMaterial(outlineMaterial, 0.45f);
        if (outlineMaterial.HasProperty("_OutlineWidth"))
        {
            outlineMaterial.SetFloat("_OutlineWidth", 0.015f);
        }
    }

    public void RefreshMaterialsForColorMode()
    {
        transparentByType.Clear();
        defaultMaterial = null;
        CreateDefaultMaterials();

        foreach (var pair in swc)
        {
            SWCNode node = pair.Value;
            if (node?.associatedGameObject == null)
            {
                continue;
            }

            Renderer renderer = node.associatedGameObject.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material = new Material(GetDefaultTransparent(node));
            }
        }

        if (neuronContainer != null)
        {
            foreach (Transform child in neuronContainer.transform)
            {
                GameObject go = child.gameObject;
                if (!go.name.StartsWith("Frustum_"))
                {
                    continue;
                }

                string[] parts = go.name.Split('_');
                if (parts.Length < 3 || !int.TryParse(parts[1], out int childId))
                {
                    continue;
                }

                Renderer renderer = go.GetComponent<Renderer>();
                if (renderer == null)
                {
                    continue;
                }

                Material source = defaultMaterial;
                if (swc.TryGetValue(childId, out SWCNode childNode))
                {
                    source = GetDefaultTransparent(childNode);
                }

                renderer.material = new Material(source);
            }
        }
    }

    private Material GetDefaultTransparent(SWCNode node)
    {
        return defaultColorMode == DefaultColorMode.TypeColor
            ? GetTransparentForType(node.type)
            : defaultMaterial;
    }

    private Material GetTransparentForType(int type)
    {
        if (!transparentByType.TryGetValue(type, out Material material))
        {
            Shader shader = defaultColorMode == DefaultColorMode.Gray
                ? Shader.Find("Custom/TransparentZWrite")
                : Shader.Find("Standard");

            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            material = new Material(shader);
            SetColorWithAlpha(material, colors[type % colors.Length], 0.2f);
            SetTransparent(material);
            transparentByType[type] = material;
        }

        return material;
    }

    private void ConfigureOutlineMaterial(Material material, float alpha)
    {
        if (material == null)
        {
            return;
        }

        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.EnableKeyword("_ALPHABLEND_ON");

        material.SetInt("_ZWrite", 0);
        material.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Front);

        if (material.HasProperty("_BaseColor"))
        {
            Color c = material.GetColor("_BaseColor");
            c.a = alpha;
            material.SetColor("_BaseColor", c);
        }

        if (material.HasProperty("_Color"))
        {
            Color c = material.GetColor("_Color");
            c.a = alpha;
            material.SetColor("_Color", c);
        }

        if (material.HasProperty("_OutlineColor"))
        {
            Color c = material.GetColor("_OutlineColor");
            c.a = alpha;
            material.SetColor("_OutlineColor", c);
        }
    }

    private void SetTransparent(Material material)
    {
        if (material == null)
        {
            return;
        }

        material.SetFloat("_Mode", 3);
        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);

        bool writeDepth = defaultColorMode == DefaultColorMode.Gray;
        material.SetInt("_ZWrite", writeDepth ? 1 : 0);

        material.DisableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");

        material.renderQueue = writeDepth
            ? 2500
            : (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }

    private void SetOpaque(Material material)
    {
        if (material == null)
        {
            return;
        }

        material.SetFloat("_Mode", 0);
        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
        material.DisableKeyword("_ALPHATEST_ON");
        material.DisableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.renderQueue = -1;
    }

    private static void SetColorWithAlpha(Material material, Color color, float alpha)
    {
        color.a = alpha;

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }
    }

    #endregion

    #region Geometry Generation

    public GameObject GenerateSphere(SWCNode node)
    {
        if (defaultMaterial == null)
        {
            CreateDefaultMaterials();
        }

        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = $"Sphere_{node.id}";
        sphere.transform.position = node.position;
        sphere.transform.localScale = Vector3.one * (node.radius * 2f);
        sphere.layer = LayerMask.NameToLayer("Selectable");

        Renderer renderer = sphere.GetComponent<Renderer>();
        renderer.material = GetDefaultTransparent(node);

        SphereCollider collider = sphere.GetComponent<SphereCollider>();
        collider.enabled = true;

        sphere.AddComponent<NearInteractionTouchableVolume>();

        node.associatedGameObject = sphere;
        node.outlineShell = CreateOutlineShell(sphere);
        node.outlineShell.SetActive(false);

        return sphere;
    }

    public GameObject GenerateCone(SWCNode node, SWCNode parentNode)
    {
        if (defaultMaterial == null)
        {
            CreateDefaultMaterials();
        }

        Vector3 start = node.position;
        Vector3 end = parentNode.position;
        Vector3 direction = end - start;
        float distance = direction.magnitude;

        GameObject cone = new GameObject($"Frustum_{node.id}_{parentNode.id}");
        cone.layer = LayerMask.NameToLayer("Selectable");

        MeshFilter meshFilter = cone.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = cone.AddComponent<MeshRenderer>();
        MeshCollider collider = cone.AddComponent<MeshCollider>();

        meshFilter.mesh = GenerateFrustumMesh(node.radius, parentNode.radius, distance);
        meshRenderer.material = GetDefaultTransparent(node);
        collider.sharedMesh = meshFilter.mesh;
        collider.convex = true;

        cone.transform.position = start;
        cone.transform.up = direction.normalized;

        cone.AddComponent<NearInteractionTouchableVolume>();

        CreateOutlineShellForCone(cone, meshFilter.mesh);
        return cone;
    }

    private GameObject CreateOutlineShell(GameObject target, float scaleFactor = 1.03f)
    {
        GameObject shell = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        shell.name = "OutlineShell";
        shell.transform.SetParent(target.transform, false);
        shell.transform.localPosition = Vector3.zero;
        shell.transform.localRotation = Quaternion.identity;
        shell.transform.localScale = Vector3.one * scaleFactor;
        shell.layer = target.layer;

        Renderer shellRenderer = shell.GetComponent<Renderer>();
        shellRenderer.material = new Material(outlineMaterial);
        ConfigureOutlineMaterial(shellRenderer.material, 0.45f);

        Collider shellCollider = shell.GetComponent<Collider>();
        if (shellCollider != null)
        {
            Destroy(shellCollider);
        }

        shell.SetActive(false);
        return shell;
    }

    private void CreateOutlineShellForCone(GameObject cone, Mesh sourceMesh)
    {
        GameObject shell = new GameObject("OutlineShell");
        shell.transform.SetParent(cone.transform, false);
        shell.transform.localPosition = Vector3.zero;
        shell.transform.localRotation = Quaternion.identity;
        shell.transform.localScale = Vector3.one * 1.03f;

        MeshFilter shellFilter = shell.AddComponent<MeshFilter>();
        shellFilter.mesh = sourceMesh;

        MeshRenderer shellRenderer = shell.AddComponent<MeshRenderer>();
        shellRenderer.material = new Material(outlineMaterial);
        ConfigureOutlineMaterial(shellRenderer.material, 0.45f);

        shell.SetActive(false);
    }

    private Mesh GenerateFrustumMesh(float bottomRadius, float topRadius, float height, int segments = 40)
    {
        Mesh mesh = new Mesh();

        Vector3[] vertices = new Vector3[(segments + 1) * 2];
        Vector3[] normals = new Vector3[vertices.Length];
        Vector2[] uvs = new Vector2[vertices.Length];
        int[] triangles = new int[segments * 6];

        float angleStep = 360f / segments;

        for (int i = 0; i <= segments; i++)
        {
            float angle = Mathf.Deg2Rad * i * angleStep;
            float x = Mathf.Cos(angle);
            float z = Mathf.Sin(angle);

            vertices[i] = new Vector3(x * bottomRadius, 0f, z * bottomRadius);
            normals[i] = new Vector3(x, 0f, z).normalized;
            uvs[i] = new Vector2((float)i / segments, 0f);

            int upperIndex = i + segments + 1;
            vertices[upperIndex] = new Vector3(x * topRadius, height, z * topRadius);
            normals[upperIndex] = new Vector3(x, 0f, z).normalized;
            uvs[upperIndex] = new Vector2((float)i / segments, 1f);
        }

        int triIndex = 0;
        for (int i = 0; i < segments; i++)
        {
            triangles[triIndex] = i;
            triangles[triIndex + 1] = i + segments + 1;
            triangles[triIndex + 2] = i + 1;

            triangles[triIndex + 3] = i + 1;
            triangles[triIndex + 4] = i + segments + 1;
            triangles[triIndex + 5] = i + segments + 2;

            triIndex += 6;
        }

        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();

        return mesh;
    }

    #endregion

    #region Interaction Utilities

    public void ToggleInteractionMode()
    {
        interactionMode = interactionMode == InteractionMode.Single
            ? InteractionMode.Branch
            : InteractionMode.Single;

        Debug.Log($"[VesselViewer] Interaction mode: {interactionMode}");
    }

    public void ToggleManipulation()
    {
        manipulationEnabled = !manipulationEnabled;

        if (manipulator == null && neuronContainer != null)
        {
            manipulator = neuronContainer.GetComponent<ObjectManipulator>();
        }

        if (boundingBoxCollider == null && neuronContainer != null)
        {
            Transform box = neuronContainer.transform.Find("BoundingBox");
            if (box != null)
            {
                boundingBoxCollider = box.GetComponent<Collider>();
            }
        }

        if (manipulator != null)
        {
            manipulator.enabled = manipulationEnabled;
        }

        if (boundingBoxCollider != null)
        {
            boundingBoxCollider.enabled = manipulationEnabled;
        }

        if (neuronContainer != null)
        {
            foreach (NearInteractionGrabbable grabbable in neuronContainer.GetComponentsInChildren<NearInteractionGrabbable>())
            {
                grabbable.enabled = true;
            }
        }
    }

    private static int MakeBranchUid(int parentId, int firstChildId)
    {
        return parentId * UID_FACTOR + firstChildId;
    }

    private static int UidParent(int uid)
    {
        return uid / UID_FACTOR;
    }

    private static int UidFirst(int uid)
    {
        return uid % UID_FACTOR;
    }

    private static int GetFirstNodeFromUid(int uid)
    {
        return Mathf.Abs(uid) % UID_FACTOR;
    }

    private int GetCanonicalParentForFirst(int firstNode)
    {
        if (!parentMappings.TryGetValue(firstNode, out List<int> parents) || parents == null || parents.Count == 0)
        {
            return -1;
        }

        parents.Sort();
        return parents[0];
    }

    private int DetectFirstForBranch(List<int> nodeIds)
    {
        return nodeIds[0];
    }

    private List<int> FindBranchFromNode(int nodeId)
    {
        foreach (var pair in branchMappings)
        {
            if (pair.Value.Contains(nodeId))
            {
                return pair.Value;
            }
        }

        return new List<int> { nodeId };
    }

    private int GetNodeIdFromObject(string name, out bool isCone, out int parentId)
    {
        isCone = false;
        parentId = -1;

        if (name.StartsWith("Sphere_", StringComparison.Ordinal))
        {
            return int.Parse(name.Substring("Sphere_".Length), CultureInfo.InvariantCulture);
        }

        if (name.StartsWith("Frustum_", StringComparison.Ordinal))
        {
            string[] parts = name.Split('_');
            if (parts.Length >= 3)
            {
                int childId = int.Parse(parts[1], CultureInfo.InvariantCulture);
                parentId = int.Parse(parts[2], CultureInfo.InvariantCulture);
                isCone = true;
                return childId;
            }
        }

        return -1;
    }

    private bool IsSelected(GameObject obj)
    {
        return selectionStates.TryGetValue(obj, out bool selected) && selected;
    }

    private bool IsBranchSelected(List<int> branch)
    {
        if (branch == null || branch.Count == 0)
        {
            return false;
        }

        foreach (int id in branch)
        {
            if (swc.TryGetValue(id, out SWCNode node) && node.associatedGameObject != null)
            {
                if (IsSelected(node.associatedGameObject))
                {
                    return true;
                }
            }
        }

        for (int i = 0; i < branch.Count - 1; i++)
        {
            GameObject cone = GameObject.Find($"Frustum_{branch[i + 1]}_{branch[i]}");
            if (cone != null && IsSelected(cone))
            {
                return true;
            }
        }

        int firstNode = branch[0];
        if (parentMappings.TryGetValue(firstNode, out List<int> parents))
        {
            foreach (int parent in parents)
            {
                GameObject incoming = GameObject.Find($"Frustum_{firstNode}_{parent}");
                if (incoming != null && IsSelected(incoming))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void SetSelected(GameObject obj, bool selected)
    {
        Renderer renderer = obj.GetComponent<Renderer>();
        if (renderer == null)
        {
            return;
        }

        if (selected)
        {
            if (!originalMaterials.ContainsKey(obj))
            {
                originalMaterials[obj] = renderer.material;
            }

            Material redMaterial = new Material(renderer.material);
            redMaterial.color = Color.red;
            renderer.material = redMaterial;
            selectionStates[obj] = true;
        }
        else
        {
            if (originalMaterials.TryGetValue(obj, out Material original))
            {
                renderer.material = original;
            }

            selectionStates[obj] = false;
        }
    }

    private void SetOutlineVisible(GameObject go, bool visible)
    {
        if (go == null)
        {
            return;
        }

        Transform shell = go.transform.Find("OutlineShell");
        if (shell != null)
        {
            shell.gameObject.SetActive(visible);
        }
    }

    #endregion

    #region Pointer Interaction

    public void OnPointerClicked(MixedRealityPointerEventData eventData)
    {
        if (Time.time - lastClickTime < ClickCooldown)
        {
            return;
        }

        lastClickTime = Time.time;

        GameObject hitObject = eventData?.Pointer?.Result?.Details.Object;
        if (hitObject == null)
        {
            return;
        }

        if (hitObject.name == "OutlineShell" && hitObject.transform.parent != null)
        {
            hitObject = hitObject.transform.parent.gameObject;
        }

        int nodeId = GetNodeIdFromObject(hitObject.name, out bool isCone, out int parentId);
        if (nodeId < 0)
        {
            return;
        }

        if (interactionMode == InteractionMode.Single)
        {
            HandleSingleClick(hitObject, nodeId, isCone, parentId);
        }
        else
        {
            HandleBranchClick(nodeId, isCone, parentId);
        }
    }

    public void OnPointerDown(MixedRealityPointerEventData eventData) { }
    public void OnPointerUp(MixedRealityPointerEventData eventData) { }
    public void OnPointerDragged(MixedRealityPointerEventData eventData) { }

    private void HandleSingleClick(GameObject hitObject, int nodeId, bool isCone, int parentId)
    {
        if (!isCone)
        {
            if (swc.TryGetValue(nodeId, out SWCNode node) && node.associatedGameObject != null)
            {
                bool selected = IsSelected(node.associatedGameObject);
                SetSelected(node.associatedGameObject, !selected);
            }

            return;
        }

        GameObject cone = GameObject.Find($"Frustum_{nodeId}_{parentId}");
        if (cone != null)
        {
            bool selected = IsSelected(cone);
            SetSelected(cone, !selected);
        }
    }

    private void HandleBranchClick(int nodeId, bool isCone, int parentId)
    {
        int branchRoot = isCone ? parentId : nodeId;
        List<int> branch = FindBranchFromNode(branchRoot);
        if (branch == null || branch.Count == 0)
        {
            branch = new List<int> { branchRoot };
        }

        bool currentlySelected = IsBranchSelected(branch);
        bool nextState = !currentlySelected;

        foreach (int id in branch)
        {
            if (swc.TryGetValue(id, out SWCNode node) && node.associatedGameObject != null)
            {
                SetSelected(node.associatedGameObject, nextState);
            }
        }

        for (int i = 0; i < branch.Count - 1; i++)
        {
            GameObject cone = GameObject.Find($"Frustum_{branch[i + 1]}_{branch[i]}");
            if (cone != null)
            {
                SetSelected(cone, nextState);
            }
        }

        int firstNode = branch[0];
        if (parentMappings.TryGetValue(firstNode, out List<int> parents))
        {
            foreach (int parent in parents)
            {
                GameObject incoming = GameObject.Find($"Frustum_{firstNode}_{parent}");
                if (incoming != null)
                {
                    SetSelected(incoming, nextState);
                }
            }
        }

        int canonicalParent = GetCanonicalParentForFirst(firstNode);
        if (canonicalParent != -1)
        {
            int uid = MakeBranchUid(canonicalParent, firstNode);
            OnBranchToggled?.Invoke(uid, nextState);
        }
    }

    #endregion

    #region Focus Interaction

    public void OnFocusEnter(FocusEventData eventData)
    {
        GameObject obj = eventData.NewFocusedObject;
        if (obj == null || obj.layer != LayerMask.NameToLayer("Selectable"))
        {
            return;
        }

        int nodeId = GetNodeIdFromObject(obj.name, out bool isCone, out int parentId);
        if (nodeId < 0)
        {
            return;
        }

        if (interactionMode == InteractionMode.Single)
        {
            HandleSingleFocusEnter(nodeId, isCone, parentId);
        }
        else
        {
            HandleBranchFocusEnter(nodeId, isCone, parentId);
        }
    }

    public void OnFocusExit(FocusEventData eventData)
    {
        GameObject obj = eventData.OldFocusedObject;
        if (obj == null || obj.layer != LayerMask.NameToLayer("Selectable"))
        {
            return;
        }

        int nodeId = GetNodeIdFromObject(obj.name, out bool isCone, out int parentId);
        if (nodeId < 0)
        {
            return;
        }

        if (interactionMode == InteractionMode.Single)
        {
            HandleSingleFocusExit(nodeId, isCone, parentId);
        }
        else
        {
            HandleBranchFocusExit(nodeId, isCone, parentId);
        }
    }

    private void HandleSingleFocusEnter(int nodeId, bool isCone, int parentId)
    {
        if (!isCone)
        {
            if (swc.TryGetValue(nodeId, out SWCNode node) && node.associatedGameObject != null)
            {
                SetOutlineVisible(node.associatedGameObject, true);
            }

            return;
        }

        GameObject cone = GameObject.Find($"Frustum_{nodeId}_{parentId}");
        SetOutlineVisible(cone, true);
    }

    private void HandleSingleFocusExit(int nodeId, bool isCone, int parentId)
    {
        if (!isCone)
        {
            if (swc.TryGetValue(nodeId, out SWCNode node) && node.associatedGameObject != null)
            {
                SetOutlineVisible(node.associatedGameObject, false);
            }

            return;
        }

        GameObject cone = GameObject.Find($"Frustum_{nodeId}_{parentId}");
        SetOutlineVisible(cone, false);
    }

    private void HandleBranchFocusEnter(int nodeId, bool isCone, int parentId)
    {
        int branchRoot = isCone ? parentId : nodeId;
        List<int> branch = FindBranchFromNode(branchRoot);

        foreach (int id in branch)
        {
            if (swc.TryGetValue(id, out SWCNode node) && node.associatedGameObject != null)
            {
                SetOutlineVisible(node.associatedGameObject, true);
            }
        }

        for (int i = 0; i < branch.Count - 1; i++)
        {
            GameObject cone = GameObject.Find($"Frustum_{branch[i + 1]}_{branch[i]}");
            SetOutlineVisible(cone, true);
        }

        int firstNode = branch[0];
        if (parentMappings.TryGetValue(firstNode, out List<int> parents))
        {
            foreach (int parent in parents)
            {
                GameObject incoming = GameObject.Find($"Frustum_{firstNode}_{parent}");
                SetOutlineVisible(incoming, true);
            }
        }

        int canonicalParent = GetCanonicalParentForFirst(firstNode);
        if (canonicalParent != -1)
        {
            OnBranchHovered?.Invoke(MakeBranchUid(canonicalParent, firstNode));
        }
    }

    private void HandleBranchFocusExit(int nodeId, bool isCone, int parentId)
    {
        int branchRoot = isCone ? parentId : nodeId;
        List<int> branch = FindBranchFromNode(branchRoot);

        foreach (int id in branch)
        {
            if (swc.TryGetValue(id, out SWCNode node) && node.associatedGameObject != null)
            {
                SetOutlineVisible(node.associatedGameObject, false);
            }
        }

        for (int i = 0; i < branch.Count - 1; i++)
        {
            GameObject cone = GameObject.Find($"Frustum_{branch[i + 1]}_{branch[i]}");
            SetOutlineVisible(cone, false);
        }

        int firstNode = branch[0];
        if (parentMappings.TryGetValue(firstNode, out List<int> parents))
        {
            foreach (int parent in parents)
            {
                GameObject incoming = GameObject.Find($"Frustum_{firstNode}_{parent}");
                SetOutlineVisible(incoming, false);
            }
        }
    }

    #endregion

    #region Linked 2D-3D API

    public void HighlightNodes(List<int> nodeIds)
    {
        if (nodeIds == null || nodeIds.Count == 0)
        {
            return;
        }

        int first = DetectFirstForBranch(nodeIds);
        int canonicalParent = GetCanonicalParentForFirst(first);
        if (canonicalParent == -1)
        {
            return;
        }

        int uid = MakeBranchUid(canonicalParent, first);
        SelectBranchByRoot(uid, true);
        OnBranchToggled?.Invoke(uid, true);
    }

    public void UnhighlightNodes(List<int> nodeIds)
    {
        if (nodeIds == null || nodeIds.Count == 0)
        {
            return;
        }

        int first = DetectFirstForBranch(nodeIds);
        int canonicalParent = GetCanonicalParentForFirst(first);
        if (canonicalParent == -1)
        {
            return;
        }

        int uid = MakeBranchUid(canonicalParent, first);
        SelectBranchByRoot(uid, false);
        OnBranchToggled?.Invoke(uid, false);
    }

    public void resetAllNodes()
    {
        selectionStates.Clear();

        foreach (var pair in swc)
        {
            SWCNode node = pair.Value;
            if (node?.associatedGameObject == null)
            {
                continue;
            }

            Renderer renderer = node.associatedGameObject.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material = GetDefaultTransparent(node);
            }

            if (node.outlineShell != null)
            {
                node.outlineShell.SetActive(false);
            }
        }

        if (neuronContainer == null)
        {
            return;
        }

        foreach (Transform child in neuronContainer.transform)
        {
            GameObject go = child.gameObject;
            if (!go.name.StartsWith("Frustum_", StringComparison.Ordinal))
            {
                continue;
            }

            string[] parts = go.name.Split('_');
            if (parts.Length < 3 || !int.TryParse(parts[1], out int childId))
            {
                continue;
            }

            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material = swc.TryGetValue(childId, out SWCNode childNode)
                    ? GetDefaultTransparent(childNode)
                    : defaultMaterial;
            }

            SetOutlineVisible(go, false);
        }
    }

    public void SelectBranchByRoot(int branchUid, bool select)
    {
        if (!branchMappings.TryGetValue(branchUid, out List<int> branch) || branch == null || branch.Count == 0)
        {
            return;
        }

        foreach (int id in branch)
        {
            if (swc.TryGetValue(id, out SWCNode node) && node.associatedGameObject != null)
            {
                SetSelected(node.associatedGameObject, select);
            }
        }

        for (int i = 0; i < branch.Count - 1; i++)
        {
            GameObject cone = GameObject.Find($"Frustum_{branch[i + 1]}_{branch[i]}");
            if (cone != null)
            {
                SetSelected(cone, select);
            }
        }

        int firstNode = branch[0];
        if (parentMappings.TryGetValue(firstNode, out List<int> parents))
        {
            foreach (int parent in parents)
            {
                GameObject incoming = GameObject.Find($"Frustum_{firstNode}_{parent}");
                if (incoming != null)
                {
                    SetSelected(incoming, select);
                }
            }
        }
    }

    public List<int> GetBranchNodesByUid(int uid)
    {
        if (branchMappings.Count == 0)
        {
            return null;
        }

        if (branchMappings.TryGetValue(uid, out List<int> directMatch))
        {
            return directMatch;
        }

        int first = GetFirstNodeFromUid(uid);
        foreach (var pair in branchMappings)
        {
            if (pair.Value != null && pair.Value.Count > 0 && pair.Value[0] == first)
            {
                return pair.Value;
            }
        }

        return null;
    }

    public GameObject GetSphereGO(int nodeId)
    {
        return swc.TryGetValue(nodeId, out SWCNode node) ? node.associatedGameObject : null;
    }

    public Transform GetTooltipAnchorForUid(int uid)
    {
        List<int> branch = GetBranchNodesByUid(uid);
        if (branch == null || branch.Count == 0)
        {
            return null;
        }

        if (branch.Count >= 2)
        {
            int last = branch[branch.Count - 1];
            int previous = branch[branch.Count - 2];

            GameObject frustum = GameObject.Find($"Frustum_{last}_{previous}");
            if (frustum != null)
            {
                return frustum.transform;
            }

            if (swc.TryGetValue(last, out SWCNode node) && node.associatedGameObject != null)
            {
                return node.associatedGameObject.transform;
            }

            return null;
        }

        int first = branch[0];
        int parent = GetCanonicalParentForFirst(first);
        if (parent != -1)
        {
            GameObject frustum = GameObject.Find($"Frustum_{first}_{parent}");
            if (frustum != null)
            {
                return frustum.transform;
            }
        }

        if (swc.TryGetValue(first, out SWCNode firstNode) && firstNode.associatedGameObject != null)
        {
            return firstNode.associatedGameObject.transform;
        }

        return null;
    }

    #endregion
}
