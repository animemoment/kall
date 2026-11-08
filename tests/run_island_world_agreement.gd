extends SceneTree

# Совпадает ли кэш острова (MapData) с генератором мира (WorldModel)?
# Это решающее измерение перед объединением рендереров.
# Запуск: godot --headless --path . --script res://tests/run_island_world_agreement.gd

func _init():
	var t = load("res://tests/TestIslandWorldAgreement.cs").new()
	var ok = t.Run()
	quit(0 if ok else 1)