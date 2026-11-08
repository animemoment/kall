@tool
extends SceneTree

# §123-диагностика: полный флоу мирового плана через реальный путь инструмента.
func _init() -> void:
	print("[GD] Running test_world_plan_flow_diag...")
	var ok := false
	var test = load("res://tests/test_world_plan_flow_diag.cs").new()
	if test != null:
		ok = test.Run()
	if ok:
		quit(0)
	else:
		quit(1)
