extends SceneTree

# 04-02/04-03: таймер кормится той же дельтой, по которой считается время.
# Запуск: godot --headless --path . --script res://tests/run_sim_delta_timer.gd

func _init():
	var t = load("res://tests/TestSimDeltaTimer.cs").new()
	quit(0 if t.Run() else 1)