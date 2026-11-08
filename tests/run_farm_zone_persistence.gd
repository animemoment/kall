@tool
extends SceneTree

## 10-01/15-06/17-02: Farm-зоны (тайлы + AutoPlant) переживают сохранение/загрузку.
## Запуск: godot --headless --path . --script res://tests/run_farm_zone_persistence.gd

func _init() -> void:
	print("[GD] Running C# TestFarmZonePersistence...")
	var test_script = load("res://tests/TestFarmZonePersistence.cs")
	if test_script == null:
		printerr("[GD ERR] Failed to load TestFarmZonePersistence.cs")
		quit(1)
		return

	var test = test_script.new()
	if test == null:
		printerr("[GD ERR] Failed to instantiate TestFarmZonePersistence")
		quit(1)
		return

	if not test.has_method("Run"):
		printerr("[GD ERR] TestFarmZonePersistence has no Run() method")
		quit(1)
		return

	var res = test.Run()
	if res is bool and not res:
		printerr("[GD ERR] TestFarmZonePersistence reported failure!")
		quit(1)
		return
	if res is int and res != 0:
		printerr("[GD ERR] TestFarmZonePersistence returned exit code: %d" % res)
		quit(res)
		return

	print("[GD OK] TestFarmZonePersistence passed with Code 0.")
	quit(0)