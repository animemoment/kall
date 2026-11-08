extends SceneTree

# 02-02 (P2): work progress must survive zoom-out -> zoom-in.
# Numeric model over the real WorkProgressTracker queue + pure render decisions,
# plus a source-text check that there is no one-way "hidden" flag/cache.
# Run: godot --headless --path . --script res://tests/run_job_progress_zoom.gd

func _init():
	var script = load("res://tests/TestJobProgressZoom.cs")
	if script == null:
		printerr("[GD ERR] Failed to load tests/TestJobProgressZoom.cs")
		quit(1)
		return

	var t = script.new()
	if t == null or not t.has_method("Run"):
		printerr("[GD ERR] TestJobProgressZoom was not created or has no Run()")
		quit(1)
		return

	quit(0 if t.Run() else 1)