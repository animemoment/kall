extends SceneTree
func _init():
	var t = load("res://tests/TestSoilTooltipOffIsland.cs").new()
	quit(0 if t.Run() else 1)
