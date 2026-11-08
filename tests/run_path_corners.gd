extends SceneTree

# 05-06 / 07-04: one corner-cutting rule for the pathfinder and the agent step.
# Run: godot --headless --path . --script res://tests/run_path_corners.gd

func _init():
	var t = load("res://tests/TestPathCorners.cs").new()
	var ok = t.Run()
	quit(0 if ok else 1)