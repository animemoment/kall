extends SceneTree

func _init():
	var t = load("res://tests/TestRegionZoneConflict.cs").new()
	quit(0 if t.Run() else 1)