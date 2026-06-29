using System;

namespace OnlyWar.Helpers
{
    // Logging seam for the headless-runnable battle/turn engine. Engine code under Helpers must not
    // call Godot natives (e.g. GD.Print) directly: doing so access-violates when the simulation runs
    // outside the Godot runtime (unit tests, headless balance tuning of the opening scenario — see
    // Design/OpeningScenario.md §8/§12). The Godot UI wires Sink to GD.Print at startup; when no
    // sink is set (tests, headless) writes are a no-op.
    public static class BattleLog
    {
        public static Action<string> Sink { get; set; }

        public static void Write(string text)
        {
            Sink?.Invoke(text);
        }
    }
}
