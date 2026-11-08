extends SceneTree

# 05-02/07-02: клетки за границей карты возвращают false, а не падают по индексу.
# Запуск: godot --headless --path . --script res://tests/run_path_bounds.gd

func _init():
	var t = load("res://tests/TestPathBounds.cs").new()
	quit(0 if t.Run() else 1)