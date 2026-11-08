extends SceneTree

# Сколько клеток различаются между версиями генератора (число, а не рассуждение).
# Запуск: godot --headless --path . --script res://tests/run_generator_version_parity.gd

func _init():
	var t = load("res://tests/TestGeneratorVersionParity.cs").new()
	var ok = t.Run()
	quit(0 if ok else 1)