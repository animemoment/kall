@tool
extends SceneTree

func _init() -> void:
	print("[GD] Running C# TestRegionSwitchKeep (12-01)...")
	var test_script = load("res://tests/TestRegionSwitchKeep.cs")
	if test_script == null:
		printerr("[GD ERR] Failed to load TestRegionSwitchKeep.cs")
		quit(1)
		return

	var test = test_script.new()
	if test == null:
		printerr("[GD ERR] Failed to instantiate TestRegionSwitchKeep")
		quit(1)
		return

	if not test.has_method("Run"):
		printerr("[GD ERR] TestRegionSwitchKeep has no Run() method")
		quit(1)
		return

	var res = test.Run()
	if res is bool and not res:
		printerr("[GD ERR] TestRegionSwitchKeep reported failure!")
		quit(1)
		return

	print("[GD OK] TestRegionSwitchKeep passed with Code 0.")
	quit(0)