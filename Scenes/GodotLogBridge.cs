using Godot;
using OnlyWar.Helpers;

// Autoload that routes the headless-safe engine log seams (BattleLog/GameLog) to the Godot console
// for the entire process lifetime. Engine code under Helpers never calls GD.Print directly so it can
// run headless (tests, balance tuning); the sinks have no destination until something wires them.
//
// This lives in an autoload rather than MainGameScene._Ready so the seams are already live during
// new-game generation (GameDataSingleton.InitializeNewGameData), which runs in StartMenu and the
// preview bootstrap *before* the main scene enters the tree. Wired in _EnterTree (earlier than
// _Ready) so nothing generated at startup is dropped on the floor.
public partial class GodotLogBridge : Node
{
    public override void _EnterTree()
    {
        BattleLog.Sink = GD.Print;
        // Leveled turn/battle trace. Set MinimumLevel lower (Info/Warn) to quiet it, or Off to
        // silence entirely; Trace surfaces per-battle sizes/timings, force generation, per-week costs.
        GameLog.Sink = (level, message) => GD.Print($"[{level}] {message}");
        GameLog.MinimumLevel = GameLogLevel.Trace;
    }
}
