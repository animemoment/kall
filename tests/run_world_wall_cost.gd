extends SceneTree

# 14-02 / R-019: стена за кромкой обязана попадать в стоимость мира (WorldCostCache).
# Запуск: godot --headless --path . --script res://tests/run_world_wall_cost.gd

func _init():
	var t = load("res://tests/TestWorldWallCost.cs").new()
	var ok = t.Run()
	quit(0 if ok else 1)