extends SceneTree

# Слои земли и воды в StreamingWorldView: вода определяется данными клетки, а не цветом тайла.
# Запуск: godot --headless --path . --script res://tests/run_streaming_water_layer.gd
# Запускаемся из первого кадра: в _init дерево сцены ещё не готово (root == null).

var _done := false

func _process(_delta: float) -> bool:
	if _done:
		return true
	_done = true
	var t = load("res://tests/TestStreamingWaterLayer.cs").new()
	var ok = t.Run(self)
	quit(0 if ok else 1)
	return true