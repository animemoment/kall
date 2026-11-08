extends SceneTree

# Коды поверхности terrain_map: 6 типов, без схлопывания степи/ледника/гор в траву.
# Запуск: godot --headless --path . --script res://tests/run_terrain_surface_code.gd

func _init():
	var t = load("res://tests/TestTerrainSurfaceCode.cs").new()
	var ok = t.Run()
	quit(0 if ok else 1)