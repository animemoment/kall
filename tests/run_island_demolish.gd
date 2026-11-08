extends SceneTree

# 21-01: RMB with a build tool demolishes FINISHED island structures (walls, furniture),
# clears the cell in the simulation walkability array (no phantom wall) and returns the
# build materials; the old plan/blueprint path keeps working.
# Run: godot --headless --path . --script res://tests/run_island_demolish.gd

func _init():
	var t = load("res://tests/TestIslandDemolish.cs").new()
	quit(0 if t.Run() else 1)