extends SceneTree

# 14-03: якорь восстановления плодородия обязан переживать мост ActiveArea → MapData.
# Запуск: godot --headless --path . --script res://tests/run_fertility_anchor.gd

func _init():
	var t = load("res://tests/TestFertilityAnchor.cs").new()
	var ok = t.Run()
	quit(0 if ok else 1)