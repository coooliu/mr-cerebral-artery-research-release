using System.Text;
using TMPro;
using UnityEngine;
using Microsoft.MixedReality.Toolkit.UI;

/// <summary>
/// UIStudyPanel manages the task control panel used in the study condition.
/// It updates task status text, enables or disables action buttons,
/// and reads the post-task Likert rating.
///
/// This cleanup is intended for the research-release version of the project.
/// Public field names and method signatures are preserved to remain compatible
/// with existing scene bindings and StudyController usage.
/// </summary>
public class UIStudyPanel : MonoBehaviour
{
    [Header("Text References")]
    public TextMeshPro Header;
    public TextMeshPro ProgressText;
    public TextMeshPro TimeText;
    public TextMeshPro AttemptsText;
    public TextMeshPro HeadPathText;
    public TextMeshPro WarningText;

    [Header("Button References")]
    public Interactable BtnUndo;
    public Interactable BtnRedo;
    public Interactable BtnReset;
    public Interactable BtnNext;

    [Header("Likert Rating")]
    [Tooltip("Single-choice toggle group used for the post-task rating.")]
    public InteractableToggleCollection LikertCollection;

    private const string HeaderFormat = "Task {0:00} / {1:00}";
    private const string TimeFormat = "Time: {0:00}:{1:00}:{2:00}";
    private const string AttemptsFormat = "Attempts: {0}";
    private const string HeadPathFormat = "Head Path: {0:F1} m";
    private const string ProgressFormat = "Progress: {0} {1}/{2}";

    private const char FilledProgressChar = '¡ö';
    private const char EmptyProgressChar = '¡õ';

    #region Public API

    public void BindHeader(int idx, int total)
    {
        if (Header == null)
        {
            return;
        }

        Header.text = string.Format(HeaderFormat, Mathf.Max(0, idx), Mathf.Max(0, total));
    }

    public void SetTime(float seconds)
    {
        if (TimeText == null)
        {
            return;
        }

        float safeSeconds = Mathf.Max(0f, seconds);
        int hours = Mathf.FloorToInt(safeSeconds / 3600f);
        int minutes = Mathf.FloorToInt((safeSeconds % 3600f) / 60f);
        int wholeSeconds = Mathf.FloorToInt(safeSeconds % 60f);

        TimeText.text = string.Format(TimeFormat, hours, minutes, wholeSeconds);
    }

    public void SetAttempts(int attempts)
    {
        if (AttemptsText == null)
        {
            return;
        }

        AttemptsText.text = string.Format(AttemptsFormat, Mathf.Max(0, attempts));
    }

    public void SetHeadPath(float meters)
    {
        if (HeadPathText == null)
        {
            return;
        }

        HeadPathText.text = string.Format(HeadPathFormat, Mathf.Max(0f, meters));
    }

    public void SetProgress(int filled, int total, bool compact = false)
    {
        if (ProgressText == null)
        {
            return;
        }

        int safeTotal = Mathf.Max(1, total);
        int safeFilled = Mathf.Clamp(filled, 0, safeTotal);

        // The compact flag is kept for API compatibility with StudyController.
        // The current research-release panel uses a single-line presentation in both modes.
        ProgressText.text = string.Format(
            ProgressFormat,
            BuildProgressBar(safeFilled, safeTotal),
            safeFilled,
            safeTotal);
    }

    public void ShowWarning(string msg)
    {
        if (WarningText == null)
        {
            return;
        }

        string safeMessage = string.IsNullOrEmpty(msg) ? string.Empty : msg;
        WarningText.text = safeMessage;
        WarningText.gameObject.SetActive(!string.IsNullOrEmpty(safeMessage));
    }

    public void SetButtons(bool undoEnabled, bool redoEnabled, bool resetEnabled, bool nextEnabled)
    {
        SetButtonState(BtnUndo, undoEnabled);
        SetButtonState(BtnRedo, redoEnabled);
        SetButtonState(BtnReset, resetEnabled);
        SetButtonState(BtnNext, nextEnabled);
    }

    public void ResetLikert()
    {
        if (LikertCollection == null)
        {
            return;
        }

        LikertCollection.CurrentIndex = -1;
    }

    public bool TryGetLikert(out int score)
    {
        score = 0;

        if (LikertCollection == null)
        {
            return false;
        }

        int currentIndex = LikertCollection.CurrentIndex;
        if (currentIndex < 0)
        {
            return false;
        }

        // Convert MRTK's zero-based index to a 1-5 rating.
        score = currentIndex + 1;
        return true;
    }

    public void ClearAllButtonListeners()
    {
        BtnUndo?.OnClick.RemoveAllListeners();
        BtnRedo?.OnClick.RemoveAllListeners();
        BtnReset?.OnClick.RemoveAllListeners();
        BtnNext?.OnClick.RemoveAllListeners();
    }

    public void InitVisualState()
    {
        ShowWarning(string.Empty);
        SetButtons(false, false, true, false);
        ResetLikert();
    }

    #endregion

    #region Helpers

    private static void SetButtonState(Interactable button, bool isEnabled)
    {
        if (button == null)
        {
            return;
        }

        button.IsEnabled = isEnabled;
    }

    private static string BuildProgressBar(int filled, int total)
    {
        int safeTotal = Mathf.Max(1, total);
        int safeFilled = Mathf.Clamp(filled, 0, safeTotal);

        StringBuilder builder = new StringBuilder(safeTotal + 2);
        builder.Append('[');

        for (int i = 0; i < safeTotal; i++)
        {
            builder.Append(i < safeFilled ? FilledProgressChar : EmptyProgressChar);
        }

        builder.Append(']');
        return builder.ToString();
    }

    #endregion
}
