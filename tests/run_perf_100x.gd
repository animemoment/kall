@tool
extends SceneTree

# Замеры нагрузки 100x (headless). Пишет отчёт в _reports/perf_100x.md.
# Запуск: godot --headless --path . --script res://tests/run_perf_100x.gd

func _init() -> void:
	print("[PERF100X] старт стенда...")
	var bench = load("res://src/tools/Perf100xBench.cs")
	if bench == null:
		printerr("[PERF100X] не удалось загрузить Perf100xBench.cs")
		quit(1)
		return

	var inst = bench.new()
	if inst == null:
		printerr("[PERF100X] не удалось создать экземпляр стенда")
		quit(1)
		return
	var report = inst.RunAll()
	if report == null or report.length() < 50:
		printerr("[PERF100X] стенд вернул пустой отчёт")
		quit(1)
		return

	var path = "res://_reports/perf_100x.md"
	var f = FileAccess.open(path, FileAccess.WRITE)
	if f == null:
		printerr("[PERF100X] не смог открыть " + path)
		quit(1)
		return
	f.store_string(report)
	f.close()

	print("[PERF100X] отчёт записан: " + path + " (" + str(report.length()) + " символов)")
	print(report)
	quit(0)