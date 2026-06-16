using Godot;
using System;

public class NewGameSettings
{
    public string ChapterName { get; set; }
    public int Seed { get; set; }
}

public partial class NewGameSetupController : Control
{
    private const int MaxChapterNameLength = 40;

    private Panel _formPanel;
    private Panel _summaryPanel;
    private LineEdit _chapterNameEdit;
    private LineEdit _seedEdit;
    private Label _validationLabel;
    private RichTextLabel _summaryLabel;

    public event EventHandler<NewGameSettings> CampaignConfirmed;
    public event EventHandler Cancelled;

    public override void _Ready()
    {
        _formPanel = GetNode<Panel>("FormPanel");
        _summaryPanel = GetNode<Panel>("SummaryPanel");
        _chapterNameEdit = GetNode<LineEdit>("FormPanel/VBox/ChapterNameEdit");
        _seedEdit = GetNode<LineEdit>("FormPanel/VBox/SeedRow/SeedEdit");
        _validationLabel = GetNode<Label>("FormPanel/VBox/ValidationLabel");
        _summaryLabel = GetNode<RichTextLabel>("SummaryPanel/VBox/SummaryLabel");

        _chapterNameEdit.MaxLength = MaxChapterNameLength;

        GetNode<Button>("FormPanel/VBox/SeedRow/RandomizeButton").Pressed += OnRandomizePressed;
        GetNode<Button>("FormPanel/VBox/ButtonRow/BackButton").Pressed += OnBackToMenuPressed;
        GetNode<Button>("FormPanel/VBox/ButtonRow/ReviewButton").Pressed += OnReviewPressed;
        GetNode<Button>("SummaryPanel/VBox/ButtonRow/EditButton").Pressed += OnEditPressed;
        GetNode<Button>("SummaryPanel/VBox/ButtonRow/BeginButton").Pressed += OnBeginPressed;

        // Seed a default random value so the field is never empty on first view.
        _seedEdit.Text = GD.RandRange(0, int.MaxValue).ToString();
        _validationLabel.Text = "";
        ShowForm();
    }

    private void OnRandomizePressed()
    {
        _seedEdit.Text = GD.RandRange(0, int.MaxValue).ToString();
    }

    private void OnReviewPressed()
    {
        if (!TryBuildSettings(out NewGameSettings settings, out string error))
        {
            _validationLabel.Text = error;
            return;
        }

        _validationLabel.Text = "";
        _summaryLabel.Text =
            $"[b]Chapter:[/b] {settings.ChapterName}\n[b]Sector Seed:[/b] {settings.Seed}\n\n"
            + "The Chapter will be founded and the sector generated from this seed. "
            + "Re-using a seed reproduces the same sector.";
        ShowSummary();
    }

    private void OnEditPressed()
    {
        ShowForm();
    }

    private void OnBeginPressed()
    {
        if (!TryBuildSettings(out NewGameSettings settings, out string error))
        {
            // Should not happen (review already validated), but guard against edited state.
            ShowForm();
            _validationLabel.Text = error;
            return;
        }
        CampaignConfirmed?.Invoke(this, settings);
    }

    private void OnBackToMenuPressed()
    {
        Cancelled?.Invoke(this, EventArgs.Empty);
    }

    private bool TryBuildSettings(out NewGameSettings settings, out string error)
    {
        settings = null;
        string name = _chapterNameEdit.Text?.Trim() ?? "";
        if (name.Length == 0)
        {
            error = "Enter a name for your Chapter.";
            return false;
        }
        if (!int.TryParse(_seedEdit.Text?.Trim(), out int seed))
        {
            error = "The sector seed must be a whole number.";
            return false;
        }

        settings = new NewGameSettings { ChapterName = name, Seed = seed };
        error = "";
        return true;
    }

    private void ShowForm()
    {
        _formPanel.Visible = true;
        _summaryPanel.Visible = false;
    }

    private void ShowSummary()
    {
        _formPanel.Visible = false;
        _summaryPanel.Visible = true;
    }
}
