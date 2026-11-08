@tool
extends SceneTree

# §123: тест моста «мировые планы → JobDispatcher» — агент работает за кромкой 1024.
# Запуск: godot --headless --path . --script tests/run_world_job_bridge_test.gd

func _init() -> void:
	print("[GD] Running test_world_job_bridge (§123: агент за кромкой)...")
	var ok := false
	var test = load("res://tests/test_world_job_bridge.cs").new()
	if test != null:
		ok = test.Run()
	if ok:
		print("[GD OK] test_world_job_bridge passed with Code 0.")
		quit(0)
	else:
		printerr("[GD ERR] test_world_job_bridge failed!")
		quit(1)
