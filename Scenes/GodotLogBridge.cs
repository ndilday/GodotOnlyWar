using Godot;
using OnlyWar.Helpers;
using System;
using System.Globalization;
using System.IO;
using System.Text;

// Autoload that routes the headless-safe engine log seams (BattleLog/GameLog) to a durable log file
// for the entire process lifetime. Engine code under Helpers never calls GD.Print directly so it can
// run headless (tests, balance tuning); the sinks have no destination until something wires them.
//
// This lives in an autoload rather than MainGameScene._Ready so the seams are already live during
// new-game generation (GameDataSingleton.InitializeNewGameData), which runs in StartMenu and the
// preview bootstrap *before* the main scene enters the tree. Wired in _EnterTree (earlier than
// _Ready) so nothing generated at startup is dropped on the floor.
public partial class GodotLogBridge : Node
{
    private DurableLogSink _logSink;

    public override void _EnterTree()
    {
        _logSink = DurableLogSink.Create();

        BattleLog.Sink = message => _logSink.Write("Battle", message);
        // Leveled turn/battle trace. Set MinimumLevel lower (Info/Warn) to quiet it, or Off to
        // silence entirely; Trace surfaces per-battle sizes/timings, force generation, per-week costs.
        GameLog.Sink = (level, message) =>
        {
            _logSink.Write(level.ToString(), message);

            if (level == GameLogLevel.Error)
            {
                GD.PushError(message);
            }
            else if (level == GameLogLevel.Warn)
            {
                GD.PushWarning(message);
            }
        };
        GameLog.MinimumLevel = GameLogLevel.Trace;

        GD.Print($"OnlyWar log sink: {_logSink.LogPath}");
    }

    public override void _ExitTree()
    {
        BattleLog.Sink = null;
        GameLog.Sink = null;
        _logSink?.Dispose();
        _logSink = null;
    }

    private sealed class DurableLogSink : IDisposable
    {
        private const long MaxLogBytes = 25L * 1024L * 1024L;
        private const int FlushEveryLines = 100;

        private readonly object _sync = new object();
        private readonly string _logDirectory;
        private readonly string _sessionStamp;
        private StreamWriter _writer;
        private int _fileIndex;
        private int _pendingFlushLines;

        private DurableLogSink(string logDirectory, string sessionStamp)
        {
            _logDirectory = logDirectory;
            _sessionStamp = sessionStamp;
            OpenNextFile();
        }

        public string LogPath { get; private set; }

        public static DurableLogSink Create()
        {
            string logDirectory = ProjectSettings.GlobalizePath("user://logs");
            Directory.CreateDirectory(logDirectory);

            string sessionStamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            return new DurableLogSink(logDirectory, sessionStamp);
        }

        public void Write(string channel, string message)
        {
            lock (_sync)
            {
                if (_writer.BaseStream.Length >= MaxLogBytes)
                {
                    OpenNextFile();
                }

                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
                _writer.Write(timestamp);
                _writer.Write(" [");
                _writer.Write(channel);
                _writer.Write("] ");
                _writer.WriteLine(message);

                _pendingFlushLines++;
                if (_pendingFlushLines >= FlushEveryLines)
                {
                    FlushLocked();
                }
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                FlushLocked();
                _writer?.Dispose();
                _writer = null;
            }
        }

        private void OpenNextFile()
        {
            _writer?.Dispose();

            string suffix = _fileIndex == 0
                ? string.Empty
                : "-" + _fileIndex.ToString("D2", CultureInfo.InvariantCulture);
            LogPath = Path.Combine(_logDirectory, "game-trace-" + _sessionStamp + suffix + ".log");

            Stream stream = new System.IO.FileStream(
                LogPath,
                FileMode.Create,
                System.IO.FileAccess.Write,
                FileShare.ReadWrite);
            _writer = new StreamWriter(stream, new UTF8Encoding(false), 65536)
            {
                AutoFlush = false
            };
            _fileIndex++;
            _pendingFlushLines = 0;
        }

        private void FlushLocked()
        {
            _writer?.Flush();
            _pendingFlushLines = 0;
        }
    }
}
