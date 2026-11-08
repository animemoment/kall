extends SceneTree

# 08-05/08-06/08-11: инварианты склада — неудачный депозит не теряет предметы,
# клетка под стеной не считается свободным слотом, MaxStack при загрузке не превышается.
# Запуск: godot --headless --path . --script res://tests/run_stockpile_invariants.gd

func _init():
	var t = load("res://tests/TestStockpileInvariants.cs").new()
	quit(0 if t.Run() else 1)