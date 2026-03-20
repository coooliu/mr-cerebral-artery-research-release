using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public enum TaskMode
{
    ThreeDOnly,
    TwoDPlusThreeD
}

[System.Serializable]
public class TaskSpec
{
    [Tooltip("Display name for this task.")]
    public string name = "Task";

    [Tooltip("Ordered target branch UIDs for the task path.")]
    public List<int> branchUids = new List<int>();
}

/// <summary>
/// StudyController coordinates the experimental task flow described in the manuscript.
/// It manages task loading, timing, head-path logging, completion gating, and mode-specific
/// button behavior for the 3D-only and 2D+3D conditions.
///
/// This version is a research-release cleanup intended for code sharing. The implementation
/// intentionally stays close to the original project structure so existing scene bindings,
/// inspector fields, and interactions remain compatible.
/// </summary>
public class StudyController : MonoBehaviour
{
    [Header("References")]
    public UIStudyPanel panel;
    public HeadPathTracker headTracker;
    public VesselViewer vessel;
    public EndpointMarkers endpointMarkers;
    public TreeEndpointMarkers treeEndpointMarkers;

    [Header("Demo Tasks (fill in Inspector)")]
    public List<TaskSpec> DemoTasks = new List<TaskSpec>();

    [Header("Mode & Extra Refs")]
    public TaskMode mode = TaskMode.ThreeDOnly;
    public TreeLayoutBezierRenderer tree;
    public GameObject panel3DOnlyRoot;
    public GameObject panel2D3DRoot;
    public UIStudyPanel panel3DOnly;
    public UIStudyPanel panel2D3D;

    private static readonly List<int> EmptyIntList = new List<int>(0);
    private const string ExtraSelectionWarning = "Extra segments selected. Deselect non-target segments.";

    // Runtime state
    private int currentTaskIndex;
    private float startTime;
    private int attempts;
    private bool completed;
    private bool rated;
    private bool taskTimingStarted;
    private readonly SelectionModel selection = new SelectionModel();

    #region Unity lifecycle

    private void Awake()
    {
        if (vessel != null)
        {
            vessel.interactionMode = VesselViewer.InteractionMode.Branch;
        }
    }

    private void Start()
    {
        EnsureDemoTasks();
        ApplyMode();
    }

    private void OnDestroy()
    {
        UnsubscribeAll();
        CancelInvoke(nameof(PollLikert));
        endpointMarkers?.Clear();
        treeEndpointMarkers?.Clear();
    }

    private void Update()
    {
        if (!taskTimingStarted)
        {
            return;
        }

        if (headTracker != null && !headTracker.IsTracking)
        {
            headTracker.StartTracking();
        }

        panel?.SetTime(Time.time - startTime);
        panel?.SetHeadPath(headTracker != null ? headTracker.TotalDistance : 0f);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!Application.isPlaying)
        {
            if (DemoTasks != null && DemoTasks.Count > 0 && panel != null)
            {
                int safeIndex = Mathf.Clamp(currentTaskIndex, 0, DemoTasks.Count - 1);
                panel.BindHeader(safeIndex + 1, DemoTasks.Count);
                panel.SetProgress(0, GetTargetUids().Count, mode == TaskMode.TwoDPlusThreeD);
            }
        }
    }
#endif

    #endregion

    #region Mode setup

    public void ApplyMode()
    {
        UnsubscribeAll();

        if (panel3DOnlyRoot != null)
        {
            panel3DOnlyRoot.SetActive(mode == TaskMode.ThreeDOnly);
        }

        if (panel2D3DRoot != null)
        {
            panel2D3DRoot.SetActive(mode == TaskMode.TwoDPlusThreeD);
        }

        panel = ResolvePanelForMode();
        panel?.ClearAllButtonListeners();
        panel?.InitVisualState();
        BindButtonsForMode();
        SubscribeForMode();
        ConfigureVesselVisualMode();
        LoadTask(currentTaskIndex);
    }

    private UIStudyPanel ResolvePanelForMode()
    {
        if (mode == TaskMode.ThreeDOnly)
        {
            return panel3DOnly != null ? panel3DOnly : panel;
        }

        return panel2D3D != null ? panel2D3D : panel;
    }

    private void BindButtonsForMode()
    {
        if (panel == null)
        {
            return;
        }

        panel.ClearAllButtonListeners();

        if (mode == TaskMode.ThreeDOnly)
        {
            panel.BtnUndo.OnClick.AddListener(DoUndo_3D);
            panel.BtnRedo.OnClick.AddListener(DoRedo_3D);
            panel.BtnReset.OnClick.AddListener(DoResetAll_3D);
        }
        else
        {
            panel.BtnUndo.OnClick.AddListener(DoUndo_Tree);
            panel.BtnRedo.OnClick.AddListener(DoRedo_Tree);
            panel.BtnReset.OnClick.AddListener(DoResetAll_Tree);
        }

        panel.BtnNext.OnClick.AddListener(GoNext);
    }

    private void SubscribeForMode()
    {
        if (mode == TaskMode.ThreeDOnly)
        {
            if (vessel != null)
            {
                vessel.OnBranchToggled += OnBranchToggled;
                vessel.OnBranchHovered += OnBranchHovered;
            }
            return;
        }

        TreeLayoutBezierRenderer.OnAnySegmentFirstHover += OnTreeFirstHover;
        TreeLayoutBezierRenderer.OnAnySegmentClicked += OnTreeClicked;

        if (vessel != null)
        {
            vessel.OnBranchToggled += OnBranchToggled;
        }
    }

    private void UnsubscribeAll()
    {
        if (vessel != null)
        {
            vessel.OnBranchToggled -= OnBranchToggled;
            vessel.OnBranchHovered -= OnBranchHovered;
        }

        TreeLayoutBezierRenderer.OnAnySegmentFirstHover -= OnTreeFirstHover;
        TreeLayoutBezierRenderer.OnAnySegmentClicked -= OnTreeClicked;
    }

    private void ConfigureVesselVisualMode()
    {
        if (vessel == null)
        {
            return;
        }

        vessel.defaultColorMode = mode == TaskMode.TwoDPlusThreeD
            ? VesselViewer.DefaultColorMode.TypeColor
            : VesselViewer.DefaultColorMode.Gray;

        vessel.resetAllNodes();
    }

    #endregion

    #region Task loading

    private void EnsureDemoTasks()
    {
        if (DemoTasks != null && DemoTasks.Count > 0)
        {
            return;
        }

        DemoTasks = new List<TaskSpec>
        {
            new TaskSpec { name = "Task 01", branchUids = new List<int> { 1100023, 6570009, 1490011 } },
            new TaskSpec { name = "Task 02", branchUids = new List<int> { 9000023, 6600659, 2400023 } }
        };
    }

    private void LoadTask(int index)
    {
        if (DemoTasks == null || DemoTasks.Count == 0)
        {
            LoadEmptyState();
            return;
        }

        endpointMarkers?.Clear();
        treeEndpointMarkers?.Clear();

        currentTaskIndex = Mathf.Clamp(index, 0, DemoTasks.Count - 1);
        ResetRuntimeState();
        selection.Clear();
        vessel?.resetAllNodes();

        panel?.BindHeader(currentTaskIndex + 1, DemoTasks.Count);
        panel?.SetAttempts(attempts);
        panel?.SetTime(0f);
        panel?.SetHeadPath(0f);
        panel?.ResetLikert();
        panel?.ShowWarning(string.Empty);

        UpdateProgressUI();
        panel?.SetButtons(false, false, true, false);
        CancelInvoke(nameof(PollLikert));
        InvokeRepeating(nameof(PollLikert), 0.2f, 0.2f);
        ShowTaskMarkers();
    }

    private void LoadEmptyState()
    {
        currentTaskIndex = 0;
        ResetRuntimeState();
        selection.Clear();
        vessel?.resetAllNodes();

        panel?.BindHeader(0, 0);
        panel?.SetAttempts(0);
        panel?.SetTime(0f);
        panel?.SetHeadPath(0f);
        panel?.ResetLikert();
        panel?.ShowWarning(string.Empty);
        panel?.SetProgress(0, 0, mode == TaskMode.TwoDPlusThreeD);
        panel?.SetButtons(false, false, true, false);

        endpointMarkers?.Clear();
        treeEndpointMarkers?.Clear();
        CancelInvoke(nameof(PollLikert));
    }

    private void ResetRuntimeState()
    {
        taskTimingStarted = false;
        startTime = 0f;
        attempts = 0;
        completed = false;
        rated = false;

        if (headTracker != null)
        {
            headTracker.StopTracking();
            headTracker.ResetDistance();
        }
    }

    private void ShowTaskMarkers()
    {
        List<int> targetUids = GetTargetUids();
        endpointMarkers?.ShowForTask(targetUids);

        if (mode == TaskMode.TwoDPlusThreeD && treeEndpointMarkers != null)
        {
            treeEndpointMarkers.EnsureManualSize(DemoTasks.Count);
            treeEndpointMarkers.ShowForTask(targetUids, currentTaskIndex);
        }
        else
        {
            treeEndpointMarkers?.Clear();
        }
    }

    private List<int> GetTargetUids()
    {
        if (DemoTasks == null || DemoTasks.Count == 0)
        {
            return EmptyIntList;
        }

        int safeIndex = Mathf.Clamp(currentTaskIndex, 0, DemoTasks.Count - 1); 
        TaskSpec spec = DemoTasks[safeIndex]; return spec != null && spec.branchUids != null ? spec.branchUids : EmptyIntList;
    }

    #endregion 
    
    #region Timing and event callbacks 
    
    private void OnBranchHovered(int uid) 
    { 
        StartTimingIfNeeded();
    } 

    private void OnTreeFirstHover() 
    { 
        StartTimingIfNeeded();
    } 

    private void StartTimingIfNeeded() 
    { 
        if (taskTimingStarted) 
        { 
            return;
        } 
        
        taskTimingStarted = true; 
        startTime = Time.time;
        
        if (headTracker != null) 
        { 
            headTracker.ResetDistance(); 
            headTracker.StartTracking(); 
        } 
        
        panel?.SetTime(0f);
        panel?.SetHeadPath(0f); 
    } 

    private void OnTreeClicked(bool toSelected) 
    { 
        if (!toSelected) 
        { 
            return; 
        } 
        
        attempts++; 
        panel?.SetAttempts(attempts); 
    } 
    
    private void OnBranchToggled(int uid, bool toSelected) 
    { 
        // In the 3D-only condition, attempts are counted from direct 3D selections. 
        // In the 2D+3D condition, attempts are counted from 2D click events to avoid double-counting.
        if (mode == TaskMode.ThreeDOnly && toSelected) 
        { 
            attempts++; 
            panel?.SetAttempts(attempts);
        } 
        
        selection.ApplyToggle(uid, toSelected);
        UpdateProgressAndGate(); 
    } 
    
    #endregion 
    
    #region Button actions 
    
    private void DoUndo_3D() 
    { 
        if (!selection.CanUndo || vessel == null) 
        { 
            return; 
        } 
        (int uid, bool toSelected) = selection.Undo(); 
        vessel.SelectBranchByRoot(uid, toSelected); 
        
        attempts++; panel?.SetAttempts(attempts); 
        
        UpdateProgressAndGate(); 
    } 
    
    private void DoRedo_3D() 
    { 
        if (!selection.CanRedo || vessel == null) 
        { 
            return; 
        } 
        (int uid, bool toSelected) = selection.Redo();
        vessel.SelectBranchByRoot(uid, toSelected); 
        
        attempts++; 
        panel?.SetAttempts(attempts); 
        
        UpdateProgressAndGate(); 
    } 
    
    private void DoResetAll_3D() 
    { 
        selection.Clear(); 
        vessel?.resetAllNodes(); 
        endpointMarkers?.Clear(); 
        treeEndpointMarkers?.Clear(); 
        
        attempts++; 
        panel?.SetAttempts(attempts); 
        
        ResetTimingForReset(); 
        completed = false; 
        rated = false; 
        
        panel?.ResetLikert(); 
        panel?.ShowWarning(string.Empty); 
        UpdateProgressUI(); 
        panel?.SetButtons(false, false, true, false); 
    
    } 
    
    private void DoUndo_Tree() 
    { 
        if (tree == null) 
        { 
            return; 
        } 
        
        tree.UndoSelection(); 
        attempts++; 
        panel?.SetAttempts(attempts); 
        
        UpdateProgressAndGate(); 
    } 
    
    private void DoRedo_Tree() 
    { 
        if (tree == null) 
        { 
            return; 
        } 
        
        tree.RedoSelection(); 
        attempts++; 
        panel?.SetAttempts(attempts); 
        
        UpdateProgressAndGate(); 
    } 
    
    private void DoResetAll_Tree() 
    { 
        tree?.ResetSelectionFromOutside();
        attempts++; 
        panel?.SetAttempts(attempts); 
        
        endpointMarkers?.Clear(); 
        treeEndpointMarkers?.Clear(); 
        
        ResetTimingForReset(); 
        completed = false; 
        rated = false; 
        
        panel?.ResetLikert(); 
        panel?.ShowWarning(string.Empty); 
        UpdateProgressUI(); 
        panel?.SetButtons(false, false, true, false); 
    } 
    
    private void ResetTimingForReset() 
    { 
        taskTimingStarted = false; 
        startTime = 0f; 
        
        if (headTracker != null) 
        { 
            headTracker.StopTracking(); 
            headTracker.ResetDistance(); 
        } 
        
        panel?.SetTime(0f); 
        panel?.SetHeadPath(0f); 
    } 
    
    private void GoNext() 
    { 
        if (!(completed && rated)) 
        { 
            return; 
        } 
        
        int nextIndex = currentTaskIndex + 1; 
        if (nextIndex >= DemoTasks.Count) 
        { 
            nextIndex = 0; 
        } 
        
        LoadTask(nextIndex); 
        
        if (mode == TaskMode.TwoDPlusThreeD) 
        { 
            tree?.ResetSelectionFromOutside(); 
        } 
    } 
    
    private void PollLikert() 
    { 
        if (rated || panel == null) 
        { 
            return; 
        } 
        
        if (panel.TryGetLikert(out _)) 
        { 
            rated = true;
            RefreshButtons(); 
        } 
    } 
    
    #endregion 
    
    #region Progress and completion 
    
    private void UpdateProgressAndGate() 
    { 
        HashSet<int> target = GetTargetUids().ToHashSet(); 
        HashSet<int> selected = selection.Selected.ToHashSet(); 
        
        bool allTargetSelected = target.IsSubsetOf(selected); 
        bool hasExtraSelection = selected.Except(target).Any(); 
        
        completed = allTargetSelected && !hasExtraSelection; 
        panel?.ShowWarning(hasExtraSelection ? ExtraSelectionWarning : string.Empty); 
        
        UpdateProgressUI(); 
        RefreshButtons(); 
    } 
    
    private void UpdateProgressUI() 
    { 
        List<int> target = GetTargetUids(); 
        int total = target.Count; 
        int filled = selection.Selected.Count(uid => target.Contains(uid)); 
        
        panel?.SetProgress(filled, total, mode == TaskMode.TwoDPlusThreeD); 
    } 
    
    private void RefreshButtons() 
    { 
        panel?.SetButtons(selection.CanUndo, selection.CanRedo, true, completed && rated); 
    }

    #endregion
}




