extends SceneTree

# Инспектор клетки: замер рассинхрона подсистем на границе 1024.
# Запуск: godot --headless --path . --script res://tests/run_cell_inspector.gd

func _init():
	print("[INSPECTOR] CellInspector — расхождения подсистем через кромку 1024")
	var t = load("res://src/tools/CellInspector.cs").new()

	print("")
	print("=== Карточки клеток (y=512) ===")
	for x in [1000, 1020, 1023, 1024, 1030, 1050]:
		print(t.Inspect(x, 512))

	print("")
	print("=== Линия по X: 1000..1060 при y=512 ===")
	print(t.ProbeLine(1000, 1060, 512))

	print("")
	print("=== Линия по Y: 1000..1060 при x=512 ===")
	print(t.ProbeColumn(1000, 1060, 512))

	print("")
	print("=== Далеко за островом (стриминг) ===")
	for x in [2048, 4096]:
		print(t.Inspect(x, 512))

	quit(0)