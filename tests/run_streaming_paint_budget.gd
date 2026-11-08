extends SceneTree

# 01-01/01-02: StreamingWorldView per-frame paint budget (cells, not chunks) and the
# island gate that keeps world-layer mountains/decor off the MapRenderer island.
# The real frame cost is measured in the live game (SetCell cost lives in the render
# server, which is empty in --headless), so the C# test pins the pure decisions and
# their wiring, plus a behavioural check of IsMountainAt on an injected chunk.
# Run: godot --headless --path . --script res://tests/run_streaming_paint_budget.gd

func _init() -> void:
	var script = load("res://tests/TestStreamingPaintBudget.cs")
	if script == null:
		printerr("[GD ERR] Failed to load tests/TestStreamingPaintBudget.cs")
		quit(1)
		return

	var test = script.new()
	if test == null or not test.has_method("Run"):
		printerr("[GD ERR] TestStreamingPaintBudget has no Run()")
		quit(1)
		return

	quit(0 if test.Run() else 1)