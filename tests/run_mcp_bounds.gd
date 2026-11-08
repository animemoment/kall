extends SceneTree

# 25-02 / 29-14: timeout_ms у run_gdscript и счётчики scroll/terrain ограничены.
# Запуск: godot --headless --path . --script res://tests/run_mcp_bounds.gd

func _init():
	var t = load("res://tests/TestMcpBounds.cs").new()
	quit(0 if t.Run() else 1)