extends SceneTree

# Внешнее заполнение follow-окна террейна за кромкой: скорость и состав кодов.
# Запуск: godot --headless --path . --script res://tests/run_terrain_window_fill.gd

func _init():
	var t = load("res://tests/TestTerrainWindowFill.cs").new()
	var ok = t.Run()
	quit(0 if ok else 1)