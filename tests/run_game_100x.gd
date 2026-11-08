@tool
extends SceneTree

# Замер РЕАЛЬНОЙ игровой сцены на 100x (headless): грузим Main.tscn, ставим 100x,
# ждём, затем читаем GameProfiler (реальные фазы игры) и печатаем сводку.
# Запуск: godot --headless --path . --script res://tests/run_game_100x.gd

var _t := 0.0
var _started := false
var _scene: Node = null
var _bench = null

func _init() -> void:
	print("[GAME100X] загрузка Main.tscn ...")
	var ps = load("res://scenes/main/Main.tscn")
	if ps == null:
		printerr("[GAME100X] не удалось загрузить Main.tscn")
		quit(1)
		return
	_scene = ps.instantiate()
	if _scene == null:
		printerr("[GAME100X] не удалось инстанцировать Main")
		quit(1)
		return
	_bench = load("res://src/tools/Perf100xBench.cs").new()
	root.add_child(_scene)
	print("[GAME100X] сцена добавлена, ждём инициализации")

func _process(delta: float) -> bool:
	_t += delta
	if not _started and _t > 3.0:
		_started = true
		if _bench != null:
			_bench.SetSpeed(100)
			var region = load("res://src/core/world/generation/UnifiedWorldGenerator.cs")
			var ok = _bench.SelectSpawn(24, 12)
			print("[GAME100X] выбор спавна программно: " + str(ok))
		else:
			print("[GAME100X] мост недоступен")
		return false
	if _started and _t > 23.0:
		if _bench != null:
			print(_bench.DumpPhases(20.0, 1200))
			print(_bench.DumpDispatchCounters())
		print("[GAME100X] готово")
		quit(0)
		return true
	return false