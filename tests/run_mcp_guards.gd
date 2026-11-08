extends SceneTree

# 29-01 / 29-07: remove_node и process_mode {mode:"disabled"} больше не могут
# удалить/остановить живую игру одной командой.
# Запуск: godot --headless --path . --script res://tests/run_mcp_guards.gd

func _init():
	var t = load("res://tests/TestMcpGuards.cs").new()
	quit(0 if t.Run() else 1)