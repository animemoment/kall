extends SceneTree

# 04-01: коммит работ не должен зависеть от числа работ в индексе.
# Запуск: godot --headless --path . --script res://tests/run_commit_gate.gd

func _init():
	var t = load("res://tests/TestCommitGate.cs").new()
	quit(0 if t.Run() else 1)