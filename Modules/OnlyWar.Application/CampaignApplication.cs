using System;
using OnlyWar.Helpers;
using OnlyWar.Helpers.Simulation;
using OnlyWar.Helpers.Storage;
using OnlyWar.Helpers.Turns;
using OnlyWar.Models;
using OnlyWar.Builders;

namespace OnlyWar.Application;

/// <summary>
/// Application boundary for campaign lifetime and cross-subsystem workflow coordination.
/// Generation, loading and turn resolution return detached session state first; only Install
/// publishes a successful result to the legacy host compatibility surface.
/// </summary>
public sealed class CampaignApplication
{
    private readonly IRNG _random;
    private GameSession _activeSession;

    public CampaignApplication(IRNG random)
    {
        _random = random ?? throw new ArgumentNullException(nameof(random));
    }

    public GameSession ActiveSession => _activeSession;

    /// <summary>
    /// Adopts the campaign already published by a legacy host bootstrap. This keeps scene
    /// replacement and preview flows on the explicit application session without regenerating
    /// or reloading the campaign.
    /// </summary>
    public GameSession AttachCurrentCampaign()
    {
        GameDataSingleton game = GameDataSingleton.Instance;
        if (!game.IsInitialized)
        {
            throw new InvalidOperationException("No campaign is currently loaded.");
        }

        _activeSession = new GameSession(
            game.GameRulesData,
            game.Sector,
            game.Date,
            _random)
        {
            UpgradePending = game.UpgradePending
        };
        return _activeSession;
    }

    public GameSession CreateNewCampaign(
        GameRulesData rules,
        Date date,
        string chapterName = null,
        int seed = 1,
        ScenarioFactionSelection invaderSelection = null)
    {
        if (rules == null) throw new ArgumentNullException(nameof(rules));
        if (date == null) throw new ArgumentNullException(nameof(date));

        Sector candidate = SectorBuilder.GenerateSector(
            seed,
            rules,
            date,
            Helpers.Application.Adapters.Generation.CandidateGenerationSupport.For(
                rules, date, _random),
            chapterName,
            invaderSelection);
        return new GameSession(rules, candidate, date, _random);
    }

    public GameSession LoadCampaign(string savePath) =>
        CampaignLoader.LoadSession(savePath, _random);

    public GameSession StartNewCampaign(
        GameRulesData rules,
        Date date,
        string chapterName = null,
        int seed = 1,
        ScenarioFactionSelection invaderSelection = null)
    {
        GameSession session = CreateNewCampaign(
            rules, date, chapterName, seed, invaderSelection);
        Install(session);
        return session;
    }

    public GameSession LoadAndInstall(string savePath)
    {
        GameSession session = LoadCampaign(savePath);
        Install(session);
        return session;
    }

    public void Install(GameSession session)
    {
        if (session == null) throw new ArgumentNullException(nameof(session));

        // Publish only after the detached session is fully constructed. If the compatibility
        // boundary rejects the install, the previous active session remains the application one.
        GameDataSingleton.Instance.Install(session);
        _activeSession = session;
        if (session.UpgradePending)
        {
            GameDataSingleton.Instance.Recoverability.BeginLoadedCampaign();
        }
        else
        {
            GameDataSingleton.Instance.Recoverability.BeginNewCampaign();
        }
    }

    public TurnResolutionResult AdvanceTurn(GameSession session = null)
    {
        GameSession target = session ?? _activeSession
            ?? throw new InvalidOperationException("No campaign session is active.");
        return new TurnController(target).ProcessTurn(target.Sector);
    }

    public void Save(string filePath, GameSession session = null)
    {
        GameSession target = session ?? _activeSession
            ?? throw new InvalidOperationException("No campaign session is active.");
        CurrentCampaignSaveWriter.Write(filePath, target);
    }

    public void Close()
    {
        _activeSession = null;
        GameDataSingleton.Instance.ClearCampaign();
    }
}
