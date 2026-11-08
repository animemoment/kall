extends SceneTree

# 08-07/08-02: снос склада не теряет входящие резервы и возвращает груз в мир,
# сценарий «стройка зовёт склад, склад зовёт стройку» не встаёт намертво.
# Запуск: godot --headless --path . --script res://tests/run_stockpile_locks.gd

func _init():
	var t = load("res://tests/TestStockpileLocks.cs").new()
	quit(0 if t.Run() else 1)