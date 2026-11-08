@tool
extends SceneTree

func _ready() -> void:
	print("[GD] Running J-05 headless verification (C# static API)...")
	print()

	var jp = load("res://src/simulation/jobs/JobSpatialPyramid.cs")
	if jp == null:
		printerr("[FAIL] Could not load JobSpatialPyramid.cs")
		quit(1)

	var idx600 = jp.GetChunkIndexStatic(600, 0)
	var idx616 = jp.GetChunkIndexStatic(616, 0)
	var idx1000 = jp.GetChunkIndexStatic(1000, 0)

	print(" -> GetChunkIndexStatic(600,0)  = ", idx600, " (new=37, old=31)")
	print(" -> GetChunkIndexStatic(616,0)  = ", idx616, " (new=38, old=31)")
	print(" -> GetChunkIndexStatic(1000,0) = ", idx1000, " (new=62, old=1023)")
	print(" -> ChunkDim=", jp.ChunkDim, " ChunkCount=", jp.ChunkCount, " ChunkGridDim=", jp.ChunkGridDim)
	print()

	if jp.ChunkDim != 64:
		printerr("[FAIL] ChunkDim=", jp.ChunkDim, " expected 64 — J-05 not fixed!")
		quit(1)
	if jp.ChunkCount != 4096:
		printerr("[FAIL] ChunkCount=", jp.ChunkCount, " expected 4096 — J-05 not fixed!")
		quit(1)
	if idx600 == idx616:
		printerr("[FAIL] 600 and 616 map to SAME chunk=", idx600, " — J-05 still present!")
		quit(1)
	if idx1000 != 62:
		printerr("[FAIL] 1000,0 maps to chunk ", idx1000, " (expected 62; old=1023)")
		quit(1)

	print("[PASS] J-05 FIXED: full 1024x1024 island (ChunkDim=64, no clamping). 600,616->37,38; 1000->62.")
	quit(0)
