using Godot;
using System;

/// <summary>
/// Behavioral base for a full-workspace campaign surface. Main screens live inside the
/// application's primary content host; the persistent top and bottom navigation remain owned by
/// MainGameScene.
/// </summary>
public partial class MainScreenController : Control
{
    public event EventHandler CloseRequested;

    /// <summary>
    /// Requests navigation back from this workspace. Derived screens may override this to validate
    /// state before allowing the navigation.
    /// </summary>
    public virtual void RequestClose()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
