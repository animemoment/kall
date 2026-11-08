extends SceneTree

# StyleBoxBatcher (F3 draw calls): раскладка и стили не меняются, шрифт F3 без subpixel.
# Запуск: godot --headless --path . --script res://tests/run_stylebox_batcher.gd
# Запускаемся из первого кадра: в _init дерево сцены ещё не готово (root == null).

var _done := false

func _process(_delta: float) -> bool:
	if _done:
		return true
	_done = true
	var t = load("res://tests/TestStyleBoxBatcher.cs").new()
	var ok = t.Run(self)
	quit(0 if ok else 1)
	return true
