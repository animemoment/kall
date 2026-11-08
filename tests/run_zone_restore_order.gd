extends SceneTree

# 10-02/15-04: зоны колонии не должны стираться при смене региона.
# Запуск: godot --headless --path . --script res://tests/run_zone_restore_order.gd

func _init():
	var t = load("res://tests/TestZoneRestoreOrder.cs").new()
	var ok = t.Run()
	quit(0 if ok else 1)