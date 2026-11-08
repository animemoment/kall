extends SceneTree

# Точность берега за кромкой: прямой сэмпл против решётки климата 16×16.
# Запуск: godot --headless --path . --script res://tests/run_coast_precision.gd

func _init():
	var t = load("res://tests/TestCoastPrecision.cs").new()
	var ok = t.Run()
	quit(0 if ok else 1)