extends SceneTree

# 07-09: completing a wall must eject only the agent that really stands in that cell.
# An agent that already left (stale spatial grid / CurrentCell cache) must not be teleported,
# and no ejection may land outside the island (edge 1020..1028, negative cells).
# Run: godot --headless --path . --script res://tests/run_wall_eject.gd

func _init():
	var t = load("res://tests/TestWallEject.cs").new()
	quit(0 if t.Run() else 1)