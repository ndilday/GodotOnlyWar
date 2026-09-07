using Godot;
using OnlyWar.Application;
using System;

public partial class DiplomacyScreenController : MainScreenController
{
    private IDiplomacyScreenApplication _application;
    private DiplomacyScreenView _view;

    public override void _Ready()
    {
        base._Ready();
        _view = GetNode<DiplomacyScreenView>("DiplomacyScreenView");
        PopulateRequestData();
    }

    public void Configure(IDiplomacyScreenApplication application)
    {
        if (_application != null)
        {
            _application.SessionChanged -= OnSessionChanged;
        }

        _application = application;
        if (_application != null)
        {
            _application.SessionChanged += OnSessionChanged;
        }
        PopulateRequestData();
    }

    private void OnSessionChanged(object sender, EventArgs e) => PopulateRequestData();

    public void PopulateRequestData()
    {
        if (_view == null || _application == null) return;

        _view.PopulateRequestTree(_application.QueryDiplomacy().Entries);
    }

    /// <summary>
    /// Selects a petition requested by another workspace without changing its owning-surface
    /// rendering or inventing a second request-detail view.
    /// </summary>
    public void FocusRequest(int requestId)
    {
        _view?.FocusRequest(requestId);
    }
}
