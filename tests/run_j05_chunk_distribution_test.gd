@tool
extends SceneTree

func _init() -> void:
	print("[GD] Running C# TestJ05ChunkDistribution...")
	var test_script = load("res://tests/TestJ05ChunkDistribution.cs")
	if test_script == null:
		printerr("[GD ERR] Failed to load TestJ05ChunkDistribution.cs")
		quit(1)
		return

	var test = test_script.new()
	if test == null:
		printerr("[GD ERR] Failed to instantiate TestJ05ChunkDistribution")
		quit(1)
		return

	if not test.has_method("Run"):
		printerr("[GD ERR] TestJ05ChunkDistribution has no Run() method")
		quit(1)
		return

	var res = test.Run()
	if res is bool and not res:
		printerr("[GD ERR] TestJ05ChunkDistribution reported failure!")
		quit(1)
		return

	print("[GD OK] TestJ05ChunkDistribution passed with Code 0.")
	quit(0)
