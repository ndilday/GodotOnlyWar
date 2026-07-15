using Godot;
using OnlyWar.Helpers.Settings;
using OnlyWar.Helpers.Turns;
using OnlyWar.Helpers.UI.SystemMenu;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

/// <summary>
/// Headless release smoke for the campaign-control wiring that cannot be exercised by xUnit.
/// It drives real buttons through the preview bootstrap and exits the process with code 1 when a
/// required node, subscription, or overlay transition is missing.
/// </summary>
public partial class ReleaseSceneWiringSmoke : Node
{
    private const int TransitionFrameLimit = 12;
    private readonly List<string> _failures = [];
    private string _warningPreferencesPath;
    private bool _warningPreferencesExisted;
    private byte[] _warningPreferencesContents;

    public override async void _Ready()
    {
        try
        {
            PrepareDeterministicWarningPreferences();
            await RunSmoke();
        }
        catch (Exception exception)
        {
            _failures.Add($"Unhandled smoke exception: {exception}");
        }
        finally
        {
            RestoreWarningPreferences();
        }

        if (_failures.Count == 0)
        {
            GD.Print("RELEASE SCENE WIRING SMOKE: PASS");
            GetTree().Quit(0);
            return;
        }

        GD.PushError($"RELEASE SCENE WIRING SMOKE: FAIL ({_failures.Count} failure(s))");
        foreach (string failure in _failures)
        {
            GD.PushError($"  - {failure}");
        }
        GetTree().Quit(1);
    }

    private async Task RunSmoke()
    {
        await ValidateOverlayScenesLoad();

        PackedScene bootstrapScene = LoadScene(
            "res://Scenes/Debug/main_game_preview_bootstrap.tscn");
        if (bootstrapScene == null)
        {
            return;
        }

        Node bootstrap = bootstrapScene.Instantiate();
        AddChild(bootstrap);
        await NextFrame();
        await NextFrame();

        MainGameScene mainGame = FindDescendant<MainGameScene>(bootstrap);
        if (!Require(mainGame != null,
                "Preview bootstrap did not instantiate MainGameScene."))
        {
            return;
        }

        RequireNode<Node>(mainGame, "SectorMap");
        RequireNode<CanvasLayer>(mainGame, "UILayer");
        RequireNode<TopMenu>(mainGame, "UILayer/TopMenu");
        RequireNode<BottomMenu>(mainGame, "UILayer/BottomMenu");
        RequireNode<SystemInspector>(mainGame, "UILayer/SystemInspector");
        RequireNode<ActivityOverlay>(mainGame, "UILayer/ActivityOverlay");

        CloseOpeningBriefingIfPresent(mainGame);
        await NextFrame();

        Button systemOptionsButton = RequireNode<Button>(mainGame,
            "UILayer/TopMenu/Panel/MarginContainer/CommandRow/RightSection/SystemOptionsButton");
        if (systemOptionsButton == null)
        {
            return;
        }

        // Escape is owned globally by the campaign root, even when gameplay overlays have focus.
        SendKey(mainGame, Key.Escape);
        SystemMenuController systemMenu = await WaitForVisible<SystemMenuController>(mainGame);
        if (Require(systemMenu != null, "Escape did not open the global System Menu."))
        {
            SendKey(mainGame, Key.Escape);
            await NextFrame();
            Require(!systemMenu.Visible, "Escape did not close the open System Menu.");
        }

        // A visible menu after pressing the real button proves both TopMenu's button-to-event
        // wiring and MainGameScene's event subscriber.
        Press(systemOptionsButton);
        systemMenu = await WaitForVisible<SystemMenuController>(mainGame);
        if (!Require(systemMenu != null,
                "System Options has no effective subscriber: System Menu did not open."))
        {
            return;
        }

        CheckBox idleSquads = RequireNode<CheckBox>(systemMenu,
            "Panel/Margin/Content/WarningPanel/Margin/Warnings/IdleSquads");
        CheckBox taskForces = RequireNode<CheckBox>(systemMenu,
            "Panel/Margin/Content/WarningPanel/Margin/Warnings/TaskForces");
        CheckBox specialMissions = RequireNode<CheckBox>(systemMenu,
            "Panel/Margin/Content/WarningPanel/Margin/Warnings/SpecialMissions");
        Require(idleSquads?.ButtonPressed == true
                && taskForces?.ButtonPressed == true
                && specialMissions?.ButtonPressed == true,
            "System Menu did not reflect the enabled global warning preferences.");

        Button systemSave = RequireNode<Button>(systemMenu,
            "Panel/Margin/Content/Actions/SaveButton");
        Press(systemSave);
        SaveLoadChooserController chooser =
            await WaitForVisible<SaveLoadChooserController>(mainGame);
        Require(chooser != null,
            "System Menu Save has no effective subscriber: chooser did not open.");
        if (chooser != null)
        {
            Require(chooser.Mode == SaveChooserMode.Save,
                $"Save opened chooser in {chooser.Mode} mode instead of Save mode.");
            Press(RequireNode<Button>(chooser,
                "Panel/Margin/Content/Header/CloseButton"));
            await NextFrame();
        }

        systemMenu = await EnsureSystemMenuVisible(mainGame, systemOptionsButton);
        if (systemMenu != null)
        {
            Button systemLoad = RequireNode<Button>(systemMenu,
                "Panel/Margin/Content/Actions/LoadButton");
            Press(systemLoad);
            chooser = await WaitForVisible<SaveLoadChooserController>(mainGame);
            Require(chooser != null,
                "System Menu Load has no effective subscriber: chooser did not open.");
            if (chooser != null)
            {
                Require(chooser.Mode == SaveChooserMode.Load,
                    $"Load opened chooser in {chooser.Mode} mode instead of Load mode.");
                Press(RequireNode<Button>(chooser,
                    "Panel/Margin/Content/Header/CloseButton"));
                await NextFrame();
            }
        }

        systemMenu = await EnsureSystemMenuVisible(mainGame, systemOptionsButton);
        if (systemMenu != null)
        {
            Press(RequireNode<Button>(systemMenu,
                "Panel/Margin/Content/ExportButton"));
            DiagnosticsExportDialog diagnostics =
                await WaitForVisible<DiagnosticsExportDialog>(mainGame);
            if (Require(diagnostics != null,
                    "Export Diagnostic Bundle has no effective subscriber."))
            {
                LineEdit destination = RequireNode<LineEdit>(diagnostics,
                    "Panel/Margin/Content/DestinationRow/Destination");
                destination.GrabFocus();
                diagnostics._UnhandledKeyInput(CreateKeyEvent(Key.X));
                await NextFrame();
                Require(diagnostics.Visible,
                    "Typing X in the diagnostic destination field closed the dialog.");

                RequireNode<Button>(diagnostics,
                    "Panel/Margin/Content/DestinationRow/ChooseButton")?.GrabFocus();
                diagnostics._UnhandledKeyInput(CreateKeyEvent(Key.X));
                await NextFrame();
                Require(!diagnostics.Visible,
                    "X did not close the diagnostic dialog when no text field had focus.");
            }
        }

        systemMenu = await EnsureSystemMenuVisible(mainGame, systemOptionsButton);
        if (systemMenu != null)
        {
            Press(RequireNode<Button>(systemMenu,
                "Panel/Margin/Content/Actions/ResumeButton"));
            await NextFrame();
            Require(!systemMenu.Visible, "Resume did not close System Menu.");
        }

        Button endTurnButton = RequireNode<Button>(mainGame,
            "UILayer/BottomMenu/Panel/MarginContainer/HBoxContainer/EndTurnButton");
        EndTurnPreflightReport expectedPreflight = EndTurnPreflight.Evaluate(
            OnlyWar.Models.GameDataSingleton.Instance.Sector,
            new EndTurnWarningPreferences());
        if (Require(expectedPreflight.RequiresConfirmation,
                "Preview campaign unexpectedly contains no End Turn attention; refusing to resolve a full turn."))
        {
            Press(endTurnButton);
            EndTurnPreflightDialog preflight =
                await WaitForVisible<EndTurnPreflightDialog>(mainGame);
            Require(preflight != null,
                "End Turn has no effective subscriber or skipped attention: preflight did not open.");
            if (preflight != null)
            {
                RequireNode<Button>(preflight,
                    "DialogView/PreflightPanel/ContentMargin/Layout/ActionRow/ProceedButton");
                SendKey(mainGame, Key.X);
                await NextFrame();
                Require(!preflight.Visible, "X did not close the End Turn preflight dialog.");
            }
        }

        RemoveChild(bootstrap);
        bootstrap.Free();
        await NextFrame();
        await ValidateTitleControls();
    }

    private async Task ValidateTitleControls()
    {
        PackedScene titleScene = LoadScene("res://Scenes/StartMenu/StartMenu.tscn");
        if (titleScene == null)
        {
            return;
        }

        StartMenu title = titleScene.Instantiate<StartMenu>();
        AddChild(title);
        await NextFrame();

        Button loadButton = RequireNode<Button>(title, "MenuButtons/LoadGameButton");
        Press(loadButton);
        SaveLoadChooserController chooser =
            await WaitForVisible<SaveLoadChooserController>(title);
        if (Require(chooser != null,
                "Title Load has no effective subscriber: chooser did not open."))
        {
            Require(chooser.Mode == SaveChooserMode.Load,
                $"Title Load opened chooser in {chooser.Mode} mode instead of Load mode.");
            Press(RequireNode<Button>(chooser,
                "Panel/Margin/Content/Header/CloseButton"));
            await NextFrame();
        }

        Press(RequireNode<Button>(title, "MenuButtons/OptionsButton"));
        SystemMenuController options =
            await WaitForVisible<SystemMenuController>(title);
        if (Require(options != null,
                "Title Options has no effective subscriber: options did not open."))
        {
            Require(!RequireNode<Button>(options,
                    "Panel/Margin/Content/Actions/SaveButton").Visible,
                "Title Options exposed campaign-only Save controls.");
            Require(RequireNode<Label>(options,
                    "Panel/Margin/Content/Title").Text == "OPTIONS",
                "Title Options did not use title-screen context.");
            Press(RequireNode<Button>(options,
                "Panel/Margin/Content/Actions/ResumeButton"));
            await NextFrame();
            Require(!options.Visible, "Title Options Close did not dismiss the menu.");
        }

        RemoveChild(title);
        title.Free();
    }

    private void PrepareDeterministicWarningPreferences()
    {
        _warningPreferencesPath = ProjectSettings.GlobalizePath(
            EndTurnWarningPreferencesRepository.DefaultUserPath);
        _warningPreferencesExisted = File.Exists(_warningPreferencesPath);
        if (_warningPreferencesExisted)
        {
            _warningPreferencesContents = File.ReadAllBytes(_warningPreferencesPath);
        }

        new EndTurnWarningPreferencesRepository(_warningPreferencesPath).Save(
            new EndTurnWarningPreferences());
    }

    private void RestoreWarningPreferences()
    {
        if (string.IsNullOrWhiteSpace(_warningPreferencesPath))
        {
            return;
        }

        try
        {
            if (_warningPreferencesExisted)
            {
                string directory = Path.GetDirectoryName(_warningPreferencesPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                File.WriteAllBytes(_warningPreferencesPath, _warningPreferencesContents ?? []);
            }
            else if (File.Exists(_warningPreferencesPath))
            {
                File.Delete(_warningPreferencesPath);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            _failures.Add($"Could not restore warning preferences after smoke: {exception.Message}");
        }
    }

    private async Task ValidateOverlayScenesLoad()
    {
        (string Path, string[] RequiredNodes)[] overlays =
        [
            ("res://Scenes/SystemMenu/system_menu.tscn",
                ["Panel/Margin/Content/Actions/SaveButton", "Panel/Margin/Content/Actions/LoadButton"]),
            ("res://Scenes/SystemMenu/save_load_chooser.tscn",
                ["Panel/Margin/Content/Header/CloseButton", "Panel/Margin/Content/Footer/PrimaryButton"]),
            ("res://Scenes/MainGameScreen/end_turn_preflight_dialog.tscn",
                ["DialogView/PreflightPanel/ContentMargin/Layout/ActionRow/ProceedButton"]),
            ("res://Scenes/SystemMenu/destructive_navigation_dialog.tscn",
                ["Panel/Margin/Content/Buttons/CancelButton"]),
            ("res://Scenes/SystemMenu/diagnostics_export_dialog.tscn",
                ["Panel/Margin/Content/Buttons/ExportButton"]),
            ("res://Scenes/SystemMenu/transient_feedback_overlay.tscn",
                ["Panel/Margin/Message"])
        ];

        foreach ((string path, string[] requiredNodes) in overlays)
        {
            PackedScene packed = LoadScene(path);
            if (packed == null)
            {
                continue;
            }

            Node instance = packed.Instantiate();
            AddChild(instance);
            await NextFrame();
            foreach (string requiredNode in requiredNodes)
            {
                Require(instance.GetNodeOrNull(requiredNode) != null,
                    $"{path} is missing required node '{requiredNode}'.");
            }
            RemoveChild(instance);
            instance.Free();
        }
    }

    private PackedScene LoadScene(string path)
    {
        PackedScene scene = GD.Load<PackedScene>(path);
        Require(scene != null, $"Could not load packed scene '{path}'.");
        return scene;
    }

    private async Task<SystemMenuController> EnsureSystemMenuVisible(
        Node root,
        Button systemOptionsButton)
    {
        SystemMenuController menu = FindDescendant<SystemMenuController>(root);
        if (menu?.Visible == true)
        {
            return menu;
        }

        Press(systemOptionsButton);
        menu = await WaitForVisible<SystemMenuController>(root);
        Require(menu != null, "System Menu could not be reopened after closing an overlay.");
        return menu;
    }

    private async Task<T> WaitForVisible<T>(Node root) where T : CanvasItem
    {
        for (int frame = 0; frame < TransitionFrameLimit; frame++)
        {
            T result = FindVisibleDescendant<T>(root);
            if (result != null)
            {
                return result;
            }
            await NextFrame();
        }
        return null;
    }

    private async Task NextFrame()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private T RequireNode<T>(Node root, string path) where T : Node
    {
        T node = root?.GetNodeOrNull<T>(path);
        Require(node != null,
            $"{root?.Name ?? "<null>"} is missing required {typeof(T).Name} at '{path}'.");
        return node;
    }

    private bool Require(bool condition, string failure)
    {
        if (!condition)
        {
            _failures.Add(failure);
        }
        return condition;
    }

    private void CloseOpeningBriefingIfPresent(Node root)
    {
        BriefingDialogController briefing = FindVisibleDescendant<BriefingDialogController>(root);
        if (briefing != null)
        {
            Press(briefing.GetNodeOrNull<Button>("DialogView/CloseButton"));
        }
    }

    private static void Press(Button button)
    {
        button?.EmitSignal(BaseButton.SignalName.Pressed);
    }

    private static void SendKey(MainGameScene mainGame, Key key)
    {
        mainGame?._Input(CreateKeyEvent(key));
    }

    private static InputEventKey CreateKeyEvent(Key key)
    {
        return new InputEventKey
        {
            Keycode = key,
            PhysicalKeycode = key,
            Pressed = true
        };
    }

    private static T FindVisibleDescendant<T>(Node root) where T : CanvasItem
    {
        foreach (T candidate in FindDescendants<T>(root))
        {
            if (candidate.Visible)
            {
                return candidate;
            }
        }
        return null;
    }

    private static T FindDescendant<T>(Node root) where T : Node
    {
        foreach (T candidate in FindDescendants<T>(root))
        {
            return candidate;
        }
        return null;
    }

    private static IEnumerable<T> FindDescendants<T>(Node root) where T : Node
    {
        if (root == null)
        {
            yield break;
        }

        if (root is T match)
        {
            yield return match;
        }

        foreach (Node child in root.GetChildren())
        {
            foreach (T descendant in FindDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }
}
