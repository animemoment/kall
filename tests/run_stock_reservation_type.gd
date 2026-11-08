extends SceneTree

# 08-04: источник и тип резерва берутся из записи о резерве, а не из IsZoneTile.
# Запуск: godot --headless --path . --script res://tests/run_stock_reservation_type.gd

func _init():
	var t = load("res://tests/TestStockReservationType.cs").new()
	quit(0 if t.Run() else 1)