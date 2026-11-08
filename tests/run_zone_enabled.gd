extends SceneTree

# 03-01/10-04: тумблер рабочей зоны доходит до зоны, диспатч её пропускает.
# Запуск: godot --headless --path . --script res://tests/run_zone_enabled.gd

func _init():
	var t = load("res://tests/TestZoneEnabled.cs").new()
	quit(0 if t.Run() else 1)