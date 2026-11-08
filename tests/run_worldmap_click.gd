extends SceneTree

# 22-01/12-02: клик по мировой карте вне режима спавна не выбирает регион.
# Запуск: godot --headless --path . --script res://tests/run_worldmap_click.gd

func _init():
	var t = load("res://tests/TestWorldMapClick.cs").new()
	quit(0 if t.Run() else 1)