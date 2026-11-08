extends SceneTree

# 19-01: FarmZoneRenderer рисовал зону поштучно (DrawRect на клетку + до 4 DrawLine),
# из-за чего большая ферма под курсором/выбранная дёргала игру. Тест проверяет батчинг
# геометрии, её кэш и то, что _Draw больше не обходит клетки.
# Запуск: godot --headless --path . --script res://tests/run_farm_zone_renderer.gd

func _init():
	var t = load("res://tests/TestFarmZoneRenderer.cs").new()
	quit(0 if t.Run() else 1)