extends SceneTree

## CC-01: a farm plot with a ground item on the east/south half of the island must still
## be cleared and must get its till job (the clearance no longer uses a hardcoded 512
## window; the boundary comes from WorldModel.IslandTiles).
## Run: godot --headless --path . --script res://tests/run_farm_clearance.gd

func _init() -> void:
	print("[GD] Running C# TestFarmClearance...")
	var test_script = load("res://tests/TestFarmClearance.cs")
	if test_script == null:
		printerr("[GD ERR] Failed to load TestFarmClearance.cs")
		quit(1)
		return

	var test = test_script.new()
	if test == null:
		printerr("[GD ERR] Failed to instantiate TestFarmClearance")
		quit(1)
		return

	if not test.has_method("Run"):
		printerr("[GD ERR] TestFarmClearance has no Run() method")
		quit(1)
		return

	var res = test.Run()
	if res is bool and not res:
		printerr("[GD ERR] TestFarmClearance reported failure!")
		quit(1)
		return
	if res is int and res != 0:
		printerr("[GD ERR] TestFarmClearance returned exit code: %d" % res)
		quit(res)
		return

	print("[GD OK] TestFarmClearance passed with Code 0.")
	quit(0)