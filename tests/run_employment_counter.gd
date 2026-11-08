extends SceneTree

# 28-01: честный счётчик занятости в HUD.
# Запуск: godot --headless --path . --script res://tests/run_employment_counter.gd

func _init():
	var t = load("res://tests/TestEmploymentCounter.cs").new()
	quit(0 if t.Run() else 1)