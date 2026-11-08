extends SceneTree

## 08-08: independent verification of the farm clearance spill bounds.
## The farm call must use WorldModel.IslandTiles instead of the old half-island window.
## Run: godot --headless --path . --script res://tests/run_spill_bounds.gd

func _init() -> void:
	print("[GD] Running C# TestSpillBounds...")
	var test_script = load("res://tests/TestSpillBounds.cs")
	if test_script == null:
		printerr("[GD ERR] Failed to load TestSpillBounds.cs")
		quit(1)
		return

	var test = test_script.new()
	if test == null:
		printerr("[GD ERR] Failed to instantiate TestSpillBounds")
		quit(1)
		return

	if not test.has_method("Run"):
		printerr("[GD ERR] TestSpillBounds has no Run() method")
		quit(1)
		return

	var result = test.Run()
	if result is bool and not result:
		printerr("[GD ERR] TestSpillBounds reported failure!")
		quit(1)
		return

	print("[GD OK] TestSpillBounds passed with Code 0.")
	quit(0)