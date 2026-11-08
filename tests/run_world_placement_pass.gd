extends SceneTree

# 21-02: WorldPlacementPass.Run резолвит границу острова один раз на прогон,
# а не делегатом на каждую клетку (до 250 000 аллокаций за событие драга).
# Запуск: godot --headless --path . --script res://tests/run_world_placement_pass.gd

func _init():
	var t = load("res://tests/TestWorldPlacementPass.cs").new()
	quit(0 if t.Run() else 1)