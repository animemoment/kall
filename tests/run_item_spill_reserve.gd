extends SceneTree

# 08-03: перенос кучи возвращает резерв в тотал (нет фантомного дефицита).
# Запуск: godot --headless --path . --script res://tests/run_item_spill_reserve.gd

func _init():
	var t = load("res://tests/TestItemSpillReserve.cs").new()
	quit(0 if t.Run() else 1)