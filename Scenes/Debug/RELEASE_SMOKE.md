# Release scene-wiring smoke

Run the Alpha 0.7.1 campaign-control wiring smoke with Godot 4.7 Mono:

```powershell
& 'C:\Projects\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe' --headless --path 'C:\Projects\GodotOnlyWar' 'res://Scenes/Debug/release_scene_wiring_smoke.tscn'
```

The runner uses `main_game_preview_bootstrap.tscn`, presses the real System Options, Save, Load,
Resume, Diagnostics, End Turn, title Load, and title Options buttons, and verifies each expected
overlay/mode. It also checks the campaign Escape toggle and X close contract, including preserving
X while a text field has focus. It exits with code `0` and prints
`RELEASE SCENE WIRING SMOKE: PASS` on success; missing nodes or inert actions exit with code `1`.
It cancels before turn resolution and restores the warning preferences it temporarily enables.
