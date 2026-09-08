using System;

namespace OnlyWar.Helpers
{
    // Verbosity levels for GameLog, coarsest (Error) to finest (Trace). Off disables all logging.
    // Higher value = more detail; a configured MinimumLevel emits everything at that level or coarser.
    public enum GameLogLevel
    {
        Off = 0,
        Error = 1,
        Warn = 2,
        Info = 3,
        Debug = 4,
        Trace = 5
    }

    // Headless-safe, level-configurable logging seam for the turn/battle simulation. Like BattleLog,
    // engine code under Helpers must never call Godot natives (GD.Print) directly — that
    // access-violates when the simulation runs outside the Godot runtime (unit tests, headless balance
    // tuning of the opening scenario). The Godot UI wires Sink (and picks MinimumLevel) at startup; in
    // tests/headless a diagnostic wires its own sink. When no sink is set, or a message is below
    // MinimumLevel, a call is a cheap early-out.
    //
    // Messages are supplied as Func<string> and built lazily, so a suppressed level costs only an int
    // comparison — instrumentation can be left in hot paths (per battle, per mission) without paying
    // string-formatting cost when logging is off.
    public static class GameLog
    {
        // Everything at this level or coarser is emitted; finer detail is suppressed. Defaults to Off
        // so production and the test suite are silent unless a caller opts in.
        public static GameLogLevel MinimumLevel { get; set; } = GameLogLevel.Off;

        public static Action<GameLogLevel, string> Sink { get; set; }

        public static bool IsEnabled(GameLogLevel level)
        {
            return Sink != null
                && level != GameLogLevel.Off
                && level <= MinimumLevel;
        }

        public static void Write(GameLogLevel level, Func<string> message)
        {
            if (!IsEnabled(level)) return;
            Sink(level, message());
        }

        public static void Error(Func<string> message) => Write(GameLogLevel.Error, message);
        public static void Warn(Func<string> message) => Write(GameLogLevel.Warn, message);
        public static void Info(Func<string> message) => Write(GameLogLevel.Info, message);
        public static void Debug(Func<string> message) => Write(GameLogLevel.Debug, message);
        public static void Trace(Func<string> message) => Write(GameLogLevel.Trace, message);
    }
}
