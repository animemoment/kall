extends SceneTree

# 11-03: готовый объект за кромкой (Wall/Building/Stockpile/Crop) обязан сноситься
# инструментом, а не только его чертёж.
# Запуск: godot --headless --path . --script res://tests/run_world_demolish.gd

func _init():
	var t = load("res://tests/TestWorldDemolish.cs").new()
	quit(0 if t.Run() else 1)