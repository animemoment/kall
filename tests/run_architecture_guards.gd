extends SceneTree

# Стражи архитектуры: не дают вернуть «второй мир».
# Запуск: godot --headless --path . --script res://tests/run_architecture_guards.gd

func _init():
	var t = load("res://tests/TestArchitectureGuards.cs").new()
	var ok = t.Run()
	quit(0 if ok else 1)