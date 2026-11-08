extends SceneTree

# Патфайндер по миру: путь через старую границу 1024, связность, достижение цели.
# Запуск: godot --headless --path . --script res://tests/run_world_pathfinder.gd

func _init():
	var t = load("res://tests/TestWorldPathfinder.cs").new()
	var ok = t.Run()
	quit(0 if ok else 1)