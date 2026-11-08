extends SceneTree

# 15-03/16-01: стены блокируют агентов после загрузки.
# Запуск: godot --headless --path . --script res://tests/run_solid_walls_restore.gd

func _init():
	var t = load("res://tests/TestSolidWallsRestore.cs").new()
	quit(0 if t.Run() else 1)