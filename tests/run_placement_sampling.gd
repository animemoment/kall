@tool
extends SceneTree

func _init() -> void:
	print("[GD] Running TestPlacementSampling...")
	var test_script = load("res://tests/TestPlacementSampling.cs")
	if test_script == null:
		printerr("[GD ERR] Failed to load TestPlacementSampling.cs")
		quit(1)
		return

	var test = test_script.new()
	if test == null:
		printerr("[GD ERR] Failed to instantiate TestPlacementSampling")
		quit(1)
		return

	if not test.has_method("Run"):
		printerr("[GD ERR] TestPlacementSampling has no Run() method")
		quit(1)
		return

	var res = test.Run()
	if res is bool and not res:
		printerr("[GD ERR] TestPlacementSampling reported failure!")
		quit(1)
		return

	print("[GD OK] TestPlacementSampling passed with Code 0.")
	quit(0)