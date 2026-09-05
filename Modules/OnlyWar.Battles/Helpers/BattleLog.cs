using System;

namespace OnlyWar.Helpers
{
    // Logging seam for the headless-runnable battle/turn engine. Engine code under Helpers must not
    // call Godot natives (e.g. GD.Print) directly: doing so access-violates when the simulation runs
    // outside the Godot runtime (unit tests, headless balance tuning of the opening scenario — see
    // Design/Reference/OpeningScenario.md). The Godot UI wires Sink to GD.Print at startup; when no
    // sink is set (tests, headless) writes are a no-op.
    public static class BattleLog
    {
        public static Action<string> Sink { get; set; }
        public static bool IsEnabled => Sink != null;

        // Optional routing seam for a host that wants one file per battle. The engine announces
        // battle boundaries; where those lines land is entirely the host's business, so a host that
        // wires only Sink (tests, headless balance runs) keeps the single-stream behaviour and
        // needs no changes. Names come from BattleTurnResolver and are already filename-safe.
        public static Action<string> BattleStarted { get; set; }
        public static Action BattleEnded { get; set; }

        public static void Write(string text)
        {
            Sink?.Invoke(text);
        }

        public static void BeginBattle(string battleName)
        {
            BattleStarted?.Invoke(battleName);
        }

        public static void EndBattle()
        {
            BattleEnded?.Invoke();
        }
    }
}
