extends SceneTree

# Живой прогон follow-окна террейна в Main.tscn: камера в центр острова, потом за кромку.
# Запуск: godot --headless --path . --script res://tests/run_terrain_follow.gd

var _started := false

func _process(_delta: float) -> bool:
	if not _started:
		_started = true
		var main = load("res://scenes/main/Main.tscn").instantiate()
		root.add_child(main)
		var probe = load("res://tests/TestTerrainFollow.cs").new()
		root.add_child(probe)
	return false