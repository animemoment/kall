@tool
extends SceneTree

## 17-01 + 17-05: маска грядок/леса для влаги и плодородия следует за миром,
## FarmZoneManager.Adapt больше не копирует HashSet тайлов на каждый вызов.
## Запуск: godot --headless --path . --script res://tests/run_farm_mask.gd

func _init() -> void:
	print("[GD] Running C# TestFarmMask...")
	var test_script = load("res://tests/TestFarmMask.cs")
	if test_script == null:
		printerr("[GD ERR] Failed to load TestFarmMask.cs")
		quit(1)
		return

	var test = test_script.new()
	if test == null:
		printerr("[GD ERR] Failed to instantiate TestFarmMask")
		quit(1)
		return

	if not test.has_method("Run"):
		printerr("[GD ERR] TestFarmMask has no Run() method")
		quit(1)
		return

	var res = test.Run()
	if res is bool and not res:
		printerr("[GD ERR] TestFarmMask reported failure!")
		quit(1)
		return
	if res is int and res != 0:
		printerr("[GD ERR] TestFarmMask returned exit code: %d" % res)
		quit(res)
		return

	print("[GD OK] TestFarmMask passed with Code 0.")
	quit(0)