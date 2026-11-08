extends SceneTree

# Сейм-инспектор: замер расхождений на стыке «остров колонии ↔ стриминговый мир».
#
# Запуск (headless):
#   godot --headless --path . --script res://tests/run_seam_inspector.gd
#
# Чтобы проверить РЕАЛЬНЫЙ мир игрока, задайте окружение:
#   SEAM_SEED      — WorldModel.Seed (десятичное или 0x...)
#   SEAM_ORIGIN_X / SEAM_ORIGIN_Y — UnifiedWorldGenerator.WorldOriginX/Y
#   SEAM_LINE      — координата линии скана (по умолчанию 512)
#
# Пример:
#   $env:SEAM_SEED="777"; $env:SEAM_ORIGIN_X="123456"; $env:SEAM_ORIGIN_Y="654320" (кратен 16 — S-04 требует)
#   godot --headless --path . --script res://tests/run_seam_inspector.gd

func _init():
	print("[SEAM] SeamInspector — стык острова и стримингового мира")
	var t = load("res://src/tools/SeamInspector.cs").new()

	var seed_env := OS.get_environment("SEAM_SEED")
	if seed_env != "":
		t.SetSeed(_parse_int(seed_env))

	var ox := OS.get_environment("SEAM_ORIGIN_X")
	var oy := OS.get_environment("SEAM_ORIGIN_Y")
	if ox != "" and oy != "":
		t.SetWorldOrigin(_parse_int(ox), _parse_int(oy))

	var line := 512
	var line_env := OS.get_environment("SEAM_LINE")
	if line_env != "":
		line = _parse_int(line_env)

	print(t.Info())

	print("")
	print("=== Полоса X: 1000..1060 при y=%d ===" % line)
	print(t.ProbeLine(1000, 1060, line))

	print("")
	print("=== Полоса Y: 1000..1060 при x=%d ===" % line)
	print(t.ProbeColumn(1000, 1060, line))

	print("")
	print("=== Карточки клеток стыка ===")
	for x in [1022, 1023, 1024, 1025]:
		print(t.CellJson(x, line))

	print("")
	print("=== JSON-полоса 1020..1028 (машинный дифф) ===")
	print(t.Json(1020, 1028, line))

	quit(0)

func _parse_int(text: String) -> int:
	var s := text.strip_edges()
	if s.begins_with("0x") or s.begins_with("0X"):
		return s.substr(2).hex_to_int()
	return s.to_int()