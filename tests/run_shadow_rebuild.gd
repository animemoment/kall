extends SceneTree

# 02-07: массовая рубка/добыча больше не пересобирает тени на каждое событие —
# одиночное изменение стоит O(1), пачка сливается в одну пересборку буфера.
# Запуск: godot --headless --path . --script res://tests/run_shadow_rebuild.gd

func _init() -> void:
	var script = load("res://tests/TestShadowRebuild.cs")
	if script == null:
		printerr("[GD ERR] Не загрузился tests/TestShadowRebuild.cs")
		quit(1)
		return

	var t = script.new()
	if t == null or not t.has_method("Run"):
		printerr("[GD ERR] TestShadowRebuild не создался или без Run()")
		quit(1)
		return

	quit(0 if t.Run() else 1)