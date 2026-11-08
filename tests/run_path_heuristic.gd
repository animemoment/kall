extends SceneTree

# 05-07 / 05-03 / 05-04: октильная эвристика (допустимость), окно мира с целью
# на выровненном краю, фолбэк единственного портала кромки.
# Запуск: godot --headless --path . --script res://tests/run_path_heuristic.gd

func _init():
	var t = load("res://tests/TestPathHeuristic.cs").new()
	var ok = t.Run()
	quit(0 if ok else 1)