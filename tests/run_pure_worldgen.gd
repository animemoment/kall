extends SceneTree

# Запуск тестов нового чистого генератора мира (PureWorldGenerator)
# Запуск: godot --headless --path . --script res://tests/run_pure_worldgen.gd

func _init():
	var t = load("res://tests/TestPureWorldGen.cs").new()
	var ok = t.Run()
	quit(0 if ok else 1)
