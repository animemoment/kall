extends SceneTree

# Кэш стоимости прохода: согласованность с миром, дальние координаты, скорость.
# Запуск: godot --headless --path . --script res://tests/run_world_cost_cache.gd

func _init():
	var t = load("res://tests/TestWorldCostCache.cs").new()
	var ok = t.Run()
	quit(0 if ok else 1)