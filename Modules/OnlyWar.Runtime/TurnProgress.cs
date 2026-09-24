namespace OnlyWar.Runtime
{
    // A single line describing what end-of-turn work is running now. The turn engine writes it from
    // whichever thread resolves the turn; a host UI polls it on its own schedule. There are no events
    // and no callbacks, so the engine never runs host code and the host decides how often to redraw.
    // Writes are cheap enough to make per battle turn.
    public sealed class TurnProgress
    {
        private volatile string _current = string.Empty;

        public string Current => _current;

        public void Report(string status)
        {
            _current = status ?? string.Empty;
        }
    }
}
