# КАРТА ЗАВИСИМОСТЕЙ ПРОЕКТА: D:\bebebe-main (1)\bebebe-main
Всего файлов: 301, Связей: 1983
============================================================

[_tools/probe_menu.gd] (GDScript, LoC: 8, I: 1.0)
  → Исходящие (1): autoload/mcp_interaction_server.gd

[addons/godot_ai/dock_panels/log_viewer.gd] (GDScript, LoC: 94, I: 0.5)
  (изолированный файл)

[addons/godot_ai/handlers/_resource_values.gd] (GDScript, LoC: 571, I: 0.25)
  ← Входящие (3): addons/godot_ai/handlers/node_handler.gd, addons/godot_ai/handlers/resource_handler.gd, addons/godot_ai/tests/test_cycle2_coercion.gd
  → Исходящие (1): addons/godot_ai/handlers/_value_coercion.gd

[addons/godot_ai/handlers/_value_coercion.gd] (GDScript, LoC: 390, I: 0.0)
  ← Входящие (3): addons/godot_ai/handlers/node_handler.gd, addons/godot_ai/handlers/_resource_values.gd, addons/godot_ai/tests/test_cycle2_coercion.gd

[addons/godot_ai/handlers/node_handler.gd] (GDScript, LoC: 745, I: 0.67)
  ← Входящие (1): addons/godot_ai/tests/test_cycle2_coercion.gd
  → Исходящие (2): addons/godot_ai/handlers/_value_coercion.gd, addons/godot_ai/handlers/_resource_values.gd

[addons/godot_ai/handlers/resource_handler.gd] (GDScript, LoC: 392, I: 0.67)
  ← Входящие (1): addons/godot_ai/tests/test_cycle2_coercion.gd
  → Исходящие (2): addons/godot_ai/handlers/_resource_values.gd, scenes/ui/Build.tscn

[addons/godot_ai/tests/test_cycle2_coercion.gd] (GDScript, LoC: 113, I: 1.0)
  → Исходящие (4): addons/godot_ai/handlers/_value_coercion.gd, addons/godot_ai/handlers/_resource_values.gd, addons/godot_ai/handlers/node_handler.gd, addons/godot_ai/handlers/resource_handler.gd

[addons/live_bridge/live_bridge.gd] (GDScript, LoC: 584, I: 0.5)
  (изолированный файл)

[autoload/agent_tools.gd] (GDScript, LoC: 315, I: 0.5)
  (изолированный файл)

[autoload/mcp_interaction_server.gd] (GDScript, LoC: 5119, I: 0.0)
  ← Входящие (1): _tools/probe_menu.gd

[autoload/perf_probe.gd] (GDScript, LoC: 413, I: 0.5)
  (изолированный файл)

[scenes/infinite_world/infinite_world_game_demo.tscn] (Scene, LoC: 3, I: 0.5)
  (изолированный файл)

[scenes/main/Main.cs] (C#, LoC: 763, I: 0.95)
  ← Входящие (4): src/ui/controllers/MainMenuController.cs, tests/Phase4StressTestRunner.cs, tests/TestWorldGen.cs, tests/TestWorldGenSeamAudit.cs
  → Исходящие (76): src/core/world/activearea/ActiveArea.cs, src/core/world/activearea/ActiveAreaRegistry.cs, src/ui/renderers/AgentRenderer.cs, src/simulation/AgentSimulationThread.cs, src/simulation/building/BlueprintManager.cs, src/simulation/building/BuildingManager.cs, src/ui/CameraController.cs, src/simulation/structures/ConstructionManager.cs, src/simulation/farming/CropGrowthManager.cs, src/ui/renderers/CropRenderer.cs, src/ui/DayNightCycle.cs, src/simulation/items/DemandRegistry.cs, src/simulation/farming/FarmJobManager.cs, src/ui/renderers/FarmZoneRenderer.cs, src/simulation/agents/FlowFieldManager.cs, src/core/common/GameProfiler.cs, src/simulation/items/GroundItemManager.cs, src/ui/renderers/GroundItemRenderer.cs, src/ui/controllers/HUDController.cs, src/simulation/pathfinding/HierarchicalPathfinder.cs, src/core/world/mutation/IWorldMutationSink.cs, src/core/world/mutation/IslandDeltaApplicator.cs, src/core/world/mutation/IslandDeltaApplicator.cs, src/core/world/mutation/IslandMutationMirror.cs, src/core/world/mutation/IslandOverrideStore.cs, src/core/world/mutation/IslandStateReplay.cs, src/core/world/mutation/IslandStateSnapshot.cs, src/ui/renderers/ItemShadowRenderer.cs, src/simulation/jobs/JobDispatcher.cs, src/simulation/jobs/JobPriorityManager.cs, scenes/main/Main.tscn, src/core/world/MapData.cs, src/core/world/activearea/MapDataShim.cs, src/ui/renderers/MapRenderer.cs, src/ui/PerformanceOverlay.cs, src/ui/PlayerInteractionManager.cs, src/core/common/ProfilerLogService.cs, src/ui/SelectionBox.cs, src/core/common/SimEvents.cs, src/simulation/items/StockpileManager.cs, src/simulation/stones/StoneJobManager.cs, src/ui/streaming/StreamingToolPlacement.cs, src/core/world/streaming/integration/StreamingWorldManager.cs, src/ui/streaming/StreamingWorldView.cs, src/simulation/structures/StructureManager.cs, src/simulation/TimeManager.cs, src/simulation/trees/TreeJobManager.cs, src/core/world/generation/UnifiedWorldGenerator.cs, src/simulation/building/WorkProgressTracker.cs, src/core/world/climate/WorldClimateProvider.cs, src/core/world/streaming/integration/WorldGameRules.cs, scenes/ui/WorldMap.tscn, src/ui/streaming/WorldMapOverlay.cs, src/core/world/streaming/coordinator/WorldSaveCoordinator.cs, src/simulation/WorldTime.cs, src/simulation/zones/Zone.cs, src/simulation/zones/ZoneManager.cs, src/simulation/zones/ZonePersistence.cs, src/core/common/CpuTopology.cs, src/core/common/ResourceRef.cs, src/core/common/ToolRequirement.cs, src/core/world/BiomeMapper.cs, src/core/world/BiomeType.cs, src/core/world/generation/WorldGenV2.cs, src/core/world/streaming/integration/ChunkMutations.cs, src/core/world/streaming/integration/IBlockWorld.cs, src/core/world/streaming/integration/IWallWorld.cs, src/core/world/mutation/IslandDomainReplay.cs, src/core/world/mutation/IslandPayload.cs, src/simulation/CellProgressStore.cs, src/simulation/DynamicWorkBalancer.cs, src/simulation/GameSpeed.cs, src/ui/ICellProbeOverlay.cs, src/ui/controllers/BuildMenuController.cs, src/ui/controllers/FarmController.cs, src/ui/streaming/WorldJobBoard.cs

[scenes/main/Main.tscn] (Scene, LoC: 753, I: 0.0)
  ← Входящие (6): scenes/main/Main.cs, src/ui/controllers/MainMenuController.cs, tests/Phase4StressTestRunner.cs, tests/TestWorldGen.cs, tests/TestWorldGenSeamAudit.cs, tests/test_phase0.gd

[scenes/ui/Build.tscn] (Scene, LoC: 165, I: 0.0)
  ← Входящие (4): addons/godot_ai/handlers/resource_handler.gd, src/ui/controllers/BuildMenuController.cs, tests/smoke_build_menu.gd, tests/test_phase0.gd

[scenes/ui/Farm.tscn] (Scene, LoC: 148, I: 0.0)
  ← Входящие (8): src/simulation/farming/FarmZoneManagerFacade.cs, src/simulation/zones/Zone.cs, src/simulation/zones/ZoneManager.cs, src/ui/controllers/FarmController.cs, src/ui/tools/BuildTool.cs, tests/Phase1StressTestRunner.cs, tests/Phase5StressTestRunner.cs, tests/test_zone_model.cs

[scenes/ui/Garden.tscn] (Scene, LoC: 3, I: 0.5)
  (изолированный файл)

[scenes/ui/JobDisplay.tscn] (Scene, LoC: 231, I: 0.0)
  ← Входящие (3): src/ui/controllers/WorkZoneController.cs, tests/test_phase0.gd, tests/test_streamingview_jobdisplay_smoke.cs

[scenes/ui/Prioritet.tscn] (Scene, LoC: 191, I: 0.0)
  ← Входящие (3): src/ui/controllers/HUDController.cs, src/ui/controllers/WorkZoneController.cs, tests/test_streamingview_jobdisplay_smoke.cs

[scenes/ui/WorldMap.tscn] (Scene, LoC: 36, I: 0.0)
  ← Входящие (2): scenes/main/Main.cs, tests/TestWorldGen.cs

[scenes/ui/hud.tscn] (Scene, LoC: 469, I: 0.5)
  (изолированный файл)

[scenes/ui/menu.tscn] (Scene, LoC: 1092, I: 0.0)
  ← Входящие (1): tests/test_phase1.gd

[scenes/ui/villager.tscn] (Scene, LoC: 54, I: 0.5)
  (изолированный файл)

[src/core/common/CpuTopology.cs] (C#, LoC: 27, I: 0.0)
  ← Входящие (111): scenes/main/Main.cs, src/core/common/GameProfiler.cs, src/core/world/activearea/ActiveArea.cs, src/core/world/activearea/MapDataShim.cs, src/core/world/generation/UnifiedWorldGenerator.cs, src/simulation/AgentSimulationThread.cs, src/simulation/SimulationContext.cs, src/simulation/agents/AgentMovementService.cs, src/simulation/agents/AgentSpatialGrid.cs, src/simulation/agents/FlatIdleWorkerPool.cs, src/simulation/agents/FlowFieldManager.cs, src/simulation/agents/IdleWorkerSpatialGrid.cs, src/simulation/agents/WorkerAssignmentSystem.cs, src/simulation/agents/WorkerZoneJobSystem.cs, src/simulation/building/BlueprintManager.cs, src/simulation/building/BuildConfig.cs, src/simulation/building/BuildingManager.cs, src/simulation/building/BuildObstacles.cs, src/simulation/building/ConstructionPipeline.cs, src/simulation/building/TileClearanceService.cs, src/simulation/building/WallBuildManager.cs, src/simulation/building/WorkProgressTracker.cs, src/simulation/farming/CropGrowthManager.cs, src/simulation/farming/FarmZone.cs, src/simulation/gpu/GpuAgentTransformSimulator.cs, src/simulation/gpu/GpuFlowFieldSimulator.cs, src/simulation/gpu/GpuHumiditySimulator.cs, src/simulation/items/DemandRegistry.cs, src/simulation/items/GroundItemManager.cs, src/simulation/items/ResourceBroker.cs, src/simulation/items/StockpileManager.cs, src/simulation/jobs/GenericJobSpatialIndex.cs, src/simulation/jobs/IJobHandler.cs, src/simulation/jobs/JobBroker.cs, src/simulation/jobs/JobDispatcher.cs, src/simulation/jobs/JobPriorityManager.cs, src/simulation/jobs/JobRegistry.cs, src/simulation/jobs/JobSpatialPyramid.cs, src/simulation/jobs/JobValidator.cs, src/simulation/jobs/handlers/BlueprintDeliveryJobHandler.cs, src/simulation/jobs/handlers/ConstructionJobHandler.cs, src/simulation/jobs/handlers/FarmingJobHandler.cs, src/simulation/jobs/handlers/HarvestJobHandler.cs, src/simulation/jobs/handlers/MiningJobHandler.cs, src/simulation/jobs/handlers/NeedsJobSystem.cs, src/simulation/jobs/handlers/PlantingJobHandler.cs, src/simulation/jobs/handlers/StockpileHaulingJobHandler.cs, src/simulation/jobs/handlers/TreeChoppingJobHandler.cs, src/simulation/jobs/handlers/WanderJobSystem.cs, src/simulation/offline/OfflineAgentNeeds.cs, src/simulation/offline/OfflineWorldTicker.cs, src/simulation/pathfinding/HierarchicalPathfinder.cs, src/simulation/scheduling/DynamicWorkScheduler.cs, src/simulation/simd/SimdMovementBatch.cs, src/simulation/simd/SimdNeedsBatch.cs, src/simulation/structures/ConstructionManager.cs, src/simulation/structures/ConstructionSite.cs, src/simulation/structures/StructureDefinition.cs, src/simulation/structures/StructureInstance.cs, src/simulation/structures/StructureManager.cs, src/simulation/structures/StructureRegistry.cs, src/simulation/zones/WorkZone.cs, src/simulation/zones/WorkZoneManagerFacade.cs, src/simulation/zones/Zone.cs, src/simulation/zones/ZoneCoverageMap.cs, src/simulation/zones/ZoneJobRouter.cs, src/simulation/zones/ZoneManager.cs, src/ui/PerformanceOverlay.cs, src/ui/PlayerInteractionManager.cs, src/ui/controllers/BuildMenuController.cs, src/ui/controllers/HUDController.cs, src/ui/controllers/PriorityBoardController.cs, src/ui/controllers/WallMaterialRegistry.cs, src/ui/controllers/WorkZoneController.cs, src/ui/renderers/AgentRenderer.cs, src/ui/renderers/CropRenderer.cs, src/ui/renderers/FertilityOverlayRenderer.cs, src/ui/renderers/GroundItemRenderer.cs, src/ui/renderers/HumidityOverlayRenderer.cs, src/ui/renderers/MapRenderer.cs, src/ui/renderers/ShadowCasterRenderer.cs, src/ui/renderers/StockpileItemRenderer.cs, src/ui/tools/BuildTool.cs, src/ui/tools/FarmingTool.cs, src/ui/tools/StoneMiningTool.cs, src/ui/tools/TreeFellingTool.cs, src/ui/tools/ZoneDraftTool.cs, tests/BenchJobAssignment.cs, tests/Phase2StressTestRunner.cs, tests/Phase3StressTestRunner.cs, tests/Phase4StressTestRunner.cs, tests/Phase5StressTestRunner.cs, tests/TerrainWindowBench.cs, tests/TestActiveArea.cs, tests/TestAdversarialConstruction.cs, tests/TestAdversarialJobDistribution.cs, tests/TestDestroyerStreaming.cs, tests/TestDestroyerWorldGen.cs, tests/TestGeneratorBench.cs, tests/TestIslandMirror.cs, tests/TestIslandZonePersistence.cs, tests/TestJobRaceStress.cs, tests/TestRenderLodBenchmark.cs, tests/TestWorkProgressPersistence.cs, tests/TestWorldGen.cs, tests/TestWorldGenSeamAudit.cs, tests/TestWorldGenV2.cs, tests/test_central_block_zone.cs, tests/test_streamingview_decor_seam.cs, tests/test_zone_agents.cs, tests/test_zone_router.cs

[src/core/common/GameProfiler.cs] (C#, LoC: 356, I: 0.02)
  ← Входящие (115): scenes/main/Main.cs, src/core/common/ProfilerLogService.cs, src/core/common/ProfilerLogService.cs, src/core/common/ProfilerLogService.cs, src/core/world/activearea/ActiveArea.cs, src/core/world/activearea/MapDataShim.cs, src/core/world/generation/UnifiedWorldGenerator.cs, src/simulation/AgentSimulationThread.cs, src/simulation/SimulationContext.cs, src/simulation/agents/AgentMovementService.cs, src/simulation/agents/AgentSpatialGrid.cs, src/simulation/agents/FlatIdleWorkerPool.cs, src/simulation/agents/FlowFieldManager.cs, src/simulation/agents/IdleWorkerSpatialGrid.cs, src/simulation/agents/WorkerAssignmentSystem.cs, src/simulation/agents/WorkerZoneJobSystem.cs, src/simulation/building/BlueprintManager.cs, src/simulation/building/BuildConfig.cs, src/simulation/building/BuildingManager.cs, src/simulation/building/BuildObstacles.cs, src/simulation/building/ConstructionPipeline.cs, src/simulation/building/TileClearanceService.cs, src/simulation/building/WallBuildManager.cs, src/simulation/building/WorkProgressTracker.cs, src/simulation/farming/CropGrowthManager.cs, src/simulation/farming/FarmZone.cs, src/simulation/gpu/GpuAgentTransformSimulator.cs, src/simulation/gpu/GpuFlowFieldSimulator.cs, src/simulation/gpu/GpuHumiditySimulator.cs, src/simulation/items/DemandRegistry.cs, src/simulation/items/GroundItemManager.cs, src/simulation/items/ResourceBroker.cs, src/simulation/items/StockpileManager.cs, src/simulation/jobs/GenericJobSpatialIndex.cs, src/simulation/jobs/IJobHandler.cs, src/simulation/jobs/JobBroker.cs, src/simulation/jobs/JobDispatcher.cs, src/simulation/jobs/JobPriorityManager.cs, src/simulation/jobs/JobRegistry.cs, src/simulation/jobs/JobSpatialPyramid.cs, src/simulation/jobs/JobValidator.cs, src/simulation/jobs/handlers/BlueprintDeliveryJobHandler.cs, src/simulation/jobs/handlers/ConstructionJobHandler.cs, src/simulation/jobs/handlers/FarmingJobHandler.cs, src/simulation/jobs/handlers/HarvestJobHandler.cs, src/simulation/jobs/handlers/MiningJobHandler.cs, src/simulation/jobs/handlers/NeedsJobSystem.cs, src/simulation/jobs/handlers/PlantingJobHandler.cs, src/simulation/jobs/handlers/StockpileHaulingJobHandler.cs, src/simulation/jobs/handlers/TreeChoppingJobHandler.cs, src/simulation/jobs/handlers/WanderJobSystem.cs, src/simulation/offline/OfflineAgentNeeds.cs, src/simulation/offline/OfflineWorldTicker.cs, src/simulation/pathfinding/HierarchicalPathfinder.cs, src/simulation/scheduling/DynamicWorkScheduler.cs, src/simulation/simd/SimdMovementBatch.cs, src/simulation/simd/SimdNeedsBatch.cs, src/simulation/structures/ConstructionManager.cs, src/simulation/structures/ConstructionSite.cs, src/simulation/structures/StructureDefinition.cs, src/simulation/structures/StructureInstance.cs, src/simulation/structures/StructureManager.cs, src/simulation/structures/StructureRegistry.cs, src/simulation/zones/WorkZone.cs, src/simulation/zones/WorkZoneManagerFacade.cs, src/simulation/zones/Zone.cs, src/simulation/zones/ZoneCoverageMap.cs, src/simulation/zones/ZoneJobRouter.cs, src/simulation/zones/ZoneManager.cs, src/ui/PerformanceOverlay.cs, src/ui/PerformanceOverlay.cs, src/ui/PlayerInteractionManager.cs, src/ui/controllers/BuildMenuController.cs, src/ui/controllers/HUDController.cs, src/ui/controllers/MainMenuController.cs, src/ui/controllers/PriorityBoardController.cs, src/ui/controllers/WallMaterialRegistry.cs, src/ui/controllers/WorkZoneController.cs, src/ui/renderers/AgentRenderer.cs, src/ui/renderers/CropRenderer.cs, src/ui/renderers/FertilityOverlayRenderer.cs, src/ui/renderers/GroundItemRenderer.cs, src/ui/renderers/HumidityOverlayRenderer.cs, src/ui/renderers/MapRenderer.cs, src/ui/renderers/ShadowCasterRenderer.cs, src/ui/renderers/StockpileItemRenderer.cs, src/ui/tools/BuildTool.cs, src/ui/tools/FarmingTool.cs, src/ui/tools/StoneMiningTool.cs, src/ui/tools/TreeFellingTool.cs, src/ui/tools/ZoneDraftTool.cs, tests/BenchJobAssignment.cs, tests/Phase2StressTestRunner.cs, tests/Phase3StressTestRunner.cs, tests/Phase4StressTestRunner.cs, tests/Phase5StressTestRunner.cs, tests/TerrainWindowBench.cs, tests/TestActiveArea.cs, tests/TestAdversarialConstruction.cs, tests/TestAdversarialJobDistribution.cs, tests/TestDestroyerStreaming.cs, tests/TestDestroyerWorldGen.cs, tests/TestGeneratorBench.cs, tests/TestIslandMirror.cs, tests/TestIslandZonePersistence.cs, tests/TestJobRaceStress.cs, tests/TestRenderLodBenchmark.cs, tests/TestWorkProgressPersistence.cs, tests/TestWorldGen.cs, tests/TestWorldGenSeamAudit.cs, tests/TestWorldGenV2.cs, tests/test_central_block_zone.cs, tests/test_streamingview_decor_seam.cs, tests/test_zone_agents.cs, tests/test_zone_router.cs
  → Исходящие (2): src/core/common/CpuTopology.cs, src/core/common/SimEvents.cs

[src/core/common/ProfilerLogService.cs] (C#, LoC: 848, I: 0.04)
  ← Входящие (112): scenes/main/Main.cs, src/core/world/activearea/ActiveArea.cs, src/core/world/activearea/MapDataShim.cs, src/core/world/generation/UnifiedWorldGenerator.cs, src/simulation/AgentSimulationThread.cs, src/simulation/SimulationContext.cs, src/simulation/agents/AgentMovementService.cs, src/simulation/agents/AgentSpatialGrid.cs, src/simulation/agents/FlatIdleWorkerPool.cs, src/simulation/agents/FlowFieldManager.cs, src/simulation/agents/IdleWorkerSpatialGrid.cs, src/simulation/agents/WorkerAssignmentSystem.cs, src/simulation/agents/WorkerZoneJobSystem.cs, src/simulation/building/BlueprintManager.cs, src/simulation/building/BuildConfig.cs, src/simulation/building/BuildingManager.cs, src/simulation/building/BuildObstacles.cs, src/simulation/building/ConstructionPipeline.cs, src/simulation/building/TileClearanceService.cs, src/simulation/building/WallBuildManager.cs, src/simulation/building/WorkProgressTracker.cs, src/simulation/farming/CropGrowthManager.cs, src/simulation/farming/FarmZone.cs, src/simulation/gpu/GpuAgentTransformSimulator.cs, src/simulation/gpu/GpuFlowFieldSimulator.cs, src/simulation/gpu/GpuHumiditySimulator.cs, src/simulation/items/DemandRegistry.cs, src/simulation/items/GroundItemManager.cs, src/simulation/items/ResourceBroker.cs, src/simulation/items/StockpileManager.cs, src/simulation/jobs/GenericJobSpatialIndex.cs, src/simulation/jobs/IJobHandler.cs, src/simulation/jobs/JobBroker.cs, src/simulation/jobs/JobDispatcher.cs, src/simulation/jobs/JobPriorityManager.cs, src/simulation/jobs/JobRegistry.cs, src/simulation/jobs/JobSpatialPyramid.cs, src/simulation/jobs/JobValidator.cs, src/simulation/jobs/handlers/BlueprintDeliveryJobHandler.cs, src/simulation/jobs/handlers/ConstructionJobHandler.cs, src/simulation/jobs/handlers/FarmingJobHandler.cs, src/simulation/jobs/handlers/HarvestJobHandler.cs, src/simulation/jobs/handlers/MiningJobHandler.cs, src/simulation/jobs/handlers/NeedsJobSystem.cs, src/simulation/jobs/handlers/PlantingJobHandler.cs, src/simulation/jobs/handlers/StockpileHaulingJobHandler.cs, src/simulation/jobs/handlers/TreeChoppingJobHandler.cs, src/simulation/jobs/handlers/WanderJobSystem.cs, src/simulation/offline/OfflineAgentNeeds.cs, src/simulation/offline/OfflineWorldTicker.cs, src/simulation/pathfinding/HierarchicalPathfinder.cs, src/simulation/scheduling/DynamicWorkScheduler.cs, src/simulation/simd/SimdMovementBatch.cs, src/simulation/simd/SimdNeedsBatch.cs, src/simulation/structures/ConstructionManager.cs, src/simulation/structures/ConstructionSite.cs, src/simulation/structures/StructureDefinition.cs, src/simulation/structures/StructureInstance.cs, src/simulation/structures/StructureManager.cs, src/simulation/structures/StructureRegistry.cs, src/simulation/zones/WorkZone.cs, src/simulation/zones/WorkZoneManagerFacade.cs, src/simulation/zones/Zone.cs, src/simulation/zones/ZoneCoverageMap.cs, src/simulation/zones/ZoneJobRouter.cs, src/simulation/zones/ZoneManager.cs, src/ui/PerformanceOverlay.cs, src/ui/PerformanceOverlay.cs, src/ui/PlayerInteractionManager.cs, src/ui/controllers/BuildMenuController.cs, src/ui/controllers/HUDController.cs, src/ui/controllers/MainMenuController.cs, src/ui/controllers/PriorityBoardController.cs, src/ui/controllers/WallMaterialRegistry.cs, src/ui/controllers/WorkZoneController.cs, src/ui/renderers/AgentRenderer.cs, src/ui/renderers/CropRenderer.cs, src/ui/renderers/FertilityOverlayRenderer.cs, src/ui/renderers/GroundItemRenderer.cs, src/ui/renderers/HumidityOverlayRenderer.cs, src/ui/renderers/MapRenderer.cs, src/ui/renderers/ShadowCasterRenderer.cs, src/ui/renderers/StockpileItemRenderer.cs, src/ui/tools/BuildTool.cs, src/ui/tools/FarmingTool.cs, src/ui/tools/StoneMiningTool.cs, src/ui/tools/TreeFellingTool.cs, src/ui/tools/ZoneDraftTool.cs, tests/BenchJobAssignment.cs, tests/Phase2StressTestRunner.cs, tests/Phase3StressTestRunner.cs, tests/Phase4StressTestRunner.cs, tests/Phase5StressTestRunner.cs, tests/TerrainWindowBench.cs, tests/TestActiveArea.cs, tests/TestAdversarialConstruction.cs, tests/TestAdversarialJobDistribution.cs, tests/TestDestroyerStreaming.cs, tests/TestDestroyerWorldGen.cs, tests/TestGeneratorBench.cs, tests/TestIslandMirror.cs, tests/TestIslandZonePersistence.cs, tests/TestJobRaceStress.cs, tests/TestRenderLodBenchmark.cs, tests/TestWorkProgressPersistence.cs, tests/TestWorldGen.cs, tests/TestWorldGenSeamAudit.cs, tests/TestWorldGenV2.cs, tests/test_central_block_zone.cs, tests/test_streamingview_decor_seam.cs, tests/test_zone_agents.cs, tests/test_zone_router.cs
  → Исходящие (5): src/core/common/GameProfiler.cs, src/simulation/scheduling/DynamicWorkScheduler.cs, src/core/common/GameProfiler.cs, src/simulation/pathfinding/HierarchicalPathfinder.cs, src/core/common/GameProfiler.cs

[src/core/common/ResourceRef.cs] (C#, LoC: 75, I: 0.0)
  ← Входящие (7): scenes/main/Main.cs, src/simulation/AgentSimulationThread.cs, src/simulation/jobs/JobDispatcher.cs, src/simulation/scheduling/DynamicWorkScheduler.cs, src/ui/PerformanceOverlay.cs, src/ui/renderers/AgentRenderer.cs, src/ui/renderers/CropRenderer.cs

[src/core/common/SimEvents.cs] (C#, LoC: 252, I: 0.0)
  ← Входящие (12): scenes/main/Main.cs, src/core/common/GameProfiler.cs, src/simulation/AgentSimulationThread.cs, src/simulation/TimeManager.cs, src/simulation/items/GroundItemManager.cs, src/simulation/scheduling/DynamicWorkScheduler.cs, src/ui/PerformanceOverlay.cs, src/ui/PlayerInteractionManager.cs, src/ui/controllers/HUDController.cs, src/ui/controllers/MainMenuController.cs, src/ui/renderers/AgentRenderer.cs, src/ui/renderers/GroundItemRenderer.cs

[src/core/common/ToolRequirement.cs] (C#, LoC: 16, I: 0.0)
  ← Входящие (1): scenes/main/Main.cs

[src/core/economy/CityState.cs] (C#, LoC: 88, I: 0.0)
  ← Входящие (1): src/ui/controllers/HUDController.cs

[src/core/economy/ResourceTypes.cs] (C#, LoC: 18, I: 0.5)
  (изолированный файл)

[src/core/items/ItemDefinition.cs] (C#, LoC: 17, I: 0.0)
  ← Входящие (1): src/core/items/ItemRegistry.cs

[src/core/items/ItemRegistry.cs] (C#, LoC: 42, I: 0.12)
  ← Входящие (7): src/simulation/building/BuildConfig.cs, src/simulation/items/GroundItemManager.cs, src/simulation/items/StockpileManager.cs, src/simulation/jobs/handlers/BlueprintDeliveryJobHandler.cs, src/simulation/jobs/handlers/NeedsJobSystem.cs, src/ui/renderers/GroundItemRenderer.cs, src/ui/renderers/StockpileItemRenderer.cs
  → Исходящие (1): src/core/items/ItemDefinition.cs

[src/core/jobs/JobCategory.cs] (C#, LoC: 10, I: 0.5)
  (изолированный файл)

[src/core/jobs/JobData.cs] (C#, LoC: 32, I: 0.0)
  ← Входящие (24): src/simulation/jobs/GenericJobSpatialIndex.cs, src/simulation/jobs/IJobHandler.cs, src/simulation/jobs/JobBroker.cs, src/simulation/jobs/JobDispatcher.cs, src/simulation/jobs/JobSpatialPyramid.cs, src/simulation/jobs/JobValidator.cs, src/simulation/jobs/handlers/BlueprintDeliveryJobHandler.cs, src/simulation/jobs/handlers/ConstructionJobHandler.cs, src/simulation/jobs/handlers/FarmingJobHandler.cs, src/simulation/jobs/handlers/HarvestJobHandler.cs, src/simulation/jobs/handlers/MiningJobHandler.cs, src/simulation/jobs/handlers/PlantingJobHandler.cs, src/simulation/jobs/handlers/StockpileHaulingJobHandler.cs, src/simulation/jobs/handlers/TreeChoppingJobHandler.cs, src/simulation/structures/ConstructionManager.cs, tests/BenchJobAssignment.cs, tests/Phase4StressTestRunner.cs, tests/Phase5StressTestRunner.cs, tests/TestAdversarialConstruction.cs, tests/TestAdversarialJobDistribution.cs, tests/TestJobRaceStress.cs, tests/TestWorkProgressPersistence.cs, tests/test_zone_agents.cs, tests/test_zone_router.cs

[src/core/jobs/JobExecutionType.cs] (C#, LoC: 11, I: 0.5)
  (изолированный файл)

[src/core/jobs/JobPriorityTier.cs] (C#, LoC: 15, I: 0.5)
  (изолированный файл)

[src/core/jobs/JobTypeId.cs] (C#, LoC: 19, I: 0.5)
  (изолированный файл)

[src/core/jobs/NeedBehavior.cs] (C#, LoC: 19, I: 0.5)
  (изолированный файл)

[src/core/world/BiomeMapper.cs] (C#, LoC: 172, I: 0.06)
  ← Входящие (34): scenes/main/Main.cs, src/core/world/MapData.cs, src/core/world/access/ChunkWorldAccess.cs, src/core/world/climate/ContinentShaper.cs, src/core/world/climate/PlanetaryHydrology.cs, src/core/world/climate/WorldClimateProvider.cs, src/core/world/climate/WorldMacroClimateParams.cs, src/core/world/generation/UnifiedWorldGenerator.cs, src/core/world/generation/WorldGenV2.cs, src/core/world/streaming/generation/RegionTraits.cs, src/core/world/streaming/integration/ChunkMutations.cs, src/core/world/streaming/integration/IWallWorld.cs, src/core/world/streaming/integration/WorldCellQuery.cs, src/ui/controllers/HUDController.cs, src/ui/renderers/FertilityOverlayRenderer.cs, src/ui/renderers/HumidityOverlayRenderer.cs, src/ui/renderers/MapRenderer.cs, src/ui/streaming/StreamingWorldView.cs, src/ui/streaming/WorldJobBoard.cs, src/ui/streaming/WorldMapOverlay.cs, tests/Phase2StressTestRunner.cs, tests/Phase5StressTestRunner.cs, tests/TerrainWindowBench.cs, tests/TestActiveArea.cs, tests/TestDestroyerStreaming.cs, tests/TestDestroyerWorldGen.cs, tests/TestGeneratorBench.cs, tests/TestIslandMirror.cs, tests/TestIslandZonePersistence.cs, tests/TestWorldGen.cs, tests/TestWorldGenSeamAudit.cs, tests/TestWorldGenV2.cs, tests/TestWorldStoreExit.cs, tests/test_streamingview_decor_seam.cs
  → Исходящие (2): src/core/world/streaming/generation/WorldCoordinates.cs, src/core/world/streaming/generation/WorldClimateGenerator.cs

[src/core/world/BiomeType.cs] (C#, LoC: 39, I: 0.0)
  ← Входящие (32): scenes/main/Main.cs, src/core/world/MapData.cs, src/core/world/access/ChunkWorldAccess.cs, src/core/world/climate/ContinentShaper.cs, src/core/world/climate/PlanetaryHydrology.cs, src/core/world/climate/WorldClimateProvider.cs, src/core/world/climate/WorldMacroClimateParams.cs, src/core/world/streaming/generation/RegionTraits.cs, src/core/world/streaming/integration/ChunkMutations.cs, src/core/world/streaming/integration/IWallWorld.cs, src/core/world/streaming/integration/WorldCellQuery.cs, src/ui/controllers/HUDController.cs, src/ui/renderers/FertilityOverlayRenderer.cs, src/ui/renderers/HumidityOverlayRenderer.cs, src/ui/renderers/MapRenderer.cs, src/ui/streaming/StreamingWorldView.cs, src/ui/streaming/WorldJobBoard.cs, src/ui/streaming/WorldMapOverlay.cs, tests/Phase2StressTestRunner.cs, tests/Phase5StressTestRunner.cs, tests/TerrainWindowBench.cs, tests/TestActiveArea.cs, tests/TestDestroyerStreaming.cs, tests/TestDestroyerWorldGen.cs, tests/TestGeneratorBench.cs, tests/TestIslandMirror.cs, tests/TestIslandZonePersistence.cs, tests/TestWorldGen.cs, tests/TestWorldGenSeamAudit.cs, tests/TestWorldGenV2.cs, tests/TestWorldStoreExit.cs, tests/test_streamingview_decor_seam.cs

[src/core/world/BuildingType.cs] (C#, LoC: 15, I: 0.5)
  (изолированный файл)

[src/core/world/FarmYield.cs] (C#, LoC: 34, I: 0.0)
  ← Входящие (1): src/simulation/farming/CropGrowthManager.cs

[src/core/world/FertilityMap.cs] (C#, LoC: 244, I: 0.0)
  ← Входящие (7): src/core/world/MapData.cs, src/core/world/activearea/ActiveArea.cs, src/simulation/AgentSimulationThread.cs, src/simulation/SimulationContext.cs, src/simulation/farming/CropGrowthManager.cs, src/ui/renderers/FertilityOverlayRenderer.cs, src/ui/renderers/MapRenderer.cs

[src/core/world/GridHelper.cs] (C#, LoC: 99, I: 0.0)
  ← Входящие (2): src/simulation/agents/AgentMovementService.cs, src/simulation/jobs/JobBroker.cs

[src/core/world/HumidityMap.cs] (C#, LoC: 396, I: 0.0)
  ← Входящие (8): src/core/world/MapData.cs, src/core/world/activearea/ActiveArea.cs, src/simulation/AgentSimulationThread.cs, src/simulation/SimulationContext.cs, src/simulation/farming/CropGrowthManager.cs, src/simulation/gpu/GpuHumiditySimulator.cs, src/ui/renderers/HumidityOverlayRenderer.cs, src/ui/renderers/MapRenderer.cs

[src/core/world/MapData.cs] (C#, LoC: 101, I: 0.16)
  ← Входящие (26): scenes/main/Main.cs, src/core/world/activearea/MapDataShim.cs, src/core/world/generation/UnifiedWorldGenerator.cs, src/simulation/building/BuildObstacles.cs, src/simulation/building/ConstructionPipeline.cs, src/simulation/building/TileClearanceService.cs, src/simulation/items/GroundItemManager.cs, src/simulation/structures/ConstructionManager.cs, src/ui/controllers/BuildMenuController.cs, src/ui/controllers/FarmController.cs, src/ui/controllers/HUDController.cs, src/ui/renderers/MapRenderer.cs, src/ui/renderers/ShadowCasterRenderer.cs, src/ui/tools/BuildTool.cs, src/ui/tools/FarmingTool.cs, src/ui/tools/StoneMiningTool.cs, src/ui/tools/TreeFellingTool.cs, src/ui/tools/ZoneDraftTool.cs, tests/Phase3StressTestRunner.cs, tests/Phase4StressTestRunner.cs, tests/TestActiveArea.cs, tests/TestAdversarialConstruction.cs, tests/TestDestroyerWorldGen.cs, tests/TestGeneratorBench.cs, tests/TestWorldGenV2.cs, tests/test_streamingview_decor_seam.cs
  → Исходящие (5): src/core/world/FertilityMap.cs, src/core/world/HumidityMap.cs, src/core/world/BiomeMapper.cs, src/core/world/BiomeType.cs, src/core/world/activearea/ActiveArea.cs

[src/core/world/MountainTileHelper.cs] (C#, LoC: 102, I: 0.0)
  ← Входящие (1): tests/TestWorldGen.cs

[src/core/world/TileType.cs] (C#, LoC: 13, I: 0.5)
  (изолированный файл)

[src/core/world/WallTileHelper.cs] (C#, LoC: 98, I: 0.4)
  ← Входящие (3): src/simulation/building/WorkProgressTracker.cs, src/ui/controllers/WallMaterialRegistry.cs, src/ui/renderers/MapRenderer.cs
  → Исходящие (2): src/simulation/building/WorkProgressTracker.cs, src/ui/controllers/WallMaterialRegistry.cs

[src/core/world/access/ChunkWorldAccess.cs] (C#, LoC: 67, I: 1.0)
  → Исходящие (11): src/core/world/access/IWorldAccess.cs, src/core/world/access/IWorldAccess.cs, src/core/world/streaming/integration/IWorldCellSource.cs, src/core/world/streaming/integration/WorldDecor.cs, src/core/world/streaming/integration/WorldDeltaKind.cs, src/core/world/BiomeMapper.cs, src/core/world/BiomeType.cs, src/core/world/activearea/ActiveArea.cs, src/core/world/streaming/integration/ChunkMutations.cs, src/core/world/streaming/integration/IBlockWorld.cs, src/core/world/streaming/integration/IWallWorld.cs

[src/core/world/access/IWorldAccess.cs] (C#, LoC: 46, I: 0.0)
  ← Входящие (2): src/core/world/access/ChunkWorldAccess.cs, src/core/world/access/ChunkWorldAccess.cs

[src/core/world/activearea/ActiveArea.cs] (C#, LoC: 239, I: 0.18)
  ← Входящие (37): scenes/main/Main.cs, src/core/world/MapData.cs, src/core/world/access/ChunkWorldAccess.cs, src/core/world/activearea/ActiveAreaRegistry.cs, src/core/world/activearea/MapDataShim.cs, src/core/world/climate/ContinentShaper.cs, src/core/world/climate/PlanetaryHydrology.cs, src/core/world/climate/WorldClimateProvider.cs, src/core/world/climate/WorldMacroClimateParams.cs, src/core/world/mutation/IslandDeltaApplicator.cs, src/core/world/mutation/IslandStateReplay.cs, src/core/world/mutation/IslandStateSnapshot.cs, src/core/world/streaming/generation/RegionTraits.cs, src/core/world/streaming/integration/ChunkMutations.cs, src/core/world/streaming/integration/IWallWorld.cs, src/core/world/streaming/integration/WorldCellQuery.cs, src/ui/controllers/HUDController.cs, src/ui/renderers/FertilityOverlayRenderer.cs, src/ui/renderers/HumidityOverlayRenderer.cs, src/ui/renderers/MapRenderer.cs, src/ui/streaming/StreamingWorldView.cs, src/ui/streaming/WorldJobBoard.cs, src/ui/streaming/WorldMapOverlay.cs, tests/Phase2StressTestRunner.cs, tests/Phase5StressTestRunner.cs, tests/TerrainWindowBench.cs, tests/TestActiveArea.cs, tests/TestDestroyerStreaming.cs, tests/TestDestroyerWorldGen.cs, tests/TestGeneratorBench.cs, tests/TestIslandMirror.cs, tests/TestIslandZonePersistence.cs, tests/TestWorldGen.cs, tests/TestWorldGenSeamAudit.cs, tests/TestWorldGenV2.cs, tests/TestWorldStoreExit.cs, tests/test_streamingview_decor_seam.cs
  → Исходящие (8): src/core/world/generation/WorldGenV2.cs, src/core/world/FertilityMap.cs, src/core/world/HumidityMap.cs, src/core/world/streaming/generation/WorldClimateGenerator.cs, src/core/world/generation/WorldGenV2.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/core/world/activearea/ActiveAreaRegistry.cs] (C#, LoC: 104, I: 0.29)
  ← Входящие (5): scenes/main/Main.cs, src/ui/streaming/WorldMapOverlay.cs, tests/TestActiveArea.cs, tests/TestIslandMirror.cs, tests/TestWorldGen.cs
  → Исходящие (2): src/core/world/activearea/ActiveArea.cs, src/core/world/generation/WorldGenV2.cs

[src/core/world/activearea/MapDataShim.cs] (C#, LoC: 117, I: 0.71)
  ← Входящие (2): scenes/main/Main.cs, tests/TestActiveArea.cs
  → Исходящие (5): src/core/world/activearea/ActiveArea.cs, src/core/world/MapData.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/core/world/climate/ContinentShaper.cs] (C#, LoC: 440, I: 0.56)
  ← Входящие (4): src/core/world/climate/WorldClimateProvider.cs, src/core/world/streaming/generation/WorldClimateGenerator.cs, tests/Phase2StressTestRunner.cs, tests/TestWorldGen.cs
  → Исходящие (5): src/core/world/streaming/generation/WorldCoordinates.cs, src/core/world/climate/Q16.cs, src/core/world/BiomeMapper.cs, src/core/world/BiomeType.cs, src/core/world/activearea/ActiveArea.cs

[src/core/world/climate/LatitudeBands.cs] (C#, LoC: 122, I: 0.5)
  ← Входящие (2): src/core/world/climate/WorldClimateProvider.cs, tests/Phase2StressTestRunner.cs
  → Исходящие (2): src/core/world/climate/Q16.cs, src/simulation/zones/Zone.cs

[src/core/world/climate/PlanetaryHydrology.cs] (C#, LoC: 228, I: 0.75)
  ← Входящие (2): src/ui/streaming/WorldMapOverlay.cs, tests/Phase2StressTestRunner.cs
  → Исходящие (6): src/core/world/streaming/generation/WorldCoordinates.cs, src/core/world/streaming/generation/WorldClimateGenerator.cs, src/core/world/streaming/WorldMapTileMapper.cs, src/core/world/BiomeMapper.cs, src/core/world/BiomeType.cs, src/core/world/activearea/ActiveArea.cs

[src/core/world/climate/Q16.cs] (C#, LoC: 64, I: 0.0)
  ← Входящие (5): src/core/world/climate/ContinentShaper.cs, src/core/world/climate/LatitudeBands.cs, src/core/world/climate/WorldClimateFields.cs, src/core/world/climate/WorldClimateProvider.cs, tests/TestDestroyerWorldGen.cs

[src/core/world/climate/WorldClimateFields.cs] (C#, LoC: 78, I: 0.5)
  ← Входящие (1): src/core/world/climate/WorldClimateProvider.cs
  → Исходящие (1): src/core/world/climate/Q16.cs

[src/core/world/climate/WorldClimateProvider.cs] (C#, LoC: 211, I: 0.56)
  ← Входящие (7): scenes/main/Main.cs, src/core/world/generation/UnifiedWorldGenerator.cs, src/core/world/streaming/generation/WorldClimateGenerator.cs, src/ui/controllers/MainMenuController.cs, src/ui/streaming/WorldMapOverlay.cs, tests/Phase2StressTestRunner.cs, tests/TestWorldGen.cs
  → Исходящие (9): src/core/world/climate/ContinentShaper.cs, src/core/world/streaming/generation/WorldCoordinates.cs, src/core/world/climate/LatitudeBands.cs, src/core/world/climate/Q16.cs, src/core/world/streaming/generation/WorldRegions.cs, src/core/world/BiomeMapper.cs, src/core/world/BiomeType.cs, src/core/world/activearea/ActiveArea.cs, src/core/world/climate/WorldClimateFields.cs

[src/core/world/climate/WorldMacroClimateParams.cs] (C#, LoC: 192, I: 1.0)
  → Исходящие (4): src/core/world/streaming/generation/WorldCoordinates.cs, src/core/world/BiomeMapper.cs, src/core/world/BiomeType.cs, src/core/world/activearea/ActiveArea.cs

[src/core/world/generation/UnifiedWorldGenerator.cs] (C#, LoC: 642, I: 0.5)
  ← Входящие (19): scenes/main/Main.cs, src/core/world/streaming/integration/WorldCellQuery.cs, src/core/world/streaming/runtime/WorldChunkStreamer.cs, src/simulation/agents/AgentMovementService.cs, src/ui/controllers/HUDController.cs, src/ui/renderers/FertilityOverlayRenderer.cs, src/ui/renderers/HumidityOverlayRenderer.cs, src/ui/renderers/MapRenderer.cs, src/ui/streaming/StreamingWorldView.cs, tests/TerrainWindowBench.cs, tests/TestActiveArea.cs, tests/TestDestroyerStreaming.cs, tests/TestDestroyerWorldGen.cs, tests/TestGeneratorBench.cs, tests/TestWorldGen.cs, tests/TestWorldGenSeamAudit.cs, tests/TestWorldGenV2.cs, tests/TestWorldStoreExit.cs, tests/test_streamingview_decor_seam.cs
  → Исходящие (19): src/core/world/BiomeMapper.cs, src/core/world/streaming/generation/WorldCoordinates.cs, src/core/world/MapData.cs, src/core/world/streaming/generation/WorldChunk.cs, src/core/world/streaming/generation/WorldChunkGeneratorTiming.cs, src/core/world/streaming/generation/WorldClimateGenerator.cs, src/core/world/climate/WorldClimateProvider.cs, src/core/world/streaming/generation/WorldCoordinates.cs, src/core/world/streaming/integration/WorldDecor.cs, src/core/world/streaming/generation/WorldRegions.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs, src/core/world/streaming/integration/ChunkMutations.cs, src/core/world/streaming/integration/IBlockWorld.cs, src/core/world/streaming/integration/IWallWorld.cs, src/core/world/streaming/generation/GeneratedCell.cs, src/core/world/streaming/generation/RegionKey.cs, src/core/world/streaming/generation/WorldClimateParams.cs

[src/core/world/generation/WorldGenV2.cs] (C#, LoC: 630, I: 0.53)
  ← Входящие (8): scenes/main/Main.cs, src/core/world/activearea/ActiveArea.cs, src/core/world/activearea/ActiveArea.cs, src/core/world/activearea/ActiveAreaRegistry.cs, tests/TestActiveArea.cs, tests/TestActiveArea.cs, tests/TestWorldGenV2.cs, tests/TestWorldGenV2.cs
  → Исходящие (9): src/core/world/BiomeMapper.cs, src/core/world/streaming/generation/WorldCoordinates.cs, src/core/world/streaming/generation/WorldClimateGenerator.cs, src/core/world/streaming/integration/WorldDecor.cs, src/core/world/streaming/integration/ChunkMutations.cs, src/core/world/streaming/integration/IBlockWorld.cs, src/core/world/streaming/integration/IWallWorld.cs, src/core/world/streaming/generation/GeneratedCell.cs, src/core/world/streaming/generation/WorldClimateParams.cs

[src/core/world/mutation/IWorldMutationSink.cs] (C#, LoC: 78, I: 0.0)
  ← Входящие (8): scenes/main/Main.cs, src/core/world/mutation/IslandMutationMirror.cs, src/core/world/mutation/IslandMutationMirror.cs, src/core/world/mutation/IslandOverrideStore.cs, src/core/world/mutation/IslandOverrideStore.cs, src/core/world/mutation/IslandOverrideStore.cs, src/core/world/mutation/IslandStateSnapshot.cs, tests/TestIslandMirror.cs

[src/core/world/mutation/IslandDeltaApplicator.cs] (C#, LoC: 162, I: 0.69)
  ← Входящие (5): scenes/main/Main.cs, scenes/main/Main.cs, tests/TestIslandMirror.cs, tests/TestIslandMirror.cs, tests/TestIslandZonePersistence.cs
  → Исходящие (11): src/core/world/activearea/ActiveArea.cs, src/core/world/mutation/IslandDomainReplay.cs, src/core/world/mutation/IslandOverrideStore.cs, src/simulation/SimulationContext.cs, src/core/world/streaming/integration/WorldDeltaKind.cs, src/core/world/streaming/integration/ChunkMutations.cs, src/core/world/streaming/integration/IBlockWorld.cs, src/core/world/streaming/integration/IWallWorld.cs, src/simulation/AgentSimulationThread.cs, src/simulation/CellProgressStore.cs, src/simulation/DynamicWorkBalancer.cs

[src/core/world/mutation/IslandDomainReplay.cs] (C#, LoC: 309, I: 0.76)
  ← Входящие (5): scenes/main/Main.cs, src/core/world/mutation/IslandDeltaApplicator.cs, src/core/world/mutation/IslandStateReplay.cs, tests/TestIslandMirror.cs, tests/TestIslandZonePersistence.cs
  → Исходящие (16): src/simulation/structures/ConstructionManager.cs, src/simulation/farming/FarmJobManager.cs, src/simulation/SimulationContext.cs, src/simulation/items/StockpileManager.cs, src/simulation/stones/StoneJobManager.cs, src/simulation/structures/StructureDefinition.cs, src/simulation/structures/StructureManager.cs, src/simulation/structures/StructureRegistry.cs, src/simulation/trees/TreeJobManager.cs, src/core/world/streaming/integration/WorldDeltaKind.cs, src/core/world/streaming/integration/ChunkMutations.cs, src/core/world/streaming/integration/IBlockWorld.cs, src/core/world/streaming/integration/IWallWorld.cs, src/simulation/AgentSimulationThread.cs, src/simulation/CellProgressStore.cs, src/simulation/DynamicWorkBalancer.cs

[src/core/world/mutation/IslandMutationMirror.cs] (C#, LoC: 339, I: 0.86)
  ← Входящие (3): scenes/main/Main.cs, tests/TestIslandMirror.cs, tests/TestIslandZonePersistence.cs
  → Исходящие (18): src/simulation/structures/ConstructionManager.cs, src/simulation/structures/ConstructionSite.cs, src/simulation/farming/FarmJobManager.cs, src/core/world/mutation/IWorldMutationSink.cs, src/simulation/items/StockpileManager.cs, src/simulation/stones/StoneJobManager.cs, src/simulation/structures/StructureDefinition.cs, src/simulation/structures/StructureInstance.cs, src/simulation/structures/StructureManager.cs, src/simulation/trees/TreeJobManager.cs, src/core/world/streaming/integration/WorldDeltaKind.cs, src/core/world/mutation/IWorldMutationSink.cs, src/core/world/streaming/integration/ChunkMutations.cs, src/core/world/streaming/integration/IBlockWorld.cs, src/core/world/streaming/integration/IWallWorld.cs, src/simulation/AgentSimulationThread.cs, src/simulation/CellProgressStore.cs, src/simulation/DynamicWorkBalancer.cs

[src/core/world/mutation/IslandOverrideStore.cs] (C#, LoC: 388, I: 0.33)
  ← Входящие (6): scenes/main/Main.cs, src/core/world/mutation/IslandDeltaApplicator.cs, src/core/world/mutation/IslandStateReplay.cs, src/core/world/mutation/IslandStateSnapshot.cs, tests/TestIslandMirror.cs, tests/TestIslandZonePersistence.cs
  → Исходящие (3): src/core/world/mutation/IWorldMutationSink.cs, src/core/world/mutation/IWorldMutationSink.cs, src/core/world/mutation/IWorldMutationSink.cs

[src/core/world/mutation/IslandPayload.cs] (C#, LoC: 238, I: 0.55)
  ← Входящие (5): scenes/main/Main.cs, src/core/world/mutation/IslandStateReplay.cs, src/core/world/mutation/IslandStateSnapshot.cs, tests/TestIslandMirror.cs, tests/TestIslandZonePersistence.cs
  → Исходящие (6): src/core/world/streaming/integration/ChunkMutations.cs, src/core/world/streaming/integration/IBlockWorld.cs, src/core/world/streaming/integration/IWallWorld.cs, src/simulation/AgentSimulationThread.cs, src/simulation/CellProgressStore.cs, src/simulation/DynamicWorkBalancer.cs

[src/core/world/mutation/IslandStateReplay.cs] (C#, LoC: 180, I: 0.86)
  ← Входящие (3): scenes/main/Main.cs, tests/TestIslandMirror.cs, tests/TestIslandZonePersistence.cs
  → Исходящие (19): src/core/world/activearea/ActiveArea.cs, src/simulation/structures/ConstructionManager.cs, src/simulation/structures/ConstructionSite.cs, src/simulation/farming/CropGrowthManager.cs, src/simulation/items/GroundItemManager.cs, src/core/world/mutation/IslandDomainReplay.cs, src/core/world/mutation/IslandOverrideStore.cs, src/core/world/mutation/IslandPayload.cs, src/simulation/jobs/JobBroker.cs, src/simulation/items/StockpileManager.cs, src/simulation/stones/StoneJobManager.cs, src/simulation/trees/TreeJobManager.cs, src/core/world/streaming/integration/WorldDeltaKind.cs, src/core/world/streaming/integration/ChunkMutations.cs, src/core/world/streaming/integration/IBlockWorld.cs, src/core/world/streaming/integration/IWallWorld.cs, src/simulation/AgentSimulationThread.cs, src/simulation/CellProgressStore.cs, src/simulation/DynamicWorkBalancer.cs

[src/core/world/mutation/IslandStateSnapshot.cs] (C#, LoC: 168, I: 0.95)
  ← Входящие (1): scenes/main/Main.cs
  → Исходящие (18): src/core/world/activearea/ActiveArea.cs, src/simulation/structures/ConstructionManager.cs, src/simulation/farming/CropData.cs, src/simulation/farming/CropGrowthManager.cs, src/simulation/items/GroundItemManager.cs, src/core/world/mutation/IslandOverrideStore.cs, src/core/world/mutation/IslandPayload.cs, src/simulation/items/StockpileManager.cs, src/simulation/stones/StoneJobManager.cs, src/simulation/trees/TreeJobManager.cs, src/core/world/streaming/integration/WorldDeltaKind.cs, src/core/world/mutation/IWorldMutationSink.cs, src/core/world/streaming/integration/ChunkMutations.cs, src/core/world/streaming/integration/IBlockWorld.cs, src/core/world/streaming/integration/IWallWorld.cs, src/simulation/AgentSimulationThread.cs, src/simulation/CellProgressStore.cs, src/simulation/DynamicWorkBalancer.cs

[src/core/world/streaming/WorldMapTileMapper.cs] (C#, LoC: 98, I: 0.0)
  ← Входящие (3): src/core/world/climate/PlanetaryHydrology.cs, src/ui/streaming/WorldMapOverlay.cs, tests/TestActiveArea.cs

[src/core/world/streaming/coordinator/WorldSaveCoordinator.cs] (C#, LoC: 252, I: 0.38)
  ← Входящие (5): scenes/main/Main.cs, src/core/world/streaming/integration/StreamingWorldManager.cs, tests/Phase5StressTestRunner.cs, tests/TestDestroyerStreaming.cs, tests/TestWorldStoreExit.cs
  → Исходящие (3): src/core/world/streaming/storage/WorldSaveMetadata.cs, src/core/world/streaming/storage/WorldChunkStore.cs, src/core/world/streaming/storage/WorldManifestStore.cs

[src/core/world/streaming/generation/GeneratedCell.cs] (C#, LoC: 24, I: 0.0)
  ← Входящие (4): src/core/world/generation/UnifiedWorldGenerator.cs, src/core/world/generation/WorldGenV2.cs, tests/Phase5StressTestRunner.cs, tests/test_streamingview_decor_seam.cs

[src/core/world/streaming/generation/RegionKey.cs] (C#, LoC: 3, I: 0.0)
  ← Входящие (6): src/core/world/generation/UnifiedWorldGenerator.cs, src/core/world/streaming/generation/RegionTraits.cs, src/core/world/streaming/generation/WorldClimateGenerator.cs, src/ui/streaming/WorldMapOverlay.cs, tests/Phase2StressTestRunner.cs, tests/TestWorldGen.cs

[src/core/world/streaming/generation/RegionTraits.cs] (C#, LoC: 229, I: 0.89)
  ← Входящие (1): src/core/world/streaming/generation/WorldClimateGenerator.cs
  → Исходящие (8): src/core/world/streaming/generation/WorldCoordinates.cs, src/core/world/streaming/generation/WorldClimateGenerator.cs, src/core/world/streaming/generation/WorldCoordinates.cs, src/core/world/streaming/generation/WorldRegions.cs, src/core/world/BiomeMapper.cs, src/core/world/BiomeType.cs, src/core/world/activearea/ActiveArea.cs, src/core/world/streaming/generation/RegionKey.cs

[src/core/world/streaming/generation/WorldChunk.cs] (C#, LoC: 39, I: 0.1)
  ← Входящие (9): src/core/world/generation/UnifiedWorldGenerator.cs, src/core/world/streaming/generation/WorldChunkGeneratorTiming.cs, src/core/world/streaming/runtime/WorldChunkStreamer.cs, src/ui/streaming/StreamingWorldView.cs, tests/TestDestroyerWorldGen.cs, tests/TestGeneratorBench.cs, tests/TestWorldGen.cs, tests/TestWorldGenV2.cs, tests/test_streamingview_decor_seam.cs
  → Исходящие (1): src/core/world/streaming/generation/WorldCoordinates.cs

[src/core/world/streaming/generation/WorldChunkGeneratorTiming.cs] (C#, LoC: 128, I: 0.5)
  ← Входящие (1): src/core/world/generation/UnifiedWorldGenerator.cs
  → Исходящие (1): src/core/world/streaming/generation/WorldChunk.cs

[src/core/world/streaming/generation/WorldClimateGenerator.cs] (C#, LoC: 367, I: 0.35)
  ← Входящие (15): src/core/world/BiomeMapper.cs, src/core/world/activearea/ActiveArea.cs, src/core/world/climate/PlanetaryHydrology.cs, src/core/world/generation/UnifiedWorldGenerator.cs, src/core/world/generation/WorldGenV2.cs, src/core/world/streaming/generation/RegionTraits.cs, src/core/world/streaming/integration/WorldCellQuery.cs, src/ui/renderers/MapRenderer.cs, src/ui/streaming/StreamingWorldView.cs, src/ui/streaming/WorldMapOverlay.cs, tests/Phase2StressTestRunner.cs, tests/TestGeneratorBench.cs, tests/TestWorldGen.cs, tests/TestWorldGenV2.cs, tests/test_streamingview_decor_seam.cs
  → Исходящие (8): src/core/world/climate/ContinentShaper.cs, src/core/world/streaming/generation/WorldCoordinates.cs, src/core/world/streaming/generation/RegionTraits.cs, src/core/world/climate/WorldClimateProvider.cs, src/core/world/streaming/generation/WorldCoordinates.cs, src/core/world/streaming/generation/WorldRegions.cs, src/core/world/streaming/generation/RegionKey.cs, src/core/world/streaming/generation/WorldClimateParams.cs

[src/core/world/streaming/generation/WorldClimateParams.cs] (C#, LoC: 70, I: 0.0)
  ← Входящие (4): src/core/world/generation/UnifiedWorldGenerator.cs, src/core/world/generation/WorldGenV2.cs, src/core/world/streaming/generation/WorldClimateGenerator.cs, src/ui/renderers/MapRenderer.cs

[src/core/world/streaming/generation/WorldCoordinates.cs] (C#, LoC: 293, I: 0.0)
  ← Входящие (27): src/core/world/BiomeMapper.cs, src/core/world/climate/ContinentShaper.cs, src/core/world/climate/PlanetaryHydrology.cs, src/core/world/climate/WorldClimateProvider.cs, src/core/world/climate/WorldMacroClimateParams.cs, src/core/world/generation/UnifiedWorldGenerator.cs, src/core/world/generation/UnifiedWorldGenerator.cs, src/core/world/generation/WorldGenV2.cs, src/core/world/streaming/generation/RegionTraits.cs, src/core/world/streaming/generation/RegionTraits.cs, src/core/world/streaming/generation/WorldChunk.cs, src/core/world/streaming/generation/WorldClimateGenerator.cs, src/core/world/streaming/generation/WorldClimateGenerator.cs, src/core/world/streaming/integration/ChunkMutations.cs, src/core/world/streaming/integration/StreamingWorldManager.cs, src/core/world/streaming/integration/WorldGameRules.cs, src/core/world/streaming/runtime/ChunkWindowPlanner.cs, src/core/world/streaming/runtime/WorldChunkStreamer.cs, src/ui/renderers/MapRenderer.cs, src/ui/streaming/StreamingWorldView.cs, src/ui/streaming/WorldJobBoard.cs, tests/TestDestroyerStreaming.cs, tests/TestDestroyerWorldGen.cs, tests/TestWorldGen.cs, tests/TestWorldGenSeamAudit.cs, tests/TestWorldGenV2.cs, tests/test_streamingview_decor_seam.cs

[src/core/world/streaming/generation/WorldRegions.cs] (C#, LoC: 54, I: 0.0)
  ← Входящие (9): src/core/world/climate/WorldClimateProvider.cs, src/core/world/generation/UnifiedWorldGenerator.cs, src/core/world/streaming/generation/RegionTraits.cs, src/core/world/streaming/generation/WorldClimateGenerator.cs, tests/TerrainWindowBench.cs, tests/TestActiveArea.cs, tests/TestGeneratorBench.cs, tests/TestWorldGenV2.cs, tests/test_streamingview_decor_seam.cs

[src/core/world/streaming/integration/ChunkMutations.cs] (C#, LoC: 131, I: 0.18)
  ← Входящие (27): scenes/main/Main.cs, src/core/world/access/ChunkWorldAccess.cs, src/core/world/generation/UnifiedWorldGenerator.cs, src/core/world/generation/WorldGenV2.cs, src/core/world/mutation/IslandDeltaApplicator.cs, src/core/world/mutation/IslandDomainReplay.cs, src/core/world/mutation/IslandMutationMirror.cs, src/core/world/mutation/IslandPayload.cs, src/core/world/mutation/IslandStateReplay.cs, src/core/world/mutation/IslandStateSnapshot.cs, src/ui/renderers/FertilityOverlayRenderer.cs, src/ui/renderers/HumidityOverlayRenderer.cs, src/ui/renderers/MapRenderer.cs, src/ui/streaming/StreamingToolPlacement.cs, src/ui/streaming/StreamingWorldView.cs, src/ui/streaming/WorldJobBoard.cs, tests/Phase5StressTestRunner.cs, tests/TerrainWindowBench.cs, tests/TestDestroyerStreaming.cs, tests/TestDestroyerWorldGen.cs, tests/TestGeneratorBench.cs, tests/TestIslandMirror.cs, tests/TestIslandZonePersistence.cs, tests/TestWorldGenSeamAudit.cs, tests/TestWorldGenV2.cs, tests/TestWorldStoreExit.cs, tests/test_streamingview_decor_seam.cs
  → Исходящие (6): src/core/world/streaming/storage/WorldChunkDeltaCodec.cs, src/core/world/streaming/storage/WorldChunkDeltaCodec.cs, src/core/world/streaming/generation/WorldCoordinates.cs, src/core/world/BiomeMapper.cs, src/core/world/BiomeType.cs, src/core/world/activearea/ActiveArea.cs

[src/core/world/streaming/integration/IBlockWorld.cs] (C#, LoC: 23, I: 0.06)
  ← Входящие (33): scenes/main/Main.cs, src/core/world/access/ChunkWorldAccess.cs, src/core/world/generation/UnifiedWorldGenerator.cs, src/core/world/generation/WorldGenV2.cs, src/core/world/mutation/IslandDeltaApplicator.cs, src/core/world/mutation/IslandDomainReplay.cs, src/core/world/mutation/IslandMutationMirror.cs, src/core/world/mutation/IslandPayload.cs, src/core/world/mutation/IslandStateReplay.cs, src/core/world/mutation/IslandStateSnapshot.cs, src/core/world/streaming/integration/IWorldCellSource.cs, src/core/world/streaming/integration/IWorldCellSource.cs, src/core/world/streaming/integration/StreamingWorldManager.cs, src/core/world/streaming/integration/WorldGameRules.cs, src/ui/renderers/FertilityOverlayRenderer.cs, src/ui/renderers/HumidityOverlayRenderer.cs, src/ui/renderers/MapRenderer.cs, src/ui/streaming/StreamingToolPlacement.cs, src/ui/streaming/StreamingWorldView.cs, src/ui/streaming/StreamingWorldView.cs, src/ui/streaming/WorldJobBoard.cs, tests/Phase5StressTestRunner.cs, tests/Phase5StressTestRunner.cs, tests/TerrainWindowBench.cs, tests/TestDestroyerStreaming.cs, tests/TestDestroyerWorldGen.cs, tests/TestGeneratorBench.cs, tests/TestIslandMirror.cs, tests/TestIslandZonePersistence.cs, tests/TestWorldGenSeamAudit.cs, tests/TestWorldGenV2.cs, tests/TestWorldStoreExit.cs, tests/test_streamingview_decor_seam.cs
  → Исходящие (2): src/core/world/streaming/integration/IWallWorld.cs, src/core/world/streaming/integration/IWallWorld.cs

[src/core/world/streaming/integration/IWallWorld.cs] (C#, LoC: 26, I: 0.09)
  ← Входящие (31): scenes/main/Main.cs, src/core/world/access/ChunkWorldAccess.cs, src/core/world/generation/UnifiedWorldGenerator.cs, src/core/world/generation/WorldGenV2.cs, src/core/world/mutation/IslandDeltaApplicator.cs, src/core/world/mutation/IslandDomainReplay.cs, src/core/world/mutation/IslandMutationMirror.cs, src/core/world/mutation/IslandPayload.cs, src/core/world/mutation/IslandStateReplay.cs, src/core/world/mutation/IslandStateSnapshot.cs, src/core/world/streaming/integration/IBlockWorld.cs, src/core/world/streaming/integration/IBlockWorld.cs, src/core/world/streaming/integration/StreamingWorldManager.cs, src/ui/renderers/FertilityOverlayRenderer.cs, src/ui/renderers/HumidityOverlayRenderer.cs, src/ui/renderers/MapRenderer.cs, src/ui/streaming/StreamingToolPlacement.cs, src/ui/streaming/StreamingWorldView.cs, src/ui/streaming/StreamingWorldView.cs, src/ui/streaming/WorldJobBoard.cs, tests/Phase5StressTestRunner.cs, tests/TerrainWindowBench.cs, tests/TestDestroyerStreaming.cs, tests/TestDestroyerWorldGen.cs, tests/TestGeneratorBench.cs, tests/TestIslandMirror.cs, tests/TestIslandZonePersistence.cs, tests/TestWorldGenSeamAudit.cs, tests/TestWorldGenV2.cs, tests/TestWorldStoreExit.cs, tests/test_streamingview_decor_seam.cs
  → Исходящие (3): src/core/world/BiomeMapper.cs, src/core/world/BiomeType.cs, src/core/world/activearea/ActiveArea.cs

[src/core/world/streaming/integration/IWorldCellSource.cs] (C#, LoC: 16, I: 0.25)
  ← Входящие (6): src/core/world/access/ChunkWorldAccess.cs, src/core/world/streaming/integration/WorldGameRules.cs, src/ui/streaming/StreamingWorldView.cs, src/ui/streaming/StreamingWorldView.cs, tests/Phase5StressTestRunner.cs, tests/Phase5StressTestRunner.cs
  → Исходящие (2): src/core/world/streaming/integration/IBlockWorld.cs, src/core/world/streaming/integration/IBlockWorld.cs

[src/core/world/streaming/integration/StreamingWorldManager.cs] (C#, LoC: 212, I: 0.46)
  ← Входящие (7): scenes/main/Main.cs, src/ui/streaming/StreamingToolPlacement.cs, src/ui/streaming/StreamingWorldView.cs, src/ui/streaming/WorldJobBoard.cs, tests/Phase5StressTestRunner.cs, tests/TestDestroyerStreaming.cs, tests/TestWorldStoreExit.cs
  → Исходящие (6): src/core/world/streaming/integration/IBlockWorld.cs, src/core/world/streaming/integration/IWallWorld.cs, src/core/world/streaming/integration/WorldCellQuery.cs, src/core/world/streaming/generation/WorldCoordinates.cs, src/core/world/streaming/integration/WorldDeltaKind.cs, src/core/world/streaming/coordinator/WorldSaveCoordinator.cs

[src/core/world/streaming/integration/WorldCellQuery.cs] (C#, LoC: 214, I: 0.28)
  ← Входящие (13): src/core/world/streaming/integration/StreamingWorldManager.cs, src/core/world/streaming/integration/WorldGameRules.cs, src/simulation/agents/AgentMovementService.cs, src/ui/controllers/HUDController.cs, src/ui/renderers/FertilityOverlayRenderer.cs, src/ui/renderers/HumidityOverlayRenderer.cs, src/ui/streaming/StreamingWorldView.cs, tests/Phase5StressTestRunner.cs, tests/TerrainWindowBench.cs, tests/TestDestroyerStreaming.cs, tests/TestDestroyerWorldGen.cs, tests/TestGeneratorBench.cs, tests/TestGeneratorBench.cs
  → Исходящие (5): src/core/world/generation/UnifiedWorldGenerator.cs, src/core/world/streaming/generation/WorldClimateGenerator.cs, src/core/world/BiomeMapper.cs, src/core/world/BiomeType.cs, src/core/world/activearea/ActiveArea.cs

[src/core/world/streaming/integration/WorldDecor.cs] (C#, LoC: 13, I: 0.0)
  ← Входящие (8): src/core/world/access/ChunkWorldAccess.cs, src/core/world/generation/UnifiedWorldGenerator.cs, src/core/world/generation/WorldGenV2.cs, src/core/world/streaming/integration/WorldGameRules.cs, src/ui/streaming/StreamingWorldView.cs, tests/TestDestroyerWorldGen.cs, tests/TestWorldGenV2.cs, tests/test_streamingview_decor_seam.cs

[src/core/world/streaming/integration/WorldDeltaKind.cs] (C#, LoC: 129, I: 0.0)
  ← Входящие (16): src/core/world/access/ChunkWorldAccess.cs, src/core/world/mutation/IslandDeltaApplicator.cs, src/core/world/mutation/IslandDomainReplay.cs, src/core/world/mutation/IslandMutationMirror.cs, src/core/world/mutation/IslandStateReplay.cs, src/core/world/mutation/IslandStateSnapshot.cs, src/core/world/streaming/integration/StreamingWorldManager.cs, src/core/world/streaming/integration/WorldGameRules.cs, src/core/world/streaming/integration/WorldPlan.cs, src/ui/streaming/StreamingToolPlacement.cs, src/ui/streaming/StreamingWorldView.cs, tests/Phase5StressTestRunner.cs, tests/TestDestroyerStreaming.cs, tests/TestIslandMirror.cs, tests/TestIslandZonePersistence.cs, tests/TestWorldStoreExit.cs

[src/core/world/streaming/integration/WorldGameRules.cs] (C#, LoC: 184, I: 0.67)
  ← Входящие (3): scenes/main/Main.cs, src/ui/streaming/StreamingToolPlacement.cs, tests/Phase5StressTestRunner.cs
  → Исходящие (6): src/core/world/streaming/integration/IBlockWorld.cs, src/core/world/streaming/integration/IWorldCellSource.cs, src/core/world/streaming/integration/WorldCellQuery.cs, src/core/world/streaming/generation/WorldCoordinates.cs, src/core/world/streaming/integration/WorldDecor.cs, src/core/world/streaming/integration/WorldDeltaKind.cs

[src/core/world/streaming/integration/WorldPlan.cs] (C#, LoC: 24, I: 0.33)
  ← Входящие (2): src/ui/streaming/StreamingWorldView.cs, tests/TestDestroyerStreaming.cs
  → Исходящие (1): src/core/world/streaming/integration/WorldDeltaKind.cs

[src/core/world/streaming/runtime/ChunkWindowPlanner.cs] (C#, LoC: 244, I: 0.2)
  ← Входящие (4): src/core/world/streaming/runtime/WorldChunkStreamer.cs, src/core/world/streaming/runtime/WorldChunkStreamer.cs, src/ui/streaming/StreamingWorldView.cs, src/ui/streaming/StreamingWorldView.cs
  → Исходящие (1): src/core/world/streaming/generation/WorldCoordinates.cs

[src/core/world/streaming/runtime/WorldChunkStreamer.cs] (C#, LoC: 860, I: 0.89)
  ← Входящие (1): src/ui/streaming/StreamingWorldView.cs
  → Исходящие (8): src/core/world/streaming/storage/WorldSaveMetadata.cs, src/core/world/streaming/runtime/ChunkWindowPlanner.cs, src/core/world/streaming/runtime/ChunkWindowPlanner.cs, src/core/world/generation/UnifiedWorldGenerator.cs, src/core/world/streaming/generation/WorldChunk.cs, src/core/world/streaming/storage/WorldChunkCodec.cs, src/core/world/streaming/storage/WorldChunkStore.cs, src/core/world/streaming/generation/WorldCoordinates.cs

[src/core/world/streaming/storage/WorldChunkCodec.cs] (C#, LoC: 223, I: 0.2)
  ← Входящие (4): src/core/world/streaming/runtime/WorldChunkStreamer.cs, src/core/world/streaming/storage/WorldChunkDeltaCodec.cs, src/core/world/streaming/storage/WorldChunkStore.cs, src/ui/streaming/StreamingWorldView.cs
  → Исходящие (1): src/core/world/streaming/storage/WorldSaveMetadata.cs

[src/core/world/streaming/storage/WorldChunkDeltaCodec.cs] (C#, LoC: 298, I: 0.17)
  ← Входящие (5): src/core/world/streaming/integration/ChunkMutations.cs, src/core/world/streaming/integration/ChunkMutations.cs, src/ui/streaming/StreamingWorldView.cs, src/ui/streaming/StreamingWorldView.cs, src/ui/streaming/StreamingWorldView.cs
  → Исходящие (1): src/core/world/streaming/storage/WorldChunkCodec.cs

[src/core/world/streaming/storage/WorldChunkStore.cs] (C#, LoC: 279, I: 0.4)
  ← Входящие (3): src/core/world/streaming/coordinator/WorldSaveCoordinator.cs, src/core/world/streaming/runtime/WorldChunkStreamer.cs, src/ui/streaming/StreamingWorldView.cs
  → Исходящие (2): src/core/world/streaming/storage/WorldSaveMetadata.cs, src/core/world/streaming/storage/WorldChunkCodec.cs

[src/core/world/streaming/storage/WorldManifestStore.cs] (C#, LoC: 271, I: 0.0)
  ← Входящие (1): src/core/world/streaming/coordinator/WorldSaveCoordinator.cs

[src/core/world/streaming/storage/WorldSaveMetadata.cs] (C#, LoC: 20, I: 0.0)
  ← Входящие (5): src/core/world/streaming/coordinator/WorldSaveCoordinator.cs, src/core/world/streaming/runtime/WorldChunkStreamer.cs, src/core/world/streaming/storage/WorldChunkCodec.cs, src/core/world/streaming/storage/WorldChunkStore.cs, src/ui/streaming/StreamingWorldView.cs

[src/simulation/AgentSimulationThread.cs] (C#, LoC: 1461, I: 0.47)
  ← Входящие (52): scenes/main/Main.cs, src/core/world/mutation/IslandDeltaApplicator.cs, src/core/world/mutation/IslandDomainReplay.cs, src/core/world/mutation/IslandMutationMirror.cs, src/core/world/mutation/IslandPayload.cs, src/core/world/mutation/IslandStateReplay.cs, src/core/world/mutation/IslandStateSnapshot.cs, src/simulation/TimeManager.cs, src/simulation/agents/AgentMovementService.cs, src/simulation/offline/OfflineCatchUpPolicy.cs, src/simulation/offline/OfflineWorldTicker.cs, src/simulation/zones/ZonePersistence.cs, src/ui/DayNightCycle.cs, src/ui/PerformanceOverlay.cs, src/ui/PlayerInteractionManager.cs, src/ui/controllers/FarmController.cs, src/ui/controllers/HUDController.cs, src/ui/controllers/PriorityBoardController.cs, src/ui/controllers/WorkZoneController.cs, src/ui/renderers/AgentRenderer.cs, src/ui/renderers/CropRenderer.cs, src/ui/renderers/DesignationRenderer.cs, src/ui/renderers/FarmZoneRenderer.cs, src/ui/renderers/GroundItemRenderer.cs, src/ui/renderers/MapRenderer.cs, src/ui/renderers/ShadowCasterRenderer.cs, src/ui/renderers/StockpileItemRenderer.cs, src/ui/tools/BuildTool.cs, src/ui/tools/FarmingTool.cs, src/ui/tools/SelectTool.cs, src/ui/tools/StoneMiningTool.cs, src/ui/tools/TreeFellingTool.cs, src/ui/tools/WarehouseTool.cs, src/ui/tools/ZoneDraftTool.cs, tests/BenchJobAssignment.cs, tests/BenchPathfinding.cs, tests/Phase1StressTestRunner.cs, tests/Phase2StressTestRunner.cs, tests/Phase3StressTestRunner.cs, tests/Phase4StressTestRunner.cs, tests/Phase5StressTestRunner.cs, tests/TestAdversarialConstruction.cs, tests/TestAdversarialJobDistribution.cs, tests/TestIslandMirror.cs, tests/TestIslandZonePersistence.cs, tests/TestJobRaceStress.cs, tests/TestRenderLodBenchmark.cs, tests/TestWorkProgressPersistence.cs, tests/test_central_block_zone.cs, tests/test_zone_agents.cs, tests/test_zone_model.cs, tests/test_zone_router.cs
  → Исходящие (47): src/simulation/agents/AgentDataPool.cs, src/simulation/agents/AgentMovementService.cs, src/simulation/agents/AgentDataPool.cs, src/simulation/agents/AgentSpatialGrid.cs, src/simulation/jobs/handlers/BlueprintDeliveryJobHandler.cs, src/simulation/jobs/handlers/ConstructionJobHandler.cs, src/simulation/structures/ConstructionManager.cs, src/simulation/farming/CropGrowthManager.cs, src/simulation/DynamicWorkBalancer.cs, src/simulation/scheduling/DynamicWorkScheduler.cs, src/simulation/farming/FarmJobManager.cs, src/simulation/jobs/handlers/FarmingJobHandler.cs, src/core/world/FertilityMap.cs, src/core/common/GameProfiler.cs, src/simulation/gpu/GpuHumiditySimulator.cs, src/simulation/items/GroundItemManager.cs, src/simulation/jobs/handlers/HarvestJobHandler.cs, src/simulation/pathfinding/HierarchicalPathfinder.cs, src/core/world/HumidityMap.cs, src/simulation/jobs/JobBroker.cs, src/simulation/jobs/JobDispatcher.cs, src/simulation/jobs/JobRegistry.cs, src/simulation/jobs/JobSpatialPyramid.cs, src/simulation/jobs/JobValidator.cs, src/simulation/scheduling/LoadMonitor.cs, src/simulation/jobs/handlers/MiningJobHandler.cs, src/simulation/jobs/handlers/NeedsJobSystem.cs, src/simulation/ParallelRng.cs, src/simulation/jobs/handlers/PlantingJobHandler.cs, src/core/common/SimEvents.cs, src/simulation/simd/SimdNeedsBatch.cs, src/simulation/SimulationContext.cs, src/simulation/jobs/handlers/StockpileHaulingJobHandler.cs, src/simulation/items/StockpileManager.cs, src/simulation/jobs/handlers/TreeChoppingJobHandler.cs, src/simulation/jobs/handlers/WanderJobSystem.cs, src/simulation/agents/WorkerZoneJobSystem.cs, src/simulation/WorldTime.cs, src/simulation/zones/ZoneJobRouter.cs, src/simulation/zones/ZoneManager.cs, src/core/common/CpuTopology.cs, src/core/common/ProfilerLogService.cs, src/core/common/ResourceRef.cs, src/simulation/scheduling/AdaptivePartitioner.cs, src/simulation/scheduling/IDynamicWorkScheduler.cs, src/simulation/scheduling/WorkItem.cs, src/simulation/simd/SimdMovementBatch.cs

[src/simulation/CellProgressStore.cs] (C#, LoC: 57, I: 0.0)
  ← Входящие (53): scenes/main/Main.cs, src/core/world/mutation/IslandDeltaApplicator.cs, src/core/world/mutation/IslandDomainReplay.cs, src/core/world/mutation/IslandMutationMirror.cs, src/core/world/mutation/IslandPayload.cs, src/core/world/mutation/IslandStateReplay.cs, src/core/world/mutation/IslandStateSnapshot.cs, src/simulation/farming/FarmJobManager.cs, src/simulation/offline/OfflineCatchUpPolicy.cs, src/simulation/offline/OfflineWorldTicker.cs, src/simulation/stones/StoneJobManager.cs, src/simulation/trees/TreeJobManager.cs, src/simulation/zones/ZonePersistence.cs, src/ui/DayNightCycle.cs, src/ui/PerformanceOverlay.cs, src/ui/PlayerInteractionManager.cs, src/ui/controllers/FarmController.cs, src/ui/controllers/HUDController.cs, src/ui/controllers/PriorityBoardController.cs, src/ui/controllers/WorkZoneController.cs, src/ui/renderers/AgentRenderer.cs, src/ui/renderers/CropRenderer.cs, src/ui/renderers/DesignationRenderer.cs, src/ui/renderers/FarmZoneRenderer.cs, src/ui/renderers/GroundItemRenderer.cs, src/ui/renderers/MapRenderer.cs, src/ui/renderers/ShadowCasterRenderer.cs, src/ui/renderers/StockpileItemRenderer.cs, src/ui/tools/BuildTool.cs, src/ui/tools/FarmingTool.cs, src/ui/tools/SelectTool.cs, src/ui/tools/StoneMiningTool.cs, src/ui/tools/TreeFellingTool.cs, src/ui/tools/WarehouseTool.cs, src/ui/tools/ZoneDraftTool.cs, tests/BenchJobAssignment.cs, tests/BenchPathfinding.cs, tests/Phase1StressTestRunner.cs, tests/Phase2StressTestRunner.cs, tests/Phase3StressTestRunner.cs, tests/Phase4StressTestRunner.cs, tests/Phase5StressTestRunner.cs, tests/TestAdversarialConstruction.cs, tests/TestAdversarialJobDistribution.cs, tests/TestIslandMirror.cs, tests/TestIslandZonePersistence.cs, tests/TestJobRaceStress.cs, tests/TestRenderLodBenchmark.cs, tests/TestWorkProgressPersistence.cs, tests/test_central_block_zone.cs, tests/test_zone_agents.cs, tests/test_zone_model.cs, tests/test_zone_router.cs

[src/simulation/DynamicWorkBalancer.cs] (C#, LoC: 67, I: 0.07)
  ← Входящие (52): scenes/main/Main.cs, src/core/world/mutation/IslandDeltaApplicator.cs, src/core/world/mutation/IslandDomainReplay.cs, src/core/world/mutation/IslandMutationMirror.cs, src/core/world/mutation/IslandPayload.cs, src/core/world/mutation/IslandStateReplay.cs, src/core/world/mutation/IslandStateSnapshot.cs, src/simulation/AgentSimulationThread.cs, src/simulation/gpu/GpuAgentTransformSimulator.cs, src/simulation/offline/OfflineCatchUpPolicy.cs, src/simulation/offline/OfflineWorldTicker.cs, src/simulation/zones/ZonePersistence.cs, src/ui/DayNightCycle.cs, src/ui/PerformanceOverlay.cs, src/ui/PlayerInteractionManager.cs, src/ui/controllers/FarmController.cs, src/ui/controllers/HUDController.cs, src/ui/controllers/PriorityBoardController.cs, src/ui/controllers/WorkZoneController.cs, src/ui/renderers/AgentRenderer.cs, src/ui/renderers/CropRenderer.cs, src/ui/renderers/DesignationRenderer.cs, src/ui/renderers/FarmZoneRenderer.cs, src/ui/renderers/GroundItemRenderer.cs, src/ui/renderers/MapRenderer.cs, src/ui/renderers/ShadowCasterRenderer.cs, src/ui/renderers/StockpileItemRenderer.cs, src/ui/tools/BuildTool.cs, src/ui/tools/FarmingTool.cs, src/ui/tools/SelectTool.cs, src/ui/tools/StoneMiningTool.cs, src/ui/tools/TreeFellingTool.cs, src/ui/tools/WarehouseTool.cs, src/ui/tools/ZoneDraftTool.cs, tests/BenchJobAssignment.cs, tests/BenchPathfinding.cs, tests/Phase1StressTestRunner.cs, tests/Phase2StressTestRunner.cs, tests/Phase3StressTestRunner.cs, tests/Phase4StressTestRunner.cs, tests/Phase5StressTestRunner.cs, tests/TestAdversarialConstruction.cs, tests/TestAdversarialJobDistribution.cs, tests/TestIslandMirror.cs, tests/TestIslandZonePersistence.cs, tests/TestJobRaceStress.cs, tests/TestRenderLodBenchmark.cs, tests/TestWorkProgressPersistence.cs, tests/test_central_block_zone.cs, tests/test_zone_agents.cs, tests/test_zone_model.cs, tests/test_zone_router.cs
  → Исходящие (4): src/simulation/scheduling/DynamicWorkScheduler.cs, src/simulation/scheduling/AdaptivePartitioner.cs, src/simulation/scheduling/IDynamicWorkScheduler.cs, src/simulation/scheduling/LoadMonitor.cs

[src/simulation/GameSpeed.cs] (C#, LoC: 11, I: 0.0)
  ← Входящие (7): scenes/main/Main.cs, src/ui/PerformanceOverlay.cs, src/ui/renderers/AgentRenderer.cs, tests/Phase1StressTestRunner.cs, tests/Phase3StressTestRunner.cs, tests/TestRenderLodBenchmark.cs, tests/test_zone_agents.cs

[src/simulation/ParallelRng.cs] (C#, LoC: 65, I: 0.0)
  ← Входящие (15): src/simulation/AgentSimulationThread.cs, src/simulation/agents/AgentMovementService.cs, src/simulation/farming/CropGrowthManager.cs, src/simulation/jobs/JobDispatcher.cs, src/simulation/jobs/handlers/BlueprintDeliveryJobHandler.cs, src/simulation/jobs/handlers/ConstructionJobHandler.cs, src/simulation/jobs/handlers/FarmingJobHandler.cs, src/simulation/jobs/handlers/HarvestJobHandler.cs, src/simulation/jobs/handlers/MiningJobHandler.cs, src/simulation/jobs/handlers/NeedsJobSystem.cs, src/simulation/jobs/handlers/PlantingJobHandler.cs, src/simulation/jobs/handlers/StockpileHaulingJobHandler.cs, src/simulation/jobs/handlers/TreeChoppingJobHandler.cs, src/simulation/jobs/handlers/WanderJobSystem.cs, src/simulation/scheduling/DynamicWorkScheduler.cs

[src/simulation/SimulationContext.cs] (C#, LoC: 52, I: 0.15)
  ← Входящие (40): src/core/world/mutation/IslandDeltaApplicator.cs, src/core/world/mutation/IslandDomainReplay.cs, src/simulation/AgentSimulationThread.cs, src/simulation/agents/AgentMovementService.cs, src/simulation/agents/FlowFieldManager.cs, src/simulation/agents/WorkerAssignmentSystem.cs, src/simulation/agents/WorkerZoneJobSystem.cs, src/simulation/building/ConstructionPipeline.cs, src/simulation/building/TileClearanceService.cs, src/simulation/farming/CropGrowthManager.cs, src/simulation/jobs/GenericJobSpatialIndex.cs, src/simulation/jobs/IJobHandler.cs, src/simulation/jobs/JobBroker.cs, src/simulation/jobs/JobDispatcher.cs, src/simulation/jobs/JobSpatialPyramid.cs, src/simulation/jobs/JobValidator.cs, src/simulation/jobs/handlers/BlueprintDeliveryJobHandler.cs, src/simulation/jobs/handlers/ConstructionJobHandler.cs, src/simulation/jobs/handlers/FarmingJobHandler.cs, src/simulation/jobs/handlers/HarvestJobHandler.cs, src/simulation/jobs/handlers/MiningJobHandler.cs, src/simulation/jobs/handlers/NeedsJobSystem.cs, src/simulation/jobs/handlers/PlantingJobHandler.cs, src/simulation/jobs/handlers/StockpileHaulingJobHandler.cs, src/simulation/jobs/handlers/TreeChoppingJobHandler.cs, src/simulation/jobs/handlers/WanderJobSystem.cs, src/simulation/pathfinding/HierarchicalPathfinder.cs, src/simulation/stones/StoneJobManager.cs, src/simulation/structures/ConstructionManager.cs, src/simulation/structures/StructureManager.cs, src/simulation/trees/TreeJobManager.cs, src/simulation/zones/WorkZoneManagerFacade.cs, src/simulation/zones/ZoneManager.cs, tests/BenchJobAssignment.cs, tests/Phase4StressTestRunner.cs, tests/Phase5StressTestRunner.cs, tests/TestAdversarialConstruction.cs, tests/TestJobRaceStress.cs, tests/TestWorkProgressPersistence.cs, tests/test_zone_agents.cs
  → Исходящие (7): src/simulation/agents/AgentMovementService.cs, src/simulation/agents/AgentSpatialGrid.cs, src/core/world/FertilityMap.cs, src/core/world/HumidityMap.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/TimeManager.cs] (C#, LoC: 51, I: 0.33)
  ← Входящие (6): scenes/main/Main.cs, src/ui/PerformanceOverlay.cs, src/ui/controllers/HUDController.cs, src/ui/renderers/MapRenderer.cs, src/ui/streaming/StreamingWorldView.cs, tests/Phase3StressTestRunner.cs
  → Исходящие (3): src/simulation/AgentSimulationThread.cs, src/core/common/SimEvents.cs, src/simulation/WorldTime.cs

[src/simulation/WorldTime.cs] (C#, LoC: 34, I: 0.0)
  ← Входящие (12): scenes/main/Main.cs, src/simulation/AgentSimulationThread.cs, src/simulation/TimeManager.cs, src/simulation/building/BuildConfig.cs, src/simulation/jobs/JobBroker.cs, src/simulation/jobs/handlers/FarmingJobHandler.cs, src/simulation/jobs/handlers/MiningJobHandler.cs, src/simulation/jobs/handlers/TreeChoppingJobHandler.cs, src/simulation/offline/OfflineCatchUpPolicy.cs, src/ui/DayNightCycle.cs, src/ui/controllers/HUDController.cs, tests/TestWorkProgressPersistence.cs

[src/simulation/agents/AgentDataPool.cs] (C#, LoC: 280, I: 0.0)
  ← Входящие (45): src/simulation/AgentSimulationThread.cs, src/simulation/AgentSimulationThread.cs, src/simulation/agents/AgentMovementService.cs, src/simulation/agents/AgentMovementService.cs, src/simulation/agents/AgentMovementService.cs, src/simulation/agents/AgentSpatialGrid.cs, src/simulation/agents/FlatIdleWorkerPool.cs, src/simulation/agents/IdleWorkerSpatialGrid.cs, src/simulation/agents/WorkerAssignmentSystem.cs, src/simulation/agents/WorkerZoneJobSystem.cs, src/simulation/agents/WorkerZoneJobSystem.cs, src/simulation/gpu/GpuAgentTransformSimulator.cs, src/simulation/jobs/GenericJobSpatialIndex.cs, src/simulation/jobs/IJobHandler.cs, src/simulation/jobs/JobDispatcher.cs, src/simulation/jobs/JobSpatialPyramid.cs, src/simulation/jobs/JobValidator.cs, src/simulation/jobs/handlers/BlueprintDeliveryJobHandler.cs, src/simulation/jobs/handlers/ConstructionJobHandler.cs, src/simulation/jobs/handlers/FarmingJobHandler.cs, src/simulation/jobs/handlers/HarvestJobHandler.cs, src/simulation/jobs/handlers/MiningJobHandler.cs, src/simulation/jobs/handlers/NeedsJobSystem.cs, src/simulation/jobs/handlers/NeedsJobSystem.cs, src/simulation/jobs/handlers/PlantingJobHandler.cs, src/simulation/jobs/handlers/StockpileHaulingJobHandler.cs, src/simulation/jobs/handlers/TreeChoppingJobHandler.cs, src/simulation/jobs/handlers/WanderJobSystem.cs, src/simulation/offline/OfflineAgentNeeds.cs, src/simulation/offline/OfflineWorldTicker.cs, src/simulation/simd/SimdMovementBatch.cs, src/simulation/simd/SimdMovementBatch.cs, src/simulation/simd/SimdNeedsBatch.cs, src/simulation/simd/SimdNeedsBatch.cs, src/simulation/structures/ConstructionManager.cs, src/simulation/structures/StructureManager.cs, src/simulation/zones/WorkZoneManagerFacade.cs, src/simulation/zones/ZoneManager.cs, tests/BenchJobAssignment.cs, tests/Phase4StressTestRunner.cs, tests/TestAdversarialJobDistribution.cs, tests/TestJobRaceStress.cs, tests/TestWorkProgressPersistence.cs, tests/test_zone_agents.cs, tests/test_zone_agents.cs

[src/simulation/agents/AgentMovementService.cs] (C#, LoC: 504, I: 0.61)
  ← Входящие (9): src/simulation/AgentSimulationThread.cs, src/simulation/SimulationContext.cs, tests/BenchJobAssignment.cs, tests/Phase4StressTestRunner.cs, tests/Phase5StressTestRunner.cs, tests/TestAdversarialConstruction.cs, tests/TestJobRaceStress.cs, tests/TestWorkProgressPersistence.cs, tests/test_zone_agents.cs
  → Исходящие (14): src/simulation/agents/AgentDataPool.cs, src/simulation/agents/AgentDataPool.cs, src/simulation/agents/AgentDataPool.cs, src/simulation/AgentSimulationThread.cs, src/simulation/agents/FlowFieldManager.cs, src/core/world/GridHelper.cs, src/simulation/pathfinding/HierarchicalPathfinder.cs, src/simulation/ParallelRng.cs, src/simulation/SimulationContext.cs, src/core/world/generation/UnifiedWorldGenerator.cs, src/core/world/streaming/integration/WorldCellQuery.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/agents/AgentSpatialGrid.cs] (C#, LoC: 185, I: 0.31)
  ← Входящие (9): src/simulation/AgentSimulationThread.cs, src/simulation/SimulationContext.cs, tests/BenchJobAssignment.cs, tests/Phase4StressTestRunner.cs, tests/Phase5StressTestRunner.cs, tests/TestAdversarialConstruction.cs, tests/TestJobRaceStress.cs, tests/TestWorkProgressPersistence.cs, tests/test_zone_agents.cs
  → Исходящие (4): src/simulation/agents/AgentDataPool.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/agents/AgentState.cs] (C#, LoC: 13, I: 0.5)
  (изолированный файл)

[src/simulation/agents/FlatIdleWorkerPool.cs] (C#, LoC: 148, I: 0.57)
  ← Входящие (3): src/simulation/jobs/JobDispatcher.cs, tests/Phase1StressTestRunner.cs, tests/TestAdversarialJobDistribution.cs
  → Исходящие (4): src/simulation/agents/AgentDataPool.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/agents/FlowFieldManager.cs] (C#, LoC: 224, I: 0.54)
  ← Входящие (6): scenes/main/Main.cs, src/simulation/agents/AgentMovementService.cs, src/simulation/pathfinding/HierarchicalPathfinder.cs, src/simulation/structures/StructureManager.cs, src/ui/controllers/MainMenuController.cs, tests/Phase1StressTestRunner.cs
  → Исходящие (7): src/simulation/gpu/GpuFlowFieldSimulator.cs, src/simulation/pathfinding/HierarchicalPathfinder.cs, src/simulation/pathfinding/HierarchicalPathfinder.cs, src/simulation/SimulationContext.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/agents/IdleWorkerSpatialGrid.cs] (C#, LoC: 402, I: 1.0)
  → Исходящие (4): src/simulation/agents/AgentDataPool.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/agents/WorkerAssignmentSystem.cs] (C#, LoC: 197, I: 0.8)
  ← Входящие (2): src/simulation/agents/WorkerZoneJobSystem.cs, src/simulation/zones/WorkZoneManagerFacade.cs
  → Исходящие (8): src/simulation/agents/AgentDataPool.cs, src/simulation/jobs/JobDispatcher.cs, src/simulation/SimulationContext.cs, src/simulation/zones/WorkZone.cs, src/simulation/zones/ZoneManager.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/agents/WorkerZoneJobSystem.cs] (C#, LoC: 126, I: 0.85)
  ← Входящие (2): src/simulation/AgentSimulationThread.cs, tests/test_zone_agents.cs
  → Исходящие (11): src/simulation/agents/AgentDataPool.cs, src/simulation/agents/AgentDataPool.cs, src/simulation/jobs/JobDispatcher.cs, src/simulation/jobs/JobRegistry.cs, src/simulation/SimulationContext.cs, src/simulation/agents/WorkerAssignmentSystem.cs, src/simulation/zones/ZoneJobRouter.cs, src/simulation/zones/ZoneManager.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/building/BlueprintManager.cs] (C#, LoC: 133, I: 0.38)
  ← Входящие (10): scenes/main/Main.cs, src/simulation/building/BuildObstacles.cs, src/simulation/building/TileClearanceService.cs, src/simulation/jobs/JobValidator.cs, src/simulation/jobs/handlers/ConstructionJobHandler.cs, src/ui/controllers/MainMenuController.cs, src/ui/renderers/MapRenderer.cs, src/ui/tools/BuildTool.cs, src/ui/tools/FarmingTool.cs, src/ui/tools/ZoneDraftTool.cs
  → Исходящие (6): src/simulation/building/BuildConfig.cs, src/simulation/structures/ConstructionManager.cs, src/simulation/structures/StructureRegistry.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/building/BuildConfig.cs] (C#, LoC: 62, I: 0.38)
  ← Входящие (8): src/simulation/building/BlueprintManager.cs, src/simulation/jobs/JobBroker.cs, src/simulation/jobs/handlers/ConstructionJobHandler.cs, src/simulation/structures/ConstructionManager.cs, src/simulation/structures/StructureRegistry.cs, tests/TestAdversarialConstruction.cs, tests/TestWorkProgressPersistence.cs, tests/test_central_block_zone.cs
  → Исходящие (5): src/core/items/ItemRegistry.cs, src/simulation/WorldTime.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/building/BuildObstacles.cs] (C#, LoC: 86, I: 1.0)
  → Исходящие (10): src/simulation/building/BlueprintManager.cs, src/simulation/building/BuildingManager.cs, src/simulation/structures/ConstructionManager.cs, src/simulation/farming/FarmJobManager.cs, src/core/world/MapData.cs, src/simulation/structures/StructureManager.cs, src/simulation/building/TileClearanceService.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/building/BuildingManager.cs] (C#, LoC: 81, I: 0.38)
  ← Входящие (8): scenes/main/Main.cs, src/simulation/building/BuildObstacles.cs, src/simulation/building/TileClearanceService.cs, src/ui/controllers/MainMenuController.cs, src/ui/renderers/MapRenderer.cs, src/ui/tools/BuildTool.cs, src/ui/tools/FarmingTool.cs, src/ui/tools/ZoneDraftTool.cs
  → Исходящие (5): src/simulation/structures/StructureManager.cs, src/simulation/structures/StructureRegistry.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/building/ConstructionPipeline.cs] (C#, LoC: 66, I: 1.0)
  → Исходящие (7): src/simulation/structures/ConstructionManager.cs, src/core/world/MapData.cs, src/simulation/SimulationContext.cs, src/simulation/structures/StructureRegistry.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/building/TileClearanceService.cs] (C#, LoC: 191, I: 0.8)
  ← Входящие (3): src/simulation/building/BuildObstacles.cs, src/simulation/jobs/handlers/FarmingJobHandler.cs, tests/Phase5StressTestRunner.cs
  → Исходящие (12): src/simulation/building/BlueprintManager.cs, src/simulation/building/BuildingManager.cs, src/simulation/structures/ConstructionManager.cs, src/simulation/structures/ConstructionSite.cs, src/simulation/farming/FarmJobManager.cs, src/simulation/items/GroundItemManager.cs, src/core/world/MapData.cs, src/simulation/SimulationContext.cs, src/simulation/structures/StructureManager.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/building/WallBuildManager.cs] (C#, LoC: 44, I: 0.45)
  ← Входящие (6): src/ui/controllers/BuildMenuController.cs, src/ui/controllers/HUDController.cs, src/ui/renderers/MapRenderer.cs, src/ui/tools/BuildTool.cs, src/ui/tools/FarmingTool.cs, src/ui/tools/ZoneDraftTool.cs
  → Исходящие (5): src/simulation/structures/StructureManager.cs, src/simulation/structures/StructureRegistry.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/building/WorkProgressTracker.cs] (C#, LoC: 184, I: 0.31)
  ← Входящие (11): scenes/main/Main.cs, src/core/world/WallTileHelper.cs, src/simulation/jobs/handlers/ConstructionJobHandler.cs, src/simulation/jobs/handlers/FarmingJobHandler.cs, src/simulation/jobs/handlers/HarvestJobHandler.cs, src/simulation/jobs/handlers/MiningJobHandler.cs, src/simulation/jobs/handlers/PlantingJobHandler.cs, src/simulation/jobs/handlers/TreeChoppingJobHandler.cs, src/ui/controllers/MainMenuController.cs, src/ui/controllers/WallMaterialRegistry.cs, src/ui/renderers/MapRenderer.cs
  → Исходящие (5): src/core/world/WallTileHelper.cs, src/ui/controllers/WallMaterialRegistry.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/farming/CropData.cs] (C#, LoC: 12, I: 0.0)
  ← Входящие (2): src/core/world/mutation/IslandStateSnapshot.cs, src/simulation/farming/CropGrowthManager.cs

[src/simulation/farming/CropGrowthManager.cs] (C#, LoC: 312, I: 0.57)
  ← Входящие (10): scenes/main/Main.cs, src/core/world/mutation/IslandStateReplay.cs, src/core/world/mutation/IslandStateSnapshot.cs, src/simulation/AgentSimulationThread.cs, src/simulation/jobs/handlers/HarvestJobHandler.cs, src/simulation/jobs/handlers/PlantingJobHandler.cs, src/simulation/zones/ZoneManager.cs, src/ui/controllers/FarmController.cs, src/ui/controllers/MainMenuController.cs, src/ui/renderers/CropRenderer.cs
  → Исходящие (13): src/simulation/farming/CropData.cs, src/core/world/FarmYield.cs, src/simulation/farming/FarmZone.cs, src/simulation/farming/FarmZoneManagerFacade.cs, src/core/world/FertilityMap.cs, src/simulation/items/GroundItemManager.cs, src/core/world/HumidityMap.cs, src/simulation/jobs/JobBroker.cs, src/simulation/ParallelRng.cs, src/simulation/SimulationContext.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/farming/FarmJobManager.cs] (C#, LoC: 381, I: 0.23)
  ← Входящие (20): scenes/main/Main.cs, src/core/world/mutation/IslandDomainReplay.cs, src/core/world/mutation/IslandMutationMirror.cs, src/simulation/AgentSimulationThread.cs, src/simulation/building/BuildObstacles.cs, src/simulation/building/TileClearanceService.cs, src/simulation/items/GroundItemManager.cs, src/simulation/jobs/JobValidator.cs, src/simulation/jobs/handlers/FarmingJobHandler.cs, src/simulation/jobs/handlers/MiningJobHandler.cs, src/simulation/jobs/handlers/PlantingJobHandler.cs, src/simulation/jobs/handlers/TreeChoppingJobHandler.cs, src/simulation/zones/ZoneManager.cs, src/ui/controllers/MainMenuController.cs, src/ui/renderers/MapRenderer.cs, src/ui/tools/BuildTool.cs, src/ui/tools/FarmingTool.cs, src/ui/tools/ZoneDraftTool.cs, tests/TestAdversarialConstruction.cs, tests/TestWorkProgressPersistence.cs
  → Исходящие (6): src/simulation/CellProgressStore.cs, src/simulation/farming/FarmZoneManagerFacade.cs, src/simulation/items/GroundItemManager.cs, src/simulation/jobs/JobBroker.cs, src/simulation/stones/StoneJobManager.cs, src/simulation/trees/TreeJobManager.cs

[src/simulation/farming/FarmZone.cs] (C#, LoC: 24, I: 0.43)
  ← Входящие (4): src/simulation/farming/CropGrowthManager.cs, src/simulation/farming/FarmZoneManagerFacade.cs, src/ui/controllers/FarmController.cs, tests/Phase2StressTestRunner.cs
  → Исходящие (3): src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/farming/FarmZoneManagerFacade.cs] (C#, LoC: 178, I: 0.31)
  ← Входящие (9): src/simulation/farming/CropGrowthManager.cs, src/simulation/farming/FarmJobManager.cs, src/simulation/jobs/handlers/PlantingJobHandler.cs, src/ui/controllers/FarmController.cs, src/ui/controllers/WorkZoneController.cs, src/ui/tools/FarmingTool.cs, src/ui/tools/SelectTool.cs, src/ui/tools/ZoneDraftTool.cs, tests/Phase2StressTestRunner.cs
  → Исходящие (4): scenes/ui/Farm.tscn, src/simulation/farming/FarmZone.cs, src/simulation/zones/Zone.cs, src/simulation/zones/ZoneManager.cs

[src/simulation/gpu/GpuAgentTransformSimulator.cs] (C#, LoC: 292, I: 1.0)
  → Исходящие (7): src/simulation/agents/AgentDataPool.cs, src/simulation/DynamicWorkBalancer.cs, src/simulation/simd/SimdMovementBatch.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs, src/simulation/simd/SimdNeedsBatch.cs

[src/simulation/gpu/GpuFlowFieldSimulator.cs] (C#, LoC: 414, I: 0.75)
  ← Входящие (1): src/simulation/agents/FlowFieldManager.cs
  → Исходящие (3): src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/gpu/GpuHumiditySimulator.cs] (C#, LoC: 322, I: 0.8)
  ← Входящие (1): src/simulation/AgentSimulationThread.cs
  → Исходящие (4): src/core/world/HumidityMap.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/items/DemandRegistry.cs] (C#, LoC: 139, I: 0.43)
  ← Входящие (4): scenes/main/Main.cs, src/simulation/items/ResourceBroker.cs, src/simulation/jobs/JobBroker.cs, tests/TestAdversarialConstruction.cs
  → Исходящие (3): src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/items/GroundItemManager.cs] (C#, LoC: 696, I: 0.28)
  ← Входящие (28): scenes/main/Main.cs, src/core/world/mutation/IslandStateReplay.cs, src/core/world/mutation/IslandStateSnapshot.cs, src/simulation/AgentSimulationThread.cs, src/simulation/building/TileClearanceService.cs, src/simulation/farming/CropGrowthManager.cs, src/simulation/farming/FarmJobManager.cs, src/simulation/items/StockpileManager.cs, src/simulation/jobs/GenericJobSpatialIndex.cs, src/simulation/jobs/JobBroker.cs, src/simulation/jobs/JobDispatcher.cs, src/simulation/jobs/JobSpatialPyramid.cs, src/simulation/jobs/JobValidator.cs, src/simulation/jobs/handlers/BlueprintDeliveryJobHandler.cs, src/simulation/jobs/handlers/ConstructionJobHandler.cs, src/simulation/jobs/handlers/MiningJobHandler.cs, src/simulation/jobs/handlers/NeedsJobSystem.cs, src/simulation/jobs/handlers/PlantingJobHandler.cs, src/simulation/jobs/handlers/StockpileHaulingJobHandler.cs, src/simulation/jobs/handlers/TreeChoppingJobHandler.cs, src/simulation/structures/ConstructionManager.cs, src/ui/PerformanceOverlay.cs, src/ui/controllers/MainMenuController.cs, src/ui/renderers/GroundItemRenderer.cs, tests/BenchJobAssignment.cs, tests/Phase3StressTestRunner.cs, tests/Phase4StressTestRunner.cs, tests/TestAdversarialConstruction.cs
  → Исходящие (11): src/simulation/structures/ConstructionManager.cs, src/simulation/farming/FarmJobManager.cs, src/core/items/ItemRegistry.cs, src/simulation/jobs/JobBroker.cs, src/core/world/MapData.cs, src/simulation/items/ResourceBroker.cs, src/core/common/SimEvents.cs, src/simulation/items/StockpileManager.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/items/ResourceBroker.cs] (C#, LoC: 37, I: 0.8)
  ← Входящие (2): src/simulation/items/GroundItemManager.cs, tests/TestAdversarialConstruction.cs
  → Исходящие (8): src/simulation/items/DemandRegistry.cs, src/simulation/jobs/JobDispatcher.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs, src/simulation/jobs/handlers/BlueprintDeliveryJobHandler.cs, src/simulation/jobs/handlers/ConstructionJobHandler.cs, src/simulation/jobs/handlers/FarmingJobHandler.cs

[src/simulation/items/StockpileManager.cs] (C#, LoC: 951, I: 0.24)
  ← Входящие (25): scenes/main/Main.cs, src/core/world/mutation/IslandDomainReplay.cs, src/core/world/mutation/IslandMutationMirror.cs, src/core/world/mutation/IslandStateReplay.cs, src/core/world/mutation/IslandStateSnapshot.cs, src/simulation/AgentSimulationThread.cs, src/simulation/items/GroundItemManager.cs, src/simulation/jobs/GenericJobSpatialIndex.cs, src/simulation/jobs/JobBroker.cs, src/simulation/jobs/JobDispatcher.cs, src/simulation/jobs/JobSpatialPyramid.cs, src/simulation/jobs/handlers/BlueprintDeliveryJobHandler.cs, src/simulation/jobs/handlers/NeedsJobSystem.cs, src/simulation/jobs/handlers/PlantingJobHandler.cs, src/simulation/jobs/handlers/StockpileHaulingJobHandler.cs, src/simulation/structures/ConstructionManager.cs, src/ui/controllers/HUDController.cs, src/ui/controllers/MainMenuController.cs, src/ui/renderers/MapRenderer.cs, src/ui/renderers/StockpileItemRenderer.cs, src/ui/tools/WarehouseTool.cs, src/ui/tools/ZoneDraftTool.cs, tests/BenchJobAssignment.cs, tests/Phase4StressTestRunner.cs, tests/TestAdversarialConstruction.cs
  → Исходящие (8): src/simulation/structures/ConstructionManager.cs, src/simulation/items/GroundItemManager.cs, src/core/items/ItemRegistry.cs, src/simulation/jobs/JobBroker.cs, src/simulation/structures/StructureManager.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/jobs/GenericJobSpatialIndex.cs] (C#, LoC: 1143, I: 1.0)
  → Исходящие (11): src/simulation/agents/AgentDataPool.cs, src/simulation/items/GroundItemManager.cs, src/core/jobs/JobData.cs, src/simulation/jobs/JobPriorityManager.cs, src/simulation/jobs/JobRegistry.cs, src/simulation/jobs/JobSpatialPyramid.cs, src/simulation/SimulationContext.cs, src/simulation/items/StockpileManager.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/jobs/IJobHandler.cs] (C#, LoC: 20, I: 0.21)
  ← Входящие (23): src/simulation/jobs/JobRegistry.cs, src/simulation/jobs/handlers/BlueprintDeliveryJobHandler.cs, src/simulation/jobs/handlers/BlueprintDeliveryJobHandler.cs, src/simulation/jobs/handlers/ConstructionJobHandler.cs, src/simulation/jobs/handlers/ConstructionJobHandler.cs, src/simulation/jobs/handlers/FarmingJobHandler.cs, src/simulation/jobs/handlers/FarmingJobHandler.cs, src/simulation/jobs/handlers/HarvestJobHandler.cs, src/simulation/jobs/handlers/HarvestJobHandler.cs, src/simulation/jobs/handlers/MiningJobHandler.cs, src/simulation/jobs/handlers/MiningJobHandler.cs, src/simulation/jobs/handlers/PlantingJobHandler.cs, src/simulation/jobs/handlers/PlantingJobHandler.cs, src/simulation/jobs/handlers/StockpileHaulingJobHandler.cs, src/simulation/jobs/handlers/StockpileHaulingJobHandler.cs, src/simulation/jobs/handlers/TreeChoppingJobHandler.cs, src/simulation/jobs/handlers/TreeChoppingJobHandler.cs, tests/BenchJobAssignment.cs, tests/BenchJobAssignment.cs, tests/TestJobRaceStress.cs, tests/TestJobRaceStress.cs, tests/test_zone_agents.cs, tests/test_zone_agents.cs
  → Исходящие (6): src/simulation/agents/AgentDataPool.cs, src/core/jobs/JobData.cs, src/simulation/SimulationContext.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/jobs/JobBroker.cs] (C#, LoC: 609, I: 0.43)
  ← Входящие (20): src/core/world/mutation/IslandStateReplay.cs, src/simulation/AgentSimulationThread.cs, src/simulation/farming/CropGrowthManager.cs, src/simulation/farming/FarmJobManager.cs, src/simulation/items/GroundItemManager.cs, src/simulation/items/StockpileManager.cs, src/simulation/jobs/JobValidator.cs, src/simulation/jobs/handlers/BlueprintDeliveryJobHandler.cs, src/simulation/jobs/handlers/ConstructionJobHandler.cs, src/simulation/jobs/handlers/FarmingJobHandler.cs, src/simulation/jobs/handlers/HarvestJobHandler.cs, src/simulation/jobs/handlers/PlantingJobHandler.cs, src/simulation/jobs/handlers/TreeChoppingJobHandler.cs, src/simulation/stones/StoneJobManager.cs, src/simulation/structures/ConstructionManager.cs, src/simulation/trees/TreeJobManager.cs, src/simulation/zones/ZoneManager.cs, src/ui/PerformanceOverlay.cs, tests/BenchJobAssignment.cs, tests/TestAdversarialConstruction.cs
  → Исходящие (15): src/simulation/building/BuildConfig.cs, src/simulation/structures/ConstructionManager.cs, src/simulation/items/DemandRegistry.cs, src/simulation/jobs/handlers/FarmingJobHandler.cs, src/core/world/GridHelper.cs, src/simulation/items/GroundItemManager.cs, src/core/jobs/JobData.cs, src/simulation/jobs/JobDispatcher.cs, src/simulation/SimulationContext.cs, src/simulation/items/StockpileManager.cs, src/simulation/WorldTime.cs, src/simulation/zones/ZoneJobRouter.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/jobs/JobDispatcher.cs] (C#, LoC: 326, I: 0.34)
  ← Входящие (29): scenes/main/Main.cs, src/simulation/AgentSimulationThread.cs, src/simulation/agents/WorkerAssignmentSystem.cs, src/simulation/agents/WorkerZoneJobSystem.cs, src/simulation/items/ResourceBroker.cs, src/simulation/jobs/JobBroker.cs, src/simulation/jobs/JobValidator.cs, src/simulation/jobs/handlers/BlueprintDeliveryJobHandler.cs, src/simulation/jobs/handlers/ConstructionJobHandler.cs, src/simulation/jobs/handlers/FarmingJobHandler.cs, src/simulation/jobs/handlers/HarvestJobHandler.cs, src/simulation/jobs/handlers/MiningJobHandler.cs, src/simulation/jobs/handlers/NeedsJobSystem.cs, src/simulation/jobs/handlers/PlantingJobHandler.cs, src/simulation/jobs/handlers/StockpileHaulingJobHandler.cs, src/simulation/jobs/handlers/TreeChoppingJobHandler.cs, src/simulation/jobs/handlers/WanderJobSystem.cs, src/simulation/structures/ConstructionManager.cs, src/simulation/zones/ZoneJobRouter.cs, src/simulation/zones/ZoneManager.cs, src/ui/controllers/HUDController.cs, src/ui/controllers/MainMenuController.cs, src/ui/controllers/WorkZoneController.cs, tests/BenchJobAssignment.cs, tests/Phase2StressTestRunner.cs, tests/TestAdversarialConstruction.cs, tests/TestAdversarialJobDistribution.cs, tests/test_zone_agents.cs, tests/test_zone_router.cs
  → Исходящие (15): src/simulation/agents/AgentDataPool.cs, src/simulation/agents/FlatIdleWorkerPool.cs, src/core/common/GameProfiler.cs, src/simulation/items/GroundItemManager.cs, src/core/jobs/JobData.cs, src/simulation/jobs/JobPriorityManager.cs, src/simulation/jobs/JobRegistry.cs, src/simulation/jobs/JobSpatialPyramid.cs, src/simulation/ParallelRng.cs, src/simulation/SimulationContext.cs, src/simulation/items/StockpileManager.cs, src/simulation/zones/ZoneJobRouter.cs, src/core/common/CpuTopology.cs, src/core/common/ProfilerLogService.cs, src/core/common/ResourceRef.cs

[src/simulation/jobs/JobPriorityManager.cs] (C#, LoC: 85, I: 0.27)
  ← Входящие (8): scenes/main/Main.cs, src/simulation/jobs/GenericJobSpatialIndex.cs, src/simulation/jobs/JobDispatcher.cs, src/simulation/jobs/JobSpatialPyramid.cs, src/simulation/zones/ZoneManager.cs, src/ui/controllers/MainMenuController.cs, src/ui/controllers/PriorityBoardController.cs, src/ui/controllers/WorkZoneController.cs
  → Исходящие (3): src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/jobs/JobRegistry.cs] (C#, LoC: 40, I: 0.27)
  ← Входящие (11): src/simulation/AgentSimulationThread.cs, src/simulation/agents/WorkerZoneJobSystem.cs, src/simulation/jobs/GenericJobSpatialIndex.cs, src/simulation/jobs/JobDispatcher.cs, src/simulation/jobs/JobSpatialPyramid.cs, src/simulation/jobs/JobValidator.cs, src/simulation/zones/ZoneManager.cs, tests/BenchJobAssignment.cs, tests/TestJobRaceStress.cs, tests/TestWorkProgressPersistence.cs, tests/test_zone_agents.cs
  → Исходящие (4): src/simulation/jobs/IJobHandler.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/jobs/JobSpatialPyramid.cs] (C#, LoC: 1363, I: 0.58)
  ← Входящие (8): src/simulation/AgentSimulationThread.cs, src/simulation/jobs/GenericJobSpatialIndex.cs, src/simulation/jobs/JobDispatcher.cs, tests/BenchJobAssignment.cs, tests/Phase2StressTestRunner.cs, tests/Phase5StressTestRunner.cs, tests/TestAdversarialJobDistribution.cs, tests/TestJobRaceStress.cs
  → Исходящие (11): src/simulation/agents/AgentDataPool.cs, src/simulation/items/GroundItemManager.cs, src/core/jobs/JobData.cs, src/simulation/jobs/JobPriorityManager.cs, src/simulation/jobs/JobRegistry.cs, src/simulation/SimulationContext.cs, src/simulation/items/StockpileManager.cs, src/simulation/zones/ZoneJobRouter.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/jobs/JobValidator.cs] (C#, LoC: 353, I: 0.94)
  ← Входящие (1): src/simulation/AgentSimulationThread.cs
  → Исходящие (15): src/simulation/agents/AgentDataPool.cs, src/simulation/building/BlueprintManager.cs, src/simulation/structures/ConstructionManager.cs, src/simulation/farming/FarmJobManager.cs, src/simulation/items/GroundItemManager.cs, src/simulation/jobs/JobBroker.cs, src/core/jobs/JobData.cs, src/simulation/jobs/JobDispatcher.cs, src/simulation/jobs/JobRegistry.cs, src/simulation/SimulationContext.cs, src/simulation/stones/StoneJobManager.cs, src/simulation/trees/TreeJobManager.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/jobs/handlers/BlueprintDeliveryJobHandler.cs] (C#, LoC: 236, I: 0.68)
  ← Входящие (7): src/simulation/AgentSimulationThread.cs, src/simulation/items/ResourceBroker.cs, tests/BenchJobAssignment.cs, tests/Phase4StressTestRunner.cs, tests/Phase5StressTestRunner.cs, tests/TestJobRaceStress.cs, tests/TestWorkProgressPersistence.cs
  → Исходящие (15): src/simulation/jobs/IJobHandler.cs, src/simulation/agents/AgentDataPool.cs, src/simulation/structures/ConstructionManager.cs, src/simulation/items/GroundItemManager.cs, src/simulation/jobs/IJobHandler.cs, src/core/items/ItemRegistry.cs, src/simulation/jobs/JobBroker.cs, src/core/jobs/JobData.cs, src/simulation/jobs/JobDispatcher.cs, src/simulation/ParallelRng.cs, src/simulation/SimulationContext.cs, src/simulation/items/StockpileManager.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/jobs/handlers/ConstructionJobHandler.cs] (C#, LoC: 159, I: 0.72)
  ← Входящие (7): src/simulation/AgentSimulationThread.cs, src/simulation/items/ResourceBroker.cs, tests/BenchJobAssignment.cs, tests/Phase4StressTestRunner.cs, tests/Phase5StressTestRunner.cs, tests/TestJobRaceStress.cs, tests/TestWorkProgressPersistence.cs
  → Исходящие (18): src/simulation/jobs/IJobHandler.cs, src/simulation/agents/AgentDataPool.cs, src/simulation/building/BlueprintManager.cs, src/simulation/building/BuildConfig.cs, src/simulation/structures/ConstructionManager.cs, src/simulation/items/GroundItemManager.cs, src/simulation/jobs/IJobHandler.cs, src/simulation/jobs/JobBroker.cs, src/core/jobs/JobData.cs, src/simulation/jobs/JobDispatcher.cs, src/simulation/ParallelRng.cs, src/simulation/SimulationContext.cs, src/simulation/structures/StructureManager.cs, src/simulation/structures/StructureRegistry.cs, src/simulation/building/WorkProgressTracker.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/jobs/handlers/FarmingJobHandler.cs] (C#, LoC: 120, I: 0.62)
  ← Входящие (9): src/simulation/AgentSimulationThread.cs, src/simulation/items/ResourceBroker.cs, src/simulation/jobs/JobBroker.cs, tests/BenchJobAssignment.cs, tests/Phase4StressTestRunner.cs, tests/Phase5StressTestRunner.cs, tests/TestAdversarialConstruction.cs, tests/TestJobRaceStress.cs, tests/TestWorkProgressPersistence.cs
  → Исходящие (15): src/simulation/jobs/IJobHandler.cs, src/simulation/agents/AgentDataPool.cs, src/simulation/farming/FarmJobManager.cs, src/simulation/jobs/IJobHandler.cs, src/simulation/jobs/JobBroker.cs, src/core/jobs/JobData.cs, src/simulation/jobs/JobDispatcher.cs, src/simulation/ParallelRng.cs, src/simulation/SimulationContext.cs, src/simulation/building/TileClearanceService.cs, src/simulation/building/WorkProgressTracker.cs, src/simulation/WorldTime.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/jobs/handlers/HarvestJobHandler.cs] (C#, LoC: 94, I: 0.81)
  ← Входящие (3): src/simulation/AgentSimulationThread.cs, tests/Phase4StressTestRunner.cs, tests/TestWorkProgressPersistence.cs
  → Исходящие (13): src/simulation/jobs/IJobHandler.cs, src/simulation/agents/AgentDataPool.cs, src/simulation/farming/CropGrowthManager.cs, src/simulation/jobs/IJobHandler.cs, src/simulation/jobs/JobBroker.cs, src/core/jobs/JobData.cs, src/simulation/jobs/JobDispatcher.cs, src/simulation/ParallelRng.cs, src/simulation/SimulationContext.cs, src/simulation/building/WorkProgressTracker.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/jobs/handlers/MiningJobHandler.cs] (C#, LoC: 152, I: 0.89)
  ← Входящие (2): src/simulation/AgentSimulationThread.cs, tests/TestWorkProgressPersistence.cs
  → Исходящие (16): src/simulation/jobs/IJobHandler.cs, src/simulation/agents/AgentDataPool.cs, src/simulation/structures/ConstructionManager.cs, src/simulation/farming/FarmJobManager.cs, src/simulation/items/GroundItemManager.cs, src/simulation/jobs/IJobHandler.cs, src/core/jobs/JobData.cs, src/simulation/jobs/JobDispatcher.cs, src/simulation/ParallelRng.cs, src/simulation/SimulationContext.cs, src/simulation/stones/StoneJobManager.cs, src/simulation/building/WorkProgressTracker.cs, src/simulation/WorldTime.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/jobs/handlers/NeedsJobSystem.cs] (C#, LoC: 311, I: 0.85)
  ← Входящие (2): src/simulation/AgentSimulationThread.cs, tests/TestWorkProgressPersistence.cs
  → Исходящие (11): src/simulation/agents/AgentDataPool.cs, src/simulation/agents/AgentDataPool.cs, src/simulation/items/GroundItemManager.cs, src/core/items/ItemRegistry.cs, src/simulation/jobs/JobDispatcher.cs, src/simulation/ParallelRng.cs, src/simulation/SimulationContext.cs, src/simulation/items/StockpileManager.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/jobs/handlers/PlantingJobHandler.cs] (C#, LoC: 227, I: 0.89)
  ← Входящие (2): src/simulation/AgentSimulationThread.cs, tests/TestAdversarialConstruction.cs
  → Исходящие (17): src/simulation/jobs/IJobHandler.cs, src/simulation/agents/AgentDataPool.cs, src/simulation/farming/CropGrowthManager.cs, src/simulation/farming/FarmJobManager.cs, src/simulation/farming/FarmZoneManagerFacade.cs, src/simulation/items/GroundItemManager.cs, src/simulation/jobs/IJobHandler.cs, src/simulation/jobs/JobBroker.cs, src/core/jobs/JobData.cs, src/simulation/jobs/JobDispatcher.cs, src/simulation/ParallelRng.cs, src/simulation/SimulationContext.cs, src/simulation/items/StockpileManager.cs, src/simulation/building/WorkProgressTracker.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/jobs/handlers/StockpileHaulingJobHandler.cs] (C#, LoC: 235, I: 0.93)
  ← Входящие (1): src/simulation/AgentSimulationThread.cs
  → Исходящие (13): src/simulation/jobs/IJobHandler.cs, src/simulation/agents/AgentDataPool.cs, src/simulation/structures/ConstructionManager.cs, src/simulation/items/GroundItemManager.cs, src/simulation/jobs/IJobHandler.cs, src/core/jobs/JobData.cs, src/simulation/jobs/JobDispatcher.cs, src/simulation/ParallelRng.cs, src/simulation/SimulationContext.cs, src/simulation/items/StockpileManager.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/jobs/handlers/TreeChoppingJobHandler.cs] (C#, LoC: 155, I: 0.85)
  ← Входящие (3): src/simulation/AgentSimulationThread.cs, tests/BenchJobAssignment.cs, tests/TestWorkProgressPersistence.cs
  → Исходящие (17): src/simulation/jobs/IJobHandler.cs, src/simulation/agents/AgentDataPool.cs, src/simulation/structures/ConstructionManager.cs, src/simulation/farming/FarmJobManager.cs, src/simulation/items/GroundItemManager.cs, src/simulation/jobs/IJobHandler.cs, src/simulation/jobs/JobBroker.cs, src/core/jobs/JobData.cs, src/simulation/jobs/JobDispatcher.cs, src/simulation/ParallelRng.cs, src/simulation/SimulationContext.cs, src/simulation/trees/TreeJobManager.cs, src/simulation/building/WorkProgressTracker.cs, src/simulation/WorldTime.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/jobs/handlers/WanderJobSystem.cs] (C#, LoC: 94, I: 0.88)
  ← Входящие (1): src/simulation/AgentSimulationThread.cs
  → Исходящие (7): src/simulation/agents/AgentDataPool.cs, src/simulation/jobs/JobDispatcher.cs, src/simulation/ParallelRng.cs, src/simulation/SimulationContext.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/offline/OfflineAgentNeeds.cs] (C#, LoC: 91, I: 0.83)
  ← Входящие (1): src/simulation/offline/OfflineWorldTicker.cs
  → Исходящие (5): src/simulation/agents/AgentDataPool.cs, src/simulation/offline/OfflineCatchUpPolicy.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/offline/OfflineCatchUpPolicy.cs] (C#, LoC: 30, I: 0.57)
  ← Входящие (3): src/simulation/offline/OfflineAgentNeeds.cs, src/simulation/offline/OfflineCropGrowth.cs, src/simulation/offline/OfflineWorldTicker.cs
  → Исходящие (4): src/simulation/WorldTime.cs, src/simulation/AgentSimulationThread.cs, src/simulation/CellProgressStore.cs, src/simulation/DynamicWorkBalancer.cs

[src/simulation/offline/OfflineCropGrowth.cs] (C#, LoC: 55, I: 0.5)
  ← Входящие (1): src/simulation/offline/OfflineWorldTicker.cs
  → Исходящие (1): src/simulation/offline/OfflineCatchUpPolicy.cs

[src/simulation/offline/OfflineWorldTicker.cs] (C#, LoC: 141, I: 1.0)
  → Исходящие (10): src/simulation/agents/AgentDataPool.cs, src/simulation/offline/OfflineAgentNeeds.cs, src/simulation/offline/OfflineCatchUpPolicy.cs, src/simulation/offline/OfflineCropGrowth.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs, src/simulation/AgentSimulationThread.cs, src/simulation/CellProgressStore.cs, src/simulation/DynamicWorkBalancer.cs

[src/simulation/pathfinding/HierarchicalPathfinder.cs] (C#, LoC: 1525, I: 0.38)
  ← Входящие (10): scenes/main/Main.cs, src/core/common/ProfilerLogService.cs, src/simulation/AgentSimulationThread.cs, src/simulation/agents/AgentMovementService.cs, src/simulation/agents/FlowFieldManager.cs, src/simulation/agents/FlowFieldManager.cs, src/simulation/structures/StructureManager.cs, src/ui/controllers/MainMenuController.cs, tests/BenchPathfinding.cs, tests/Phase1StressTestRunner.cs
  → Исходящие (6): src/simulation/pathfinding/NavGridState.cs, src/simulation/agents/FlowFieldManager.cs, src/simulation/SimulationContext.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/pathfinding/NavGridState.cs] (C#, LoC: 44, I: 0.0)
  ← Входящие (1): src/simulation/pathfinding/HierarchicalPathfinder.cs

[src/simulation/scheduling/AdaptivePartitioner.cs] (C#, LoC: 106, I: 0.0)
  ← Входящие (2): src/simulation/AgentSimulationThread.cs, src/simulation/DynamicWorkBalancer.cs

[src/simulation/scheduling/DynamicWorkScheduler.cs] (C#, LoC: 877, I: 0.76)
  ← Входящие (4): src/core/common/ProfilerLogService.cs, src/simulation/AgentSimulationThread.cs, src/simulation/DynamicWorkBalancer.cs, src/simulation/scheduling/LoadMonitor.cs
  → Исходящие (13): src/simulation/scheduling/IDynamicWorkScheduler.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/simulation/scheduling/IDynamicWorkScheduler.cs, src/simulation/scheduling/LoadMonitor.cs, src/simulation/ParallelRng.cs, src/simulation/scheduling/IDynamicWorkScheduler.cs, src/simulation/scheduling/WorkItem.cs, src/simulation/scheduling/WorkQuantum.cs, src/simulation/scheduling/WorkStealingQueue.cs, src/core/common/ProfilerLogService.cs, src/core/common/ResourceRef.cs, src/core/common/SimEvents.cs

[src/simulation/scheduling/IDynamicWorkScheduler.cs] (C#, LoC: 117, I: 0.14)
  ← Входящие (6): src/simulation/AgentSimulationThread.cs, src/simulation/DynamicWorkBalancer.cs, src/simulation/scheduling/DynamicWorkScheduler.cs, src/simulation/scheduling/DynamicWorkScheduler.cs, src/simulation/scheduling/DynamicWorkScheduler.cs, src/simulation/scheduling/LoadMonitor.cs
  → Исходящие (1): src/simulation/scheduling/WorkQuantum.cs

[src/simulation/scheduling/LoadMonitor.cs] (C#, LoC: 256, I: 0.4)
  ← Входящие (3): src/simulation/AgentSimulationThread.cs, src/simulation/DynamicWorkBalancer.cs, src/simulation/scheduling/DynamicWorkScheduler.cs
  → Исходящие (2): src/simulation/scheduling/DynamicWorkScheduler.cs, src/simulation/scheduling/IDynamicWorkScheduler.cs

[src/simulation/scheduling/WorkItem.cs] (C#, LoC: 57, I: 0.0)
  ← Входящие (2): src/simulation/AgentSimulationThread.cs, src/simulation/scheduling/DynamicWorkScheduler.cs

[src/simulation/scheduling/WorkQuantum.cs] (C#, LoC: 99, I: 0.0)
  ← Входящие (2): src/simulation/scheduling/DynamicWorkScheduler.cs, src/simulation/scheduling/IDynamicWorkScheduler.cs

[src/simulation/scheduling/WorkStealingQueue.cs] (C#, LoC: 240, I: 0.0)
  ← Входящие (1): src/simulation/scheduling/DynamicWorkScheduler.cs

[src/simulation/simd/SimdMovementBatch.cs] (C#, LoC: 280, I: 0.71)
  ← Входящие (2): src/simulation/AgentSimulationThread.cs, src/simulation/gpu/GpuAgentTransformSimulator.cs
  → Исходящие (5): src/simulation/agents/AgentDataPool.cs, src/simulation/agents/AgentDataPool.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/simd/SimdNeedsBatch.cs] (C#, LoC: 572, I: 0.71)
  ← Входящие (2): src/simulation/AgentSimulationThread.cs, src/simulation/gpu/GpuAgentTransformSimulator.cs
  → Исходящие (5): src/simulation/agents/AgentDataPool.cs, src/simulation/agents/AgentDataPool.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/stones/StoneJobManager.cs] (C#, LoC: 155, I: 0.17)
  ← Входящие (15): scenes/main/Main.cs, src/core/world/mutation/IslandDomainReplay.cs, src/core/world/mutation/IslandMutationMirror.cs, src/core/world/mutation/IslandStateReplay.cs, src/core/world/mutation/IslandStateSnapshot.cs, src/simulation/farming/FarmJobManager.cs, src/simulation/jobs/JobValidator.cs, src/simulation/jobs/handlers/MiningJobHandler.cs, src/simulation/structures/ConstructionManager.cs, src/ui/controllers/MainMenuController.cs, src/ui/renderers/DesignationRenderer.cs, src/ui/renderers/MapRenderer.cs, src/ui/tools/StoneMiningTool.cs, tests/TestAdversarialConstruction.cs, tests/TestWorkProgressPersistence.cs
  → Исходящие (3): src/simulation/CellProgressStore.cs, src/simulation/jobs/JobBroker.cs, src/simulation/SimulationContext.cs

[src/simulation/structures/ConstructionManager.cs] (C#, LoC: 651, I: 0.36)
  ← Входящие (30): scenes/main/Main.cs, src/core/world/mutation/IslandDomainReplay.cs, src/core/world/mutation/IslandMutationMirror.cs, src/core/world/mutation/IslandStateReplay.cs, src/core/world/mutation/IslandStateSnapshot.cs, src/simulation/AgentSimulationThread.cs, src/simulation/building/BlueprintManager.cs, src/simulation/building/BuildObstacles.cs, src/simulation/building/ConstructionPipeline.cs, src/simulation/building/TileClearanceService.cs, src/simulation/items/GroundItemManager.cs, src/simulation/items/StockpileManager.cs, src/simulation/jobs/JobBroker.cs, src/simulation/jobs/JobValidator.cs, src/simulation/jobs/handlers/BlueprintDeliveryJobHandler.cs, src/simulation/jobs/handlers/ConstructionJobHandler.cs, src/simulation/jobs/handlers/MiningJobHandler.cs, src/simulation/jobs/handlers/StockpileHaulingJobHandler.cs, src/simulation/jobs/handlers/TreeChoppingJobHandler.cs, src/simulation/structures/StructureManager.cs, src/ui/controllers/MainMenuController.cs, src/ui/renderers/MapRenderer.cs, src/ui/tools/BuildTool.cs, src/ui/tools/ZoneDraftTool.cs, tests/BenchJobAssignment.cs, tests/Phase2StressTestRunner.cs, tests/Phase3StressTestRunner.cs, tests/Phase4StressTestRunner.cs, tests/TestAdversarialConstruction.cs, tests/TestWorkProgressPersistence.cs
  → Исходящие (17): src/simulation/agents/AgentDataPool.cs, src/simulation/building/BuildConfig.cs, src/simulation/structures/ConstructionSite.cs, src/simulation/items/GroundItemManager.cs, src/simulation/jobs/JobBroker.cs, src/core/jobs/JobData.cs, src/simulation/jobs/JobDispatcher.cs, src/core/world/MapData.cs, src/simulation/SimulationContext.cs, src/simulation/items/StockpileManager.cs, src/simulation/stones/StoneJobManager.cs, src/simulation/structures/StructureDefinition.cs, src/simulation/structures/StructureManager.cs, src/simulation/trees/TreeJobManager.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/structures/ConstructionSite.cs] (C#, LoC: 105, I: 0.44)
  ← Входящие (5): src/core/world/mutation/IslandMutationMirror.cs, src/core/world/mutation/IslandStateReplay.cs, src/simulation/building/TileClearanceService.cs, src/simulation/structures/ConstructionManager.cs, tests/TestIslandMirror.cs
  → Исходящие (4): src/simulation/structures/StructureDefinition.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/structures/StructureCategory.cs] (C#, LoC: 11, I: 0.5)
  (изолированный файл)

[src/simulation/structures/StructureDefinition.cs] (C#, LoC: 66, I: 0.27)
  ← Входящие (8): src/core/world/mutation/IslandDomainReplay.cs, src/core/world/mutation/IslandMutationMirror.cs, src/simulation/structures/ConstructionManager.cs, src/simulation/structures/ConstructionSite.cs, src/simulation/structures/StructureInstance.cs, src/simulation/structures/StructureManager.cs, src/simulation/structures/StructureRegistry.cs, tests/TestIslandMirror.cs
  → Исходящие (3): src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/structures/StructureInstance.cs] (C#, LoC: 24, I: 0.57)
  ← Входящие (3): src/core/world/mutation/IslandMutationMirror.cs, src/simulation/structures/StructureManager.cs, tests/TestIslandMirror.cs
  → Исходящие (4): src/simulation/structures/StructureDefinition.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/structures/StructureManager.cs] (C#, LoC: 269, I: 0.34)
  ← Входящие (19): scenes/main/Main.cs, src/core/world/mutation/IslandDomainReplay.cs, src/core/world/mutation/IslandMutationMirror.cs, src/simulation/building/BuildingManager.cs, src/simulation/building/BuildObstacles.cs, src/simulation/building/TileClearanceService.cs, src/simulation/building/WallBuildManager.cs, src/simulation/items/StockpileManager.cs, src/simulation/jobs/handlers/ConstructionJobHandler.cs, src/simulation/structures/ConstructionManager.cs, src/simulation/zones/WorkZoneManagerFacade.cs, src/ui/controllers/MainMenuController.cs, src/ui/renderers/MapRenderer.cs, src/ui/tools/BuildTool.cs, src/ui/tools/FarmingTool.cs, src/ui/tools/ZoneDraftTool.cs, tests/TestAdversarialConstruction.cs, tests/TestIslandMirror.cs, tests/test_central_block_zone.cs
  → Исходящие (10): src/simulation/agents/AgentDataPool.cs, src/simulation/structures/ConstructionManager.cs, src/simulation/agents/FlowFieldManager.cs, src/simulation/pathfinding/HierarchicalPathfinder.cs, src/simulation/SimulationContext.cs, src/simulation/structures/StructureDefinition.cs, src/simulation/structures/StructureInstance.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/structures/StructureRegistry.cs] (C#, LoC: 127, I: 0.28)
  ← Входящие (13): src/core/world/mutation/IslandDomainReplay.cs, src/simulation/building/BlueprintManager.cs, src/simulation/building/BuildingManager.cs, src/simulation/building/ConstructionPipeline.cs, src/simulation/building/WallBuildManager.cs, src/simulation/jobs/handlers/ConstructionJobHandler.cs, src/ui/tools/BuildTool.cs, src/ui/tools/ZoneDraftTool.cs, tests/Phase3StressTestRunner.cs, tests/Phase4StressTestRunner.cs, tests/TestAdversarialConstruction.cs, tests/TestWorkProgressPersistence.cs, tests/test_central_block_zone.cs
  → Исходящие (5): src/simulation/building/BuildConfig.cs, src/simulation/structures/StructureDefinition.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/trees/TreeJobManager.cs] (C#, LoC: 143, I: 0.17)
  ← Входящие (15): scenes/main/Main.cs, src/core/world/mutation/IslandDomainReplay.cs, src/core/world/mutation/IslandMutationMirror.cs, src/core/world/mutation/IslandStateReplay.cs, src/core/world/mutation/IslandStateSnapshot.cs, src/simulation/farming/FarmJobManager.cs, src/simulation/jobs/JobValidator.cs, src/simulation/jobs/handlers/TreeChoppingJobHandler.cs, src/simulation/structures/ConstructionManager.cs, src/ui/controllers/MainMenuController.cs, src/ui/renderers/DesignationRenderer.cs, src/ui/renderers/MapRenderer.cs, src/ui/tools/TreeFellingTool.cs, tests/TestAdversarialConstruction.cs, tests/TestWorkProgressPersistence.cs
  → Исходящие (3): src/simulation/CellProgressStore.cs, src/simulation/jobs/JobBroker.cs, src/simulation/SimulationContext.cs

[src/simulation/zones/WorkZone.cs] (C#, LoC: 128, I: 0.3)
  ← Входящие (7): src/simulation/agents/WorkerAssignmentSystem.cs, src/simulation/zones/WorkZoneManagerFacade.cs, src/simulation/zones/Zone.cs, src/simulation/zones/ZoneManager.cs, src/ui/controllers/WorkZoneController.cs, tests/Phase2StressTestRunner.cs, tests/test_zone_model.cs
  → Исходящие (3): src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/zones/WorkZoneManagerFacade.cs] (C#, LoC: 186, I: 0.62)
  ← Входящие (6): src/ui/controllers/WorkZoneController.cs, tests/Phase2StressTestRunner.cs, tests/test_central_block_zone.cs, tests/test_zone_agents.cs, tests/test_zone_model.cs, tests/test_zone_router.cs
  → Исходящие (10): src/simulation/agents/AgentDataPool.cs, src/simulation/SimulationContext.cs, src/simulation/structures/StructureManager.cs, src/simulation/zones/WorkZone.cs, src/simulation/agents/WorkerAssignmentSystem.cs, src/simulation/zones/Zone.cs, src/simulation/zones/ZoneManager.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/zones/Zone.cs] (C#, LoC: 208, I: 0.31)
  ← Входящие (11): scenes/main/Main.cs, src/core/world/climate/LatitudeBands.cs, src/simulation/farming/FarmZoneManagerFacade.cs, src/simulation/zones/WorkZoneManagerFacade.cs, src/simulation/zones/ZoneCoverageMap.cs, src/simulation/zones/ZoneManager.cs, src/simulation/zones/ZonePersistence.cs, tests/BenchJobAssignment.cs, tests/Phase1StressTestRunner.cs, tests/Phase5StressTestRunner.cs, tests/TestIslandZonePersistence.cs
  → Исходящие (5): scenes/ui/Farm.tscn, src/simulation/zones/WorkZone.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/zones/ZoneCoverageMap.cs] (C#, LoC: 187, I: 0.83)
  ← Входящие (1): src/simulation/zones/ZoneJobRouter.cs
  → Исходящие (5): src/simulation/zones/Zone.cs, src/simulation/zones/ZoneManager.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/zones/ZoneJobRouter.cs] (C#, LoC: 411, I: 0.36)
  ← Входящие (9): src/simulation/AgentSimulationThread.cs, src/simulation/agents/WorkerZoneJobSystem.cs, src/simulation/jobs/JobBroker.cs, src/simulation/jobs/JobDispatcher.cs, src/simulation/jobs/JobSpatialPyramid.cs, src/simulation/zones/ZoneManager.cs, tests/test_central_block_zone.cs, tests/test_zone_agents.cs, tests/test_zone_router.cs
  → Исходящие (5): src/simulation/jobs/JobDispatcher.cs, src/simulation/zones/ZoneCoverageMap.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/zones/ZoneManager.cs] (C#, LoC: 817, I: 0.42)
  ← Входящие (21): scenes/main/Main.cs, src/simulation/AgentSimulationThread.cs, src/simulation/agents/WorkerAssignmentSystem.cs, src/simulation/agents/WorkerZoneJobSystem.cs, src/simulation/farming/FarmZoneManagerFacade.cs, src/simulation/zones/WorkZoneManagerFacade.cs, src/simulation/zones/ZoneCoverageMap.cs, src/simulation/zones/ZonePersistence.cs, src/ui/controllers/HUDController.cs, src/ui/controllers/MainMenuController.cs, src/ui/controllers/WorkZoneController.cs, src/ui/renderers/FarmZoneRenderer.cs, src/ui/tools/BuildTool.cs, src/ui/tools/SelectTool.cs, tests/BenchJobAssignment.cs, tests/Phase2StressTestRunner.cs, tests/TestIslandZonePersistence.cs, tests/test_central_block_zone.cs, tests/test_zone_agents.cs, tests/test_zone_model.cs, tests/test_zone_router.cs
  → Исходящие (15): src/simulation/agents/AgentDataPool.cs, src/simulation/farming/CropGrowthManager.cs, scenes/ui/Farm.tscn, src/simulation/farming/FarmJobManager.cs, src/simulation/jobs/JobBroker.cs, src/simulation/jobs/JobDispatcher.cs, src/simulation/jobs/JobPriorityManager.cs, src/simulation/jobs/JobRegistry.cs, src/simulation/SimulationContext.cs, src/simulation/zones/WorkZone.cs, src/simulation/zones/Zone.cs, src/simulation/zones/ZoneJobRouter.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/simulation/zones/ZonePersistence.cs] (C#, LoC: 192, I: 0.71)
  ← Входящие (2): scenes/main/Main.cs, tests/TestIslandZonePersistence.cs
  → Исходящие (5): src/simulation/zones/Zone.cs, src/simulation/zones/ZoneManager.cs, src/simulation/AgentSimulationThread.cs, src/simulation/CellProgressStore.cs, src/simulation/DynamicWorkBalancer.cs

[src/ui/CameraController.cs] (C#, LoC: 273, I: 0.08)
  ← Входящие (12): scenes/main/Main.cs, src/ui/PlayerInteractionManager.cs, src/ui/streaming/StreamingWorldView.cs, src/ui/tools/SelectTool.cs, tests/Phase4StressTestRunner.cs, tests/Phase5StressTestRunner.cs, tests/TerrainWindowBench.cs, tests/TestDestroyerCamera.cs, tests/TestRenderLodBenchmark.cs, tests/TestWorldGen.cs, tests/TestWorldGenSeamAudit.cs, tests/test_central_block_zone.cs
  → Исходящие (1): src/ui/renderers/MapRenderer.cs

[src/ui/DayNightCycle.cs] (C#, LoC: 203, I: 0.18)
  ← Входящие (18): scenes/main/Main.cs, src/ui/controllers/HUDController.cs, src/ui/renderers/AgentRenderer.cs, src/ui/renderers/AgentRenderer.cs, src/ui/renderers/ItemShadowRenderer.cs, src/ui/renderers/ItemShadowRenderer.cs, src/ui/renderers/ShadowCasterRenderer.cs, src/ui/renderers/ShadowCasterRenderer.cs, src/ui/streaming/StreamingWorldView.cs, src/ui/tools/SelectTool.cs, tests/Phase4StressTestRunner.cs, tests/Phase5StressTestRunner.cs, tests/TerrainWindowBench.cs, tests/TestDestroyerCamera.cs, tests/TestRenderLodBenchmark.cs, tests/TestRenderLodBenchmark.cs, tests/TestWorldGenSeamAudit.cs, tests/test_central_block_zone.cs
  → Исходящие (4): src/simulation/WorldTime.cs, src/simulation/AgentSimulationThread.cs, src/simulation/CellProgressStore.cs, src/simulation/DynamicWorkBalancer.cs

[src/ui/ICellProbeOverlay.cs] (C#, LoC: 16, I: 0.0)
  ← Входящие (13): scenes/main/Main.cs, src/ui/PlayerInteractionManager.cs, src/ui/controllers/HUDController.cs, src/ui/controllers/HUDController.cs, src/ui/streaming/StreamingWorldView.cs, src/ui/tools/SelectTool.cs, tests/Phase4StressTestRunner.cs, tests/Phase5StressTestRunner.cs, tests/TerrainWindowBench.cs, tests/TestDestroyerCamera.cs, tests/TestRenderLodBenchmark.cs, tests/TestWorldGenSeamAudit.cs, tests/test_central_block_zone.cs

[src/ui/PerformanceOverlay.cs] (C#, LoC: 945, I: 0.78)
  ← Входящие (4): scenes/main/Main.cs, src/ui/tools/SelectTool.cs, tests/TestDestroyerCamera.cs, tests/TestRenderLodBenchmark.cs
  → Исходящие (14): src/simulation/AgentSimulationThread.cs, src/core/common/ProfilerLogService.cs, src/core/common/GameProfiler.cs, src/simulation/items/GroundItemManager.cs, src/simulation/jobs/JobBroker.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs, src/simulation/TimeManager.cs, src/core/common/CpuTopology.cs, src/core/common/ResourceRef.cs, src/core/common/SimEvents.cs, src/simulation/CellProgressStore.cs, src/simulation/DynamicWorkBalancer.cs, src/simulation/GameSpeed.cs

[src/ui/PlayerInteractionManager.cs] (C#, LoC: 137, I: 0.79)
  ← Входящие (4): scenes/main/Main.cs, src/ui/controllers/BuildMenuController.cs, src/ui/controllers/HUDController.cs, tests/Phase4StressTestRunner.cs
  → Исходящие (15): src/ui/CameraController.cs, src/ui/ICellProbeOverlay.cs, src/ui/tools/ITool.cs, src/ui/tools/SelectTool.cs, src/ui/SelectionBox.cs, src/core/common/SimEvents.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs, src/simulation/AgentSimulationThread.cs, src/simulation/CellProgressStore.cs, src/simulation/DynamicWorkBalancer.cs, src/ui/tools/BuildTool.cs, src/ui/tools/FarmingTool.cs, src/ui/tools/IToolWorldPlacement.cs

[src/ui/SelectionBox.cs] (C#, LoC: 104, I: 0.0)
  ← Входящие (8): scenes/main/Main.cs, src/ui/PlayerInteractionManager.cs, src/ui/tools/FarmingTool.cs, src/ui/tools/SelectTool.cs, src/ui/tools/StoneMiningTool.cs, src/ui/tools/TreeFellingTool.cs, src/ui/tools/WarehouseTool.cs, src/ui/tools/ZoneDraftTool.cs

[src/ui/controllers/BuildMenuController.cs] (C#, LoC: 528, I: 0.88)
  ← Входящие (2): scenes/main/Main.cs, src/ui/controllers/HUDController.cs
  → Исходящие (15): scenes/ui/Build.tscn, src/ui/tools/BuildTool.cs, src/ui/controllers/HUDController.cs, src/ui/controllers/IDraftCoordinator.cs, src/core/world/MapData.cs, src/ui/renderers/MapRenderer.cs, src/ui/PlayerInteractionManager.cs, src/simulation/building/WallBuildManager.cs, src/ui/controllers/WallMaterialRegistry.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs, src/ui/tools/FarmingTool.cs, src/ui/tools/ITool.cs, src/ui/tools/IToolWorldPlacement.cs

[src/ui/controllers/FarmController.cs] (C#, LoC: 425, I: 0.86)
  ← Входящие (2): scenes/main/Main.cs, src/ui/controllers/HUDController.cs
  → Исходящие (12): src/ui/controllers/IZoneWindow.cs, src/simulation/farming/CropGrowthManager.cs, scenes/ui/Farm.tscn, src/simulation/farming/FarmZone.cs, src/simulation/farming/FarmZoneManagerFacade.cs, src/ui/controllers/IZoneWindow.cs, src/core/world/MapData.cs, src/ui/renderers/MapRenderer.cs, src/ui/controllers/ZoneWindowRegistry.cs, src/simulation/AgentSimulationThread.cs, src/simulation/CellProgressStore.cs, src/simulation/DynamicWorkBalancer.cs

[src/ui/controllers/HUDController.cs] (C#, LoC: 1501, I: 0.93)
  ← Входящие (3): scenes/main/Main.cs, src/ui/controllers/BuildMenuController.cs, tests/Phase4StressTestRunner.cs
  → Исходящие (40): src/ui/ICellProbeOverlay.cs, src/ui/controllers/IDraftCoordinator.cs, src/ui/controllers/BuildMenuController.cs, src/ui/tools/BuildTool.cs, src/core/economy/CityState.cs, src/ui/DayNightCycle.cs, src/ui/controllers/FarmController.cs, src/ui/ICellProbeOverlay.cs, src/ui/controllers/IDraftCoordinator.cs, src/ui/tools/IToolWorldPlacement.cs, src/simulation/jobs/JobDispatcher.cs, src/core/world/MapData.cs, src/ui/renderers/MapRenderer.cs, src/ui/PlayerInteractionManager.cs, scenes/ui/Prioritet.tscn, src/ui/controllers/PriorityBoardController.cs, src/core/common/SimEvents.cs, src/simulation/items/StockpileManager.cs, src/ui/tools/StoneMiningTool.cs, src/simulation/TimeManager.cs, src/ui/tools/TreeFellingTool.cs, src/core/world/generation/UnifiedWorldGenerator.cs, src/simulation/building/WallBuildManager.cs, src/ui/controllers/WorkZoneController.cs, src/core/world/streaming/integration/WorldCellQuery.cs, src/simulation/WorldTime.cs, src/ui/tools/ZoneDraftTool.cs, src/simulation/zones/ZoneManager.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs, src/core/world/BiomeMapper.cs, src/core/world/BiomeType.cs, src/core/world/activearea/ActiveArea.cs, src/simulation/AgentSimulationThread.cs, src/simulation/CellProgressStore.cs, src/simulation/DynamicWorkBalancer.cs, src/ui/tools/FarmingTool.cs, src/ui/tools/ITool.cs, src/ui/tools/SelectTool.cs

[src/ui/controllers/IDraftCoordinator.cs] (C#, LoC: 33, I: 0.0)
  ← Входящие (3): src/ui/controllers/BuildMenuController.cs, src/ui/controllers/HUDController.cs, src/ui/controllers/HUDController.cs

[src/ui/controllers/IZoneWindow.cs] (C#, LoC: 25, I: 0.0)
  ← Входящие (8): src/ui/controllers/FarmController.cs, src/ui/controllers/FarmController.cs, src/ui/controllers/WorkZoneController.cs, src/ui/controllers/WorkZoneController.cs, src/ui/controllers/WorkZoneController.cs, src/ui/controllers/WorkZoneController.cs, src/ui/controllers/ZoneWindowRegistry.cs, src/ui/controllers/ZoneWindowRegistry.cs

[src/ui/controllers/MainMenuController.cs] (C#, LoC: 94, I: 0.96)
  ← Входящие (1): tests/Phase4StressTestRunner.cs
  → Исходящие (23): src/simulation/building/BlueprintManager.cs, src/simulation/building/BuildingManager.cs, src/simulation/structures/ConstructionManager.cs, src/simulation/farming/CropGrowthManager.cs, src/simulation/farming/FarmJobManager.cs, src/simulation/agents/FlowFieldManager.cs, src/core/common/GameProfiler.cs, src/simulation/items/GroundItemManager.cs, src/simulation/pathfinding/HierarchicalPathfinder.cs, src/simulation/jobs/JobDispatcher.cs, src/simulation/jobs/JobPriorityManager.cs, scenes/main/Main.cs, scenes/main/Main.tscn, src/ui/renderers/MapRenderer.cs, src/core/common/ProfilerLogService.cs, src/core/common/SimEvents.cs, src/simulation/items/StockpileManager.cs, src/simulation/stones/StoneJobManager.cs, src/simulation/structures/StructureManager.cs, src/simulation/trees/TreeJobManager.cs, src/simulation/building/WorkProgressTracker.cs, src/core/world/climate/WorldClimateProvider.cs, src/simulation/zones/ZoneManager.cs

[src/ui/controllers/PriorityBoardController.cs] (C#, LoC: 136, I: 0.88)
  ← Входящие (1): src/ui/controllers/HUDController.cs
  → Исходящие (7): src/simulation/jobs/JobPriorityManager.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs, src/simulation/AgentSimulationThread.cs, src/simulation/CellProgressStore.cs, src/simulation/DynamicWorkBalancer.cs

[src/ui/controllers/WallMaterialRegistry.cs] (C#, LoC: 42, I: 0.6)
  ← Входящие (4): src/core/world/WallTileHelper.cs, src/simulation/building/WorkProgressTracker.cs, src/ui/controllers/BuildMenuController.cs, src/ui/tools/ZoneDraftTool.cs
  → Исходящие (6): src/core/world/WallTileHelper.cs, src/simulation/building/WorkProgressTracker.cs, src/ui/renderers/MapRenderer.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs

[src/ui/controllers/WorkZoneController.cs] (C#, LoC: 562, I: 0.95)
  ← Входящие (1): src/ui/controllers/HUDController.cs
  → Исходящие (19): src/ui/controllers/IZoneWindow.cs, src/ui/controllers/IZoneWindow.cs, src/simulation/farming/FarmZoneManagerFacade.cs, src/ui/controllers/IZoneWindow.cs, src/ui/controllers/IZoneWindow.cs, src/simulation/jobs/JobDispatcher.cs, scenes/ui/JobDisplay.tscn, src/simulation/jobs/JobPriorityManager.cs, scenes/ui/Prioritet.tscn, src/simulation/zones/WorkZone.cs, src/simulation/zones/WorkZoneManagerFacade.cs, src/simulation/zones/ZoneManager.cs, src/ui/controllers/ZoneWindowRegistry.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs, src/simulation/AgentSimulationThread.cs, src/simulation/CellProgressStore.cs, src/simulation/DynamicWorkBalancer.cs

[src/ui/controllers/ZoneWindowRegistry.cs] (C#, LoC: 18, I: 0.5)
  ← Входящие (2): src/ui/controllers/FarmController.cs, src/ui/controllers/WorkZoneController.cs
  → Исходящие (2): src/ui/controllers/IZoneWindow.cs, src/ui/controllers/IZoneWindow.cs

[src/ui/renderers/AgentLerpBatch.cs] (C#, LoC: 242, I: 0.0)
  ← Входящие (2): src/ui/renderers/AgentRenderer.cs, tests/TestRenderLodBenchmark.cs

[src/ui/renderers/AgentRenderer.cs] (C#, LoC: 629, I: 0.87)
  ← Входящие (2): scenes/main/Main.cs, tests/TestRenderLodBenchmark.cs
  → Исходящие (13): src/ui/renderers/AgentLerpBatch.cs, src/simulation/AgentSimulationThread.cs, src/ui/DayNightCycle.cs, src/core/common/GameProfiler.cs, src/ui/renderers/MapRenderer.cs, src/core/common/SimEvents.cs, src/ui/DayNightCycle.cs, src/core/common/CpuTopology.cs, src/core/common/ProfilerLogService.cs, src/core/common/ResourceRef.cs, src/simulation/CellProgressStore.cs, src/simulation/DynamicWorkBalancer.cs, src/simulation/GameSpeed.cs

[src/ui/renderers/CropRenderer.cs] (C#, LoC: 145, I: 0.91)
  ← Входящие (1): scenes/main/Main.cs
  → Исходящие (10): src/simulation/farming/CropGrowthManager.cs, src/ui/renderers/ItemShadowRenderer.cs, src/ui/renderers/MapRenderer.cs, src/core/common/ResourceRef.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs, src/simulation/AgentSimulationThread.cs, src/simulation/CellProgressStore.cs, src/simulation/DynamicWorkBalancer.cs

[src/ui/renderers/DesignationRenderer.cs] (C#, LoC: 200, I: 0.86)
  ← Входящие (1): src/ui/renderers/MapRenderer.cs
  → Исходящие (6): src/ui/renderers/MapRenderer.cs, src/simulation/stones/StoneJobManager.cs, src/simulation/trees/TreeJobManager.cs, src/simulation/AgentSimulationThread.cs, src/simulation/CellProgressStore.cs, src/simulation/DynamicWorkBalancer.cs

[src/ui/renderers/FarmZoneRenderer.cs] (C#, LoC: 131, I: 0.8)
  ← Входящие (1): scenes/main/Main.cs
  → Исходящие (4): src/simulation/zones/ZoneManager.cs, src/simulation/AgentSimulationThread.cs, src/simulation/CellProgressStore.cs, src/simulation/DynamicWorkBalancer.cs

[src/ui/renderers/FertilityOverlayRenderer.cs] (C#, LoC: 155, I: 0.93)
  ← Входящие (1): src/ui/renderers/MapRenderer.cs
  → Исходящие (13): src/core/world/FertilityMap.cs, src/ui/renderers/MapRenderer.cs, src/core/world/generation/UnifiedWorldGenerator.cs, src/core/world/streaming/integration/WorldCellQuery.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs, src/core/world/streaming/integration/ChunkMutations.cs, src/core/world/streaming/integration/IBlockWorld.cs, src/core/world/streaming/integration/IWallWorld.cs, src/core/world/BiomeMapper.cs, src/core/world/BiomeType.cs, src/core/world/activearea/ActiveArea.cs

[src/ui/renderers/GroundItemRenderer.cs] (C#, LoC: 133, I: 0.92)
  ← Входящие (1): scenes/main/Main.cs
  → Исходящие (11): src/simulation/items/GroundItemManager.cs, src/core/items/ItemRegistry.cs, src/ui/renderers/ItemShadowRenderer.cs, src/ui/renderers/MapRenderer.cs, src/core/common/SimEvents.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs, src/simulation/AgentSimulationThread.cs, src/simulation/CellProgressStore.cs, src/simulation/DynamicWorkBalancer.cs

[src/ui/renderers/HumidityOverlayRenderer.cs] (C#, LoC: 146, I: 0.93)
  ← Входящие (1): src/ui/renderers/MapRenderer.cs
  → Исходящие (13): src/core/world/HumidityMap.cs, src/ui/renderers/MapRenderer.cs, src/core/world/generation/UnifiedWorldGenerator.cs, src/core/world/streaming/integration/WorldCellQuery.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs, src/core/world/streaming/integration/ChunkMutations.cs, src/core/world/streaming/integration/IBlockWorld.cs, src/core/world/streaming/integration/IWallWorld.cs, src/core/world/BiomeMapper.cs, src/core/world/BiomeType.cs, src/core/world/activearea/ActiveArea.cs

[src/ui/renderers/ItemShadowRenderer.cs] (C#, LoC: 255, I: 0.38)
  ← Входящие (5): scenes/main/Main.cs, src/ui/renderers/CropRenderer.cs, src/ui/renderers/GroundItemRenderer.cs, src/ui/renderers/StockpileItemRenderer.cs, tests/TestRenderLodBenchmark.cs
  → Исходящие (3): src/ui/DayNightCycle.cs, src/ui/renderers/MapRenderer.cs, src/ui/DayNightCycle.cs

[src/ui/renderers/MapRenderer.cs] (C#, LoC: 1813, I: 0.6)
  ← Входящие (25): scenes/main/Main.cs, src/ui/CameraController.cs, src/ui/controllers/BuildMenuController.cs, src/ui/controllers/FarmController.cs, src/ui/controllers/HUDController.cs, src/ui/controllers/MainMenuController.cs, src/ui/controllers/WallMaterialRegistry.cs, src/ui/renderers/AgentRenderer.cs, src/ui/renderers/CropRenderer.cs, src/ui/renderers/DesignationRenderer.cs, src/ui/renderers/FertilityOverlayRenderer.cs, src/ui/renderers/GroundItemRenderer.cs, src/ui/renderers/HumidityOverlayRenderer.cs, src/ui/renderers/ItemShadowRenderer.cs, src/ui/renderers/ShadowCasterRenderer.cs, src/ui/renderers/StockpileItemRenderer.cs, src/ui/streaming/StreamingToolPlacement.cs, src/ui/streaming/StreamingWorldView.cs, src/ui/tools/BuildTool.cs, src/ui/tools/WorldPlacementPass.cs, src/ui/tools/ZoneDraftTool.cs, tests/Phase5StressTestRunner.cs, tests/TerrainWindowBench.cs, tests/TestWorldGenSeamAudit.cs, tests/test_central_block_zone.cs
  → Исходящие (37): src/simulation/building/BlueprintManager.cs, src/simulation/building/BuildingManager.cs, src/simulation/structures/ConstructionManager.cs, src/ui/renderers/DesignationRenderer.cs, src/simulation/farming/FarmJobManager.cs, src/core/world/FertilityMap.cs, src/ui/renderers/FertilityOverlayRenderer.cs, src/core/world/HumidityMap.cs, src/ui/renderers/HumidityOverlayRenderer.cs, src/core/world/MapData.cs, src/ui/renderers/ShadowCasterRenderer.cs, src/ui/renderers/StockpileItemRenderer.cs, src/simulation/items/StockpileManager.cs, src/simulation/stones/StoneJobManager.cs, src/simulation/structures/StructureManager.cs, src/ui/renderers/TerrainTileLayer.cs, src/simulation/TimeManager.cs, src/simulation/trees/TreeJobManager.cs, src/core/world/generation/UnifiedWorldGenerator.cs, src/simulation/building/WallBuildManager.cs, src/core/world/WallTileHelper.cs, src/simulation/building/WorkProgressTracker.cs, src/core/world/streaming/generation/WorldClimateGenerator.cs, src/core/world/streaming/generation/WorldCoordinates.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs, src/core/world/BiomeMapper.cs, src/core/world/BiomeType.cs, src/core/world/activearea/ActiveArea.cs, src/core/world/streaming/integration/ChunkMutations.cs, src/core/world/streaming/integration/IBlockWorld.cs, src/core/world/streaming/integration/IWallWorld.cs, src/simulation/AgentSimulationThread.cs, src/simulation/CellProgressStore.cs, src/simulation/DynamicWorkBalancer.cs, src/core/world/streaming/generation/WorldClimateParams.cs

[src/ui/renderers/ShadowCasterRenderer.cs] (C#, LoC: 289, I: 0.83)
  ← Входящие (2): src/ui/renderers/MapRenderer.cs, tests/TestRenderLodBenchmark.cs
  → Исходящие (10): src/ui/DayNightCycle.cs, src/core/world/MapData.cs, src/ui/renderers/MapRenderer.cs, src/ui/DayNightCycle.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs, src/simulation/AgentSimulationThread.cs, src/simulation/CellProgressStore.cs, src/simulation/DynamicWorkBalancer.cs

[src/ui/renderers/StockpileItemRenderer.cs] (C#, LoC: 131, I: 0.91)
  ← Входящие (1): src/ui/renderers/MapRenderer.cs
  → Исходящие (10): src/core/items/ItemRegistry.cs, src/ui/renderers/ItemShadowRenderer.cs, src/ui/renderers/MapRenderer.cs, src/simulation/items/StockpileManager.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs, src/simulation/AgentSimulationThread.cs, src/simulation/CellProgressStore.cs, src/simulation/DynamicWorkBalancer.cs

[src/ui/renderers/TerrainTileLayer.cs] (C#, LoC: 93, I: 0.0)
  ← Входящие (2): src/ui/renderers/MapRenderer.cs, src/ui/streaming/StreamingWorldView.cs

[src/ui/streaming/StreamingToolPlacement.cs] (C#, LoC: 120, I: 0.67)
  ← Входящие (6): scenes/main/Main.cs, tests/Phase3StressTestRunner.cs, tests/TestDestroyerStreaming.cs, tests/TestWorldGenV2.cs, tests/TestWorldStoreExit.cs, tests/test_streamingview_decor_seam.cs
  → Исходящие (12): src/ui/tools/IToolWorldPlacement.cs, src/ui/tools/IToolWorldPlacement.cs, src/ui/renderers/MapRenderer.cs, src/core/world/streaming/integration/StreamingWorldManager.cs, src/core/world/streaming/integration/WorldDeltaKind.cs, src/core/world/streaming/integration/WorldGameRules.cs, src/core/world/streaming/integration/ChunkMutations.cs, src/core/world/streaming/integration/IBlockWorld.cs, src/core/world/streaming/integration/IWallWorld.cs, src/ui/tools/BuildTool.cs, src/ui/tools/FarmingTool.cs, src/ui/tools/ITool.cs

[src/ui/streaming/StreamingWorldView.cs] (C#, LoC: 1983, I: 0.83)
  ← Входящие (7): scenes/main/Main.cs, src/ui/streaming/WorldJobBoard.cs, tests/Phase3StressTestRunner.cs, tests/TestDestroyerStreaming.cs, tests/TestWorldGenV2.cs, tests/TestWorldStoreExit.cs, tests/test_streamingview_decor_seam.cs
  → Исходящие (34): src/core/world/streaming/integration/IWallWorld.cs, src/core/world/streaming/integration/IBlockWorld.cs, src/core/world/streaming/integration/IWorldCellSource.cs, src/core/world/streaming/runtime/ChunkWindowPlanner.cs, src/core/world/streaming/runtime/ChunkWindowPlanner.cs, src/core/world/streaming/integration/IBlockWorld.cs, src/core/world/streaming/integration/IWallWorld.cs, src/core/world/streaming/integration/IWorldCellSource.cs, src/ui/renderers/MapRenderer.cs, src/ui/renderers/TerrainTileLayer.cs, src/simulation/TimeManager.cs, src/core/world/generation/UnifiedWorldGenerator.cs, src/core/world/streaming/generation/WorldChunk.cs, src/core/world/streaming/storage/WorldChunkCodec.cs, src/core/world/streaming/storage/WorldChunkDeltaCodec.cs, src/core/world/streaming/storage/WorldChunkDeltaCodec.cs, src/core/world/streaming/storage/WorldChunkDeltaCodec.cs, src/core/world/streaming/storage/WorldChunkStore.cs, src/core/world/streaming/runtime/WorldChunkStreamer.cs, src/core/world/streaming/generation/WorldClimateGenerator.cs, src/core/world/streaming/generation/WorldCoordinates.cs, src/core/world/streaming/integration/WorldDecor.cs, src/core/world/streaming/integration/WorldDeltaKind.cs, src/core/world/BiomeMapper.cs, src/core/world/BiomeType.cs, src/core/world/activearea/ActiveArea.cs, src/core/world/streaming/integration/ChunkMutations.cs, src/core/world/streaming/integration/StreamingWorldManager.cs, src/core/world/streaming/integration/WorldCellQuery.cs, src/ui/CameraController.cs, src/ui/DayNightCycle.cs, src/ui/ICellProbeOverlay.cs, src/core/world/streaming/integration/WorldPlan.cs, src/core/world/streaming/storage/WorldSaveMetadata.cs

[src/ui/streaming/WorldJobBoard.cs] (C#, LoC: 107, I: 0.6)
  ← Входящие (6): scenes/main/Main.cs, tests/Phase3StressTestRunner.cs, tests/TestDestroyerStreaming.cs, tests/TestWorldGenV2.cs, tests/TestWorldStoreExit.cs, tests/test_streamingview_decor_seam.cs
  → Исходящие (9): src/core/world/streaming/integration/StreamingWorldManager.cs, src/ui/streaming/StreamingWorldView.cs, src/core/world/streaming/generation/WorldCoordinates.cs, src/core/world/BiomeMapper.cs, src/core/world/BiomeType.cs, src/core/world/activearea/ActiveArea.cs, src/core/world/streaming/integration/ChunkMutations.cs, src/core/world/streaming/integration/IBlockWorld.cs, src/core/world/streaming/integration/IWallWorld.cs

[src/ui/streaming/WorldMapOverlay.cs] (C#, LoC: 899, I: 0.6)
  ← Входящие (6): scenes/main/Main.cs, tests/TestDestroyerStreaming.cs, tests/TestWorldGen.cs, tests/TestWorldGenV2.cs, tests/TestWorldStoreExit.cs, tests/test_streamingview_decor_seam.cs
  → Исходящие (9): src/core/world/BiomeMapper.cs, src/core/world/climate/PlanetaryHydrology.cs, src/core/world/streaming/generation/WorldClimateGenerator.cs, src/core/world/climate/WorldClimateProvider.cs, src/core/world/streaming/WorldMapTileMapper.cs, src/core/world/BiomeType.cs, src/core/world/activearea/ActiveArea.cs, src/core/world/activearea/ActiveAreaRegistry.cs, src/core/world/streaming/generation/RegionKey.cs

[src/ui/tools/BuildTool.cs] (C#, LoC: 291, I: 0.8)
  ← Входящие (5): src/ui/PlayerInteractionManager.cs, src/ui/controllers/BuildMenuController.cs, src/ui/controllers/HUDController.cs, src/ui/streaming/StreamingToolPlacement.cs, src/ui/tools/ZoneDraftTool.cs
  → Исходящие (20): src/ui/tools/ITool.cs, src/simulation/building/BlueprintManager.cs, src/simulation/building/BuildingManager.cs, src/simulation/structures/ConstructionManager.cs, scenes/ui/Farm.tscn, src/simulation/farming/FarmJobManager.cs, src/ui/tools/ITool.cs, src/ui/tools/IToolWorldPlacement.cs, src/core/world/MapData.cs, src/ui/renderers/MapRenderer.cs, src/simulation/structures/StructureManager.cs, src/simulation/structures/StructureRegistry.cs, src/simulation/building/WallBuildManager.cs, src/simulation/zones/ZoneManager.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs, src/simulation/AgentSimulationThread.cs, src/simulation/CellProgressStore.cs, src/simulation/DynamicWorkBalancer.cs

[src/ui/tools/FarmingTool.cs] (C#, LoC: 173, I: 0.78)
  ← Входящие (5): src/ui/PlayerInteractionManager.cs, src/ui/controllers/BuildMenuController.cs, src/ui/controllers/HUDController.cs, src/ui/streaming/StreamingToolPlacement.cs, src/ui/tools/ZoneDraftTool.cs
  → Исходящие (18): src/ui/tools/ITool.cs, src/simulation/building/BlueprintManager.cs, src/simulation/building/BuildingManager.cs, src/simulation/farming/FarmJobManager.cs, src/simulation/farming/FarmZoneManagerFacade.cs, src/ui/tools/ITool.cs, src/ui/tools/IToolWorldPlacement.cs, src/core/world/MapData.cs, src/ui/SelectionBox.cs, src/simulation/structures/StructureManager.cs, src/simulation/building/WallBuildManager.cs, src/ui/tools/WorldPlacementPass.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs, src/simulation/AgentSimulationThread.cs, src/simulation/CellProgressStore.cs, src/simulation/DynamicWorkBalancer.cs

[src/ui/tools/ITool.cs] (C#, LoC: 12, I: 0.0)
  ← Входящие (18): src/ui/PlayerInteractionManager.cs, src/ui/controllers/BuildMenuController.cs, src/ui/controllers/HUDController.cs, src/ui/streaming/StreamingToolPlacement.cs, src/ui/tools/BuildTool.cs, src/ui/tools/BuildTool.cs, src/ui/tools/FarmingTool.cs, src/ui/tools/FarmingTool.cs, src/ui/tools/SelectTool.cs, src/ui/tools/SelectTool.cs, src/ui/tools/StoneMiningTool.cs, src/ui/tools/StoneMiningTool.cs, src/ui/tools/TreeFellingTool.cs, src/ui/tools/TreeFellingTool.cs, src/ui/tools/WarehouseTool.cs, src/ui/tools/WarehouseTool.cs, src/ui/tools/ZoneDraftTool.cs, src/ui/tools/ZoneDraftTool.cs

[src/ui/tools/IToolWorldPlacement.cs] (C#, LoC: 53, I: 0.0)
  ← Входящие (12): src/ui/PlayerInteractionManager.cs, src/ui/controllers/BuildMenuController.cs, src/ui/controllers/HUDController.cs, src/ui/streaming/StreamingToolPlacement.cs, src/ui/streaming/StreamingToolPlacement.cs, src/ui/tools/BuildTool.cs, src/ui/tools/FarmingTool.cs, src/ui/tools/StoneMiningTool.cs, src/ui/tools/TreeFellingTool.cs, src/ui/tools/WarehouseTool.cs, src/ui/tools/WorldPlacementPass.cs, src/ui/tools/ZoneDraftTool.cs

[src/ui/tools/SelectTool.cs] (C#, LoC: 98, I: 0.8)
  ← Входящие (3): src/ui/PlayerInteractionManager.cs, src/ui/controllers/HUDController.cs, src/ui/tools/ZoneDraftTool.cs
  → Исходящие (12): src/ui/tools/ITool.cs, src/simulation/farming/FarmZoneManagerFacade.cs, src/ui/ICellProbeOverlay.cs, src/ui/tools/ITool.cs, src/ui/SelectionBox.cs, src/simulation/zones/ZoneManager.cs, src/simulation/AgentSimulationThread.cs, src/simulation/CellProgressStore.cs, src/simulation/DynamicWorkBalancer.cs, src/ui/CameraController.cs, src/ui/DayNightCycle.cs, src/ui/PerformanceOverlay.cs

[src/ui/tools/StoneMiningTool.cs] (C#, LoC: 122, I: 0.93)
  ← Входящие (1): src/ui/controllers/HUDController.cs
  → Исходящие (13): src/ui/tools/ITool.cs, src/ui/tools/ITool.cs, src/ui/tools/IToolWorldPlacement.cs, src/core/world/MapData.cs, src/ui/SelectionBox.cs, src/simulation/stones/StoneJobManager.cs, src/ui/tools/WorldPlacementPass.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs, src/simulation/AgentSimulationThread.cs, src/simulation/CellProgressStore.cs, src/simulation/DynamicWorkBalancer.cs

[src/ui/tools/TreeFellingTool.cs] (C#, LoC: 120, I: 0.93)
  ← Входящие (1): src/ui/controllers/HUDController.cs
  → Исходящие (13): src/ui/tools/ITool.cs, src/ui/tools/ITool.cs, src/ui/tools/IToolWorldPlacement.cs, src/core/world/MapData.cs, src/ui/SelectionBox.cs, src/simulation/trees/TreeJobManager.cs, src/ui/tools/WorldPlacementPass.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs, src/simulation/AgentSimulationThread.cs, src/simulation/CellProgressStore.cs, src/simulation/DynamicWorkBalancer.cs

[src/ui/tools/WarehouseTool.cs] (C#, LoC: 94, I: 1.0)
  → Исходящие (9): src/ui/tools/ITool.cs, src/ui/tools/ITool.cs, src/ui/tools/IToolWorldPlacement.cs, src/ui/SelectionBox.cs, src/simulation/items/StockpileManager.cs, src/ui/tools/WorldPlacementPass.cs, src/simulation/AgentSimulationThread.cs, src/simulation/CellProgressStore.cs, src/simulation/DynamicWorkBalancer.cs

[src/ui/tools/WorldPlacementPass.cs] (C#, LoC: 87, I: 0.25)
  ← Входящие (6): src/ui/tools/FarmingTool.cs, src/ui/tools/StoneMiningTool.cs, src/ui/tools/TreeFellingTool.cs, src/ui/tools/WarehouseTool.cs, src/ui/tools/ZoneDraftTool.cs, tests/TestAdversarialConstruction.cs
  → Исходящие (2): src/ui/tools/IToolWorldPlacement.cs, src/ui/renderers/MapRenderer.cs

[src/ui/tools/ZoneDraftTool.cs] (C#, LoC: 820, I: 0.96)
  ← Входящие (1): src/ui/controllers/HUDController.cs
  → Исходящие (26): src/ui/tools/ITool.cs, src/simulation/building/BlueprintManager.cs, src/simulation/building/BuildingManager.cs, src/simulation/structures/ConstructionManager.cs, src/simulation/farming/FarmJobManager.cs, src/simulation/farming/FarmZoneManagerFacade.cs, src/ui/tools/ITool.cs, src/ui/tools/IToolWorldPlacement.cs, src/core/world/MapData.cs, src/ui/renderers/MapRenderer.cs, src/ui/SelectionBox.cs, src/simulation/items/StockpileManager.cs, src/simulation/structures/StructureManager.cs, src/simulation/structures/StructureRegistry.cs, src/simulation/building/WallBuildManager.cs, src/ui/controllers/WallMaterialRegistry.cs, src/ui/tools/WorldPlacementPass.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs, src/simulation/AgentSimulationThread.cs, src/simulation/CellProgressStore.cs, src/simulation/DynamicWorkBalancer.cs, src/ui/tools/BuildTool.cs, src/ui/tools/FarmingTool.cs, src/ui/tools/SelectTool.cs

[tests/BenchJobAssignment.cs] (C#, LoC: 613, I: 0.96)
  ← Входящие (1): tests/run_job_assignment_bench.gd
  → Исходящие (26): src/simulation/jobs/IJobHandler.cs, src/simulation/agents/AgentDataPool.cs, src/simulation/agents/AgentMovementService.cs, src/simulation/agents/AgentSpatialGrid.cs, src/simulation/structures/ConstructionManager.cs, src/simulation/items/GroundItemManager.cs, src/simulation/jobs/IJobHandler.cs, src/simulation/jobs/JobBroker.cs, src/core/jobs/JobData.cs, src/simulation/jobs/JobDispatcher.cs, src/simulation/jobs/JobRegistry.cs, src/simulation/jobs/JobSpatialPyramid.cs, src/simulation/SimulationContext.cs, src/simulation/items/StockpileManager.cs, src/simulation/jobs/handlers/TreeChoppingJobHandler.cs, src/simulation/zones/Zone.cs, src/simulation/zones/ZoneManager.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs, src/simulation/AgentSimulationThread.cs, src/simulation/CellProgressStore.cs, src/simulation/DynamicWorkBalancer.cs, src/simulation/jobs/handlers/BlueprintDeliveryJobHandler.cs, src/simulation/jobs/handlers/ConstructionJobHandler.cs, src/simulation/jobs/handlers/FarmingJobHandler.cs

[tests/BenchPathfinding.cs] (C#, LoC: 327, I: 0.8)
  ← Входящие (1): tests/run_pathfinding_bench.gd
  → Исходящие (4): src/simulation/pathfinding/HierarchicalPathfinder.cs, src/simulation/AgentSimulationThread.cs, src/simulation/CellProgressStore.cs, src/simulation/DynamicWorkBalancer.cs

[tests/Phase1StressTestRunner.cs] (C#, LoC: 135, I: 0.9)
  ← Входящие (1): tests/test_phase1.gd
  → Исходящие (9): src/simulation/AgentSimulationThread.cs, scenes/ui/Farm.tscn, src/simulation/agents/FlatIdleWorkerPool.cs, src/simulation/agents/FlowFieldManager.cs, src/simulation/pathfinding/HierarchicalPathfinder.cs, src/simulation/zones/Zone.cs, src/simulation/CellProgressStore.cs, src/simulation/DynamicWorkBalancer.cs, src/simulation/GameSpeed.cs

[tests/Phase2StressTestRunner.cs] (C#, LoC: 124, I: 0.96)
  ← Входящие (1): tests/test_phase2.gd
  → Исходящие (23): src/simulation/structures/ConstructionManager.cs, src/simulation/farming/FarmZone.cs, src/simulation/farming/FarmZoneManagerFacade.cs, src/simulation/jobs/JobDispatcher.cs, src/simulation/jobs/JobSpatialPyramid.cs, src/simulation/zones/WorkZone.cs, src/simulation/zones/WorkZoneManagerFacade.cs, src/core/world/streaming/generation/WorldClimateGenerator.cs, src/core/world/climate/WorldClimateProvider.cs, src/simulation/zones/ZoneManager.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs, src/core/world/climate/ContinentShaper.cs, src/core/world/climate/LatitudeBands.cs, src/core/world/climate/PlanetaryHydrology.cs, src/core/world/BiomeMapper.cs, src/core/world/BiomeType.cs, src/core/world/activearea/ActiveArea.cs, src/simulation/AgentSimulationThread.cs, src/simulation/CellProgressStore.cs, src/simulation/DynamicWorkBalancer.cs, src/core/world/streaming/generation/RegionKey.cs

[tests/Phase3StressTestRunner.cs] (C#, LoC: 113, I: 0.83)
  ← Входящие (3): tests/diag_csharp.gd, tests/run_phase3_suite.gd, tests/test_phase3.gd
  → Исходящие (15): src/simulation/AgentSimulationThread.cs, src/simulation/structures/ConstructionManager.cs, src/simulation/items/GroundItemManager.cs, src/core/world/MapData.cs, src/simulation/structures/StructureRegistry.cs, src/simulation/TimeManager.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs, src/simulation/CellProgressStore.cs, src/simulation/DynamicWorkBalancer.cs, src/simulation/GameSpeed.cs, src/ui/streaming/StreamingToolPlacement.cs, src/ui/streaming/StreamingWorldView.cs, src/ui/streaming/WorldJobBoard.cs

[tests/Phase4StressTestRunner.cs] (C#, LoC: 200, I: 0.93)
  ← Входящие (2): tests/run_phase4_suite.gd, tests/test_phase4.gd
  → Исходящие (28): src/simulation/agents/AgentDataPool.cs, src/simulation/agents/AgentMovementService.cs, src/simulation/agents/AgentSpatialGrid.cs, src/simulation/jobs/handlers/BlueprintDeliveryJobHandler.cs, src/simulation/structures/ConstructionManager.cs, src/simulation/items/GroundItemManager.cs, src/ui/controllers/HUDController.cs, src/core/jobs/JobData.cs, scenes/main/Main.cs, scenes/main/Main.tscn, src/core/world/MapData.cs, src/ui/PlayerInteractionManager.cs, src/simulation/SimulationContext.cs, src/simulation/items/StockpileManager.cs, src/simulation/structures/StructureRegistry.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs, src/simulation/AgentSimulationThread.cs, src/simulation/CellProgressStore.cs, src/simulation/DynamicWorkBalancer.cs, src/simulation/jobs/handlers/ConstructionJobHandler.cs, src/simulation/jobs/handlers/FarmingJobHandler.cs, src/simulation/jobs/handlers/HarvestJobHandler.cs, src/ui/CameraController.cs, src/ui/DayNightCycle.cs, src/ui/ICellProbeOverlay.cs, src/ui/controllers/MainMenuController.cs

[tests/Phase5StressTestRunner.cs] (C#, LoC: 367, I: 0.95)
  ← Входящие (2): tests/run_phase5_suite.gd, tests/test_phase5.gd
  → Исходящие (36): src/core/world/streaming/integration/IBlockWorld.cs, src/core/world/streaming/integration/IWorldCellSource.cs, src/simulation/agents/AgentMovementService.cs, src/simulation/agents/AgentSpatialGrid.cs, scenes/ui/Farm.tscn, src/core/world/streaming/integration/IBlockWorld.cs, src/core/world/streaming/integration/IWorldCellSource.cs, src/core/jobs/JobData.cs, src/simulation/jobs/JobSpatialPyramid.cs, src/ui/renderers/MapRenderer.cs, src/simulation/SimulationContext.cs, src/core/world/streaming/integration/StreamingWorldManager.cs, src/simulation/building/TileClearanceService.cs, src/core/world/streaming/integration/WorldDeltaKind.cs, src/core/world/streaming/integration/WorldGameRules.cs, src/core/world/streaming/coordinator/WorldSaveCoordinator.cs, src/simulation/zones/Zone.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs, src/core/world/BiomeMapper.cs, src/core/world/BiomeType.cs, src/core/world/activearea/ActiveArea.cs, src/core/world/streaming/integration/ChunkMutations.cs, src/core/world/streaming/integration/IWallWorld.cs, src/core/world/streaming/integration/WorldCellQuery.cs, src/simulation/AgentSimulationThread.cs, src/simulation/CellProgressStore.cs, src/simulation/DynamicWorkBalancer.cs, src/simulation/jobs/handlers/BlueprintDeliveryJobHandler.cs, src/simulation/jobs/handlers/ConstructionJobHandler.cs, src/simulation/jobs/handlers/FarmingJobHandler.cs, src/ui/CameraController.cs, src/ui/DayNightCycle.cs, src/ui/ICellProbeOverlay.cs, src/core/world/streaming/generation/GeneratedCell.cs

[tests/TerrainWindowBench.cs] (C#, LoC: 240, I: 0.94)
  ← Входящие (1): tests/run_terrain_window_bench.gd
  → Исходящие (16): src/ui/renderers/MapRenderer.cs, src/core/world/generation/UnifiedWorldGenerator.cs, src/core/world/streaming/integration/WorldCellQuery.cs, src/core/world/streaming/generation/WorldRegions.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs, src/core/world/BiomeMapper.cs, src/core/world/BiomeType.cs, src/core/world/activearea/ActiveArea.cs, src/core/world/streaming/integration/ChunkMutations.cs, src/core/world/streaming/integration/IBlockWorld.cs, src/core/world/streaming/integration/IWallWorld.cs, src/ui/CameraController.cs, src/ui/DayNightCycle.cs, src/ui/ICellProbeOverlay.cs

[tests/TestActiveArea.cs] (C#, LoC: 407, I: 0.93)
  ← Входящие (1): tests/run_activearea_test.gd
  → Исходящие (14): src/core/world/activearea/ActiveArea.cs, src/core/world/activearea/ActiveAreaRegistry.cs, src/core/world/generation/WorldGenV2.cs, src/core/world/MapData.cs, src/core/world/activearea/MapDataShim.cs, src/core/world/generation/UnifiedWorldGenerator.cs, src/core/world/generation/WorldGenV2.cs, src/core/world/streaming/generation/WorldRegions.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs, src/core/world/BiomeMapper.cs, src/core/world/BiomeType.cs, src/core/world/streaming/WorldMapTileMapper.cs

[tests/TestAdversarialConstruction.cs] (C#, LoC: 474, I: 0.96)
  ← Входящие (1): tests/run_adversarial_construction_test.gd
  → Исходящие (27): src/simulation/agents/AgentMovementService.cs, src/simulation/agents/AgentSpatialGrid.cs, src/simulation/building/BuildConfig.cs, src/simulation/structures/ConstructionManager.cs, src/simulation/items/DemandRegistry.cs, src/simulation/farming/FarmJobManager.cs, src/simulation/jobs/handlers/FarmingJobHandler.cs, src/simulation/items/GroundItemManager.cs, src/simulation/jobs/JobBroker.cs, src/core/jobs/JobData.cs, src/simulation/jobs/JobDispatcher.cs, src/core/world/MapData.cs, src/simulation/jobs/handlers/PlantingJobHandler.cs, src/simulation/items/ResourceBroker.cs, src/simulation/SimulationContext.cs, src/simulation/items/StockpileManager.cs, src/simulation/stones/StoneJobManager.cs, src/simulation/structures/StructureManager.cs, src/simulation/structures/StructureRegistry.cs, src/simulation/trees/TreeJobManager.cs, src/ui/tools/WorldPlacementPass.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs, src/simulation/AgentSimulationThread.cs, src/simulation/CellProgressStore.cs, src/simulation/DynamicWorkBalancer.cs

[tests/TestAdversarialJobDistribution.cs] (C#, LoC: 275, I: 0.92)
  ← Входящие (1): tests/run_adversarial_job_distribution_test.gd
  → Исходящие (11): src/simulation/agents/AgentDataPool.cs, src/simulation/agents/FlatIdleWorkerPool.cs, src/core/jobs/JobData.cs, src/simulation/jobs/JobDispatcher.cs, src/simulation/jobs/JobSpatialPyramid.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs, src/simulation/AgentSimulationThread.cs, src/simulation/CellProgressStore.cs, src/simulation/DynamicWorkBalancer.cs

[tests/TestDestroyerCamera.cs] (C#, LoC: 233, I: 0.8)
  ← Входящие (1): tests/run_destroyer_camera_test.gd
  → Исходящие (4): src/ui/CameraController.cs, src/ui/DayNightCycle.cs, src/ui/ICellProbeOverlay.cs, src/ui/PerformanceOverlay.cs

[tests/TestDestroyerStreaming.cs] (C#, LoC: 704, I: 0.95)
  ← Входящие (1): tests/run_destroyer_streaming_test.gd
  → Исходящие (20): src/core/world/streaming/integration/StreamingWorldManager.cs, src/ui/streaming/StreamingWorldView.cs, src/core/world/generation/UnifiedWorldGenerator.cs, src/core/world/streaming/integration/WorldCellQuery.cs, src/core/world/streaming/generation/WorldCoordinates.cs, src/core/world/streaming/integration/WorldDeltaKind.cs, src/ui/streaming/WorldJobBoard.cs, src/core/world/streaming/coordinator/WorldSaveCoordinator.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs, src/core/world/BiomeMapper.cs, src/core/world/BiomeType.cs, src/core/world/activearea/ActiveArea.cs, src/core/world/streaming/integration/ChunkMutations.cs, src/core/world/streaming/integration/IBlockWorld.cs, src/core/world/streaming/integration/IWallWorld.cs, src/ui/streaming/StreamingToolPlacement.cs, src/ui/streaming/WorldMapOverlay.cs, src/core/world/streaming/integration/WorldPlan.cs

[tests/TestDestroyerWorldGen.cs] (C#, LoC: 356, I: 0.94)
  ← Входящие (1): tests/run_destroyer_worldgen_test.gd
  → Исходящие (16): src/core/world/MapData.cs, src/core/world/climate/Q16.cs, src/core/world/generation/UnifiedWorldGenerator.cs, src/core/world/streaming/integration/WorldCellQuery.cs, src/core/world/streaming/generation/WorldChunk.cs, src/core/world/streaming/generation/WorldCoordinates.cs, src/core/world/streaming/integration/WorldDecor.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs, src/core/world/BiomeMapper.cs, src/core/world/BiomeType.cs, src/core/world/activearea/ActiveArea.cs, src/core/world/streaming/integration/ChunkMutations.cs, src/core/world/streaming/integration/IBlockWorld.cs, src/core/world/streaming/integration/IWallWorld.cs

[tests/TestGeneratorBench.cs] (C#, LoC: 403, I: 0.7)
  ← Входящие (7): tests/diag_csharp.gd, tests/run_bench_bulk.gd, tests/run_bench_cellat.gd, tests/run_bench_chunks.gd, tests/run_bench_micro.gd, tests/run_bench_windows.gd, tests/run_generator_bench.gd
  → Исходящие (16): src/core/world/streaming/integration/WorldCellQuery.cs, src/core/world/MapData.cs, src/core/world/generation/UnifiedWorldGenerator.cs, src/core/world/streaming/integration/WorldCellQuery.cs, src/core/world/streaming/generation/WorldChunk.cs, src/core/world/streaming/generation/WorldClimateGenerator.cs, src/core/world/streaming/generation/WorldRegions.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs, src/core/world/BiomeMapper.cs, src/core/world/BiomeType.cs, src/core/world/activearea/ActiveArea.cs, src/core/world/streaming/integration/ChunkMutations.cs, src/core/world/streaming/integration/IBlockWorld.cs, src/core/world/streaming/integration/IWallWorld.cs

[tests/TestIslandMirror.cs] (C#, LoC: 517, I: 0.96)
  ← Входящие (1): tests/run_island_store_test.gd
  → Исходящие (26): src/core/world/activearea/ActiveArea.cs, src/simulation/structures/ConstructionSite.cs, src/core/world/mutation/IslandDeltaApplicator.cs, src/core/world/mutation/IslandDeltaApplicator.cs, src/core/world/mutation/IslandMutationMirror.cs, src/core/world/mutation/IslandOverrideStore.cs, src/simulation/structures/StructureDefinition.cs, src/simulation/structures/StructureInstance.cs, src/simulation/structures/StructureManager.cs, src/core/world/streaming/integration/WorldDeltaKind.cs, src/core/world/mutation/IWorldMutationSink.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs, src/simulation/AgentSimulationThread.cs, src/simulation/CellProgressStore.cs, src/simulation/DynamicWorkBalancer.cs, src/core/world/BiomeMapper.cs, src/core/world/BiomeType.cs, src/core/world/activearea/ActiveAreaRegistry.cs, src/core/world/streaming/integration/ChunkMutations.cs, src/core/world/streaming/integration/IBlockWorld.cs, src/core/world/streaming/integration/IWallWorld.cs, src/core/world/mutation/IslandDomainReplay.cs, src/core/world/mutation/IslandPayload.cs, src/core/world/mutation/IslandStateReplay.cs

[tests/TestIslandZonePersistence.cs] (C#, LoC: 137, I: 0.96)
  ← Входящие (1): tests/run_island_zone_persistence_test.gd
  → Исходящие (22): src/core/world/mutation/IslandDomainReplay.cs, src/core/world/mutation/IslandOverrideStore.cs, src/core/world/mutation/IslandPayload.cs, src/core/world/streaming/integration/WorldDeltaKind.cs, src/simulation/zones/Zone.cs, src/simulation/zones/ZoneManager.cs, src/simulation/zones/ZonePersistence.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs, src/core/world/BiomeMapper.cs, src/core/world/BiomeType.cs, src/core/world/activearea/ActiveArea.cs, src/core/world/streaming/integration/ChunkMutations.cs, src/core/world/streaming/integration/IBlockWorld.cs, src/core/world/streaming/integration/IWallWorld.cs, src/core/world/mutation/IslandDeltaApplicator.cs, src/core/world/mutation/IslandMutationMirror.cs, src/core/world/mutation/IslandStateReplay.cs, src/simulation/AgentSimulationThread.cs, src/simulation/CellProgressStore.cs, src/simulation/DynamicWorkBalancer.cs

[tests/TestJobRaceStress.cs] (C#, LoC: 403, I: 0.9)
  ← Входящие (2): tests/run_job_race_stress.gd, tests/test_zone_agents.cs
  → Исходящие (19): src/simulation/jobs/IJobHandler.cs, src/simulation/agents/AgentDataPool.cs, src/simulation/agents/AgentMovementService.cs, src/simulation/agents/AgentSpatialGrid.cs, tests/test_zone_agents.cs, src/simulation/jobs/IJobHandler.cs, src/core/jobs/JobData.cs, src/simulation/jobs/JobRegistry.cs, src/simulation/jobs/JobSpatialPyramid.cs, src/simulation/SimulationContext.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs, src/simulation/AgentSimulationThread.cs, src/simulation/CellProgressStore.cs, src/simulation/DynamicWorkBalancer.cs, src/simulation/jobs/handlers/BlueprintDeliveryJobHandler.cs, src/simulation/jobs/handlers/ConstructionJobHandler.cs, src/simulation/jobs/handlers/FarmingJobHandler.cs

[tests/TestRenderLodBenchmark.cs] (C#, LoC: 253, I: 1.0)
  → Исходящие (16): src/ui/renderers/AgentLerpBatch.cs, src/ui/renderers/AgentRenderer.cs, src/simulation/AgentSimulationThread.cs, src/ui/DayNightCycle.cs, src/ui/renderers/ItemShadowRenderer.cs, src/ui/renderers/ShadowCasterRenderer.cs, src/ui/DayNightCycle.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs, src/simulation/CellProgressStore.cs, src/simulation/DynamicWorkBalancer.cs, src/simulation/GameSpeed.cs, src/ui/CameraController.cs, src/ui/ICellProbeOverlay.cs, src/ui/PerformanceOverlay.cs

[tests/TestWorkProgressPersistence.cs] (C#, LoC: 271, I: 0.96)
  ← Входящие (1): tests/run_work_progress_persistence_test.gd
  → Исходящие (26): src/simulation/agents/AgentDataPool.cs, src/simulation/agents/AgentMovementService.cs, src/simulation/agents/AgentSpatialGrid.cs, src/simulation/building/BuildConfig.cs, src/simulation/jobs/handlers/ConstructionJobHandler.cs, src/simulation/structures/ConstructionManager.cs, src/simulation/farming/FarmJobManager.cs, src/simulation/jobs/handlers/FarmingJobHandler.cs, src/core/jobs/JobData.cs, src/simulation/jobs/JobRegistry.cs, src/simulation/jobs/handlers/MiningJobHandler.cs, src/simulation/SimulationContext.cs, src/simulation/stones/StoneJobManager.cs, src/simulation/structures/StructureRegistry.cs, src/simulation/jobs/handlers/TreeChoppingJobHandler.cs, src/simulation/trees/TreeJobManager.cs, src/simulation/WorldTime.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs, src/simulation/AgentSimulationThread.cs, src/simulation/CellProgressStore.cs, src/simulation/DynamicWorkBalancer.cs, src/simulation/jobs/handlers/BlueprintDeliveryJobHandler.cs, src/simulation/jobs/handlers/HarvestJobHandler.cs, src/simulation/jobs/handlers/NeedsJobSystem.cs

[tests/TestWorldGen.cs] (C#, LoC: 1002, I: 1.0)
  → Исходящие (20): src/core/world/BiomeMapper.cs, src/ui/CameraController.cs, src/core/world/climate/ContinentShaper.cs, scenes/main/Main.cs, scenes/main/Main.tscn, src/core/world/MountainTileHelper.cs, src/core/world/generation/UnifiedWorldGenerator.cs, src/core/world/streaming/generation/WorldChunk.cs, src/core/world/streaming/generation/WorldClimateGenerator.cs, src/core/world/climate/WorldClimateProvider.cs, src/core/world/streaming/generation/WorldCoordinates.cs, scenes/ui/WorldMap.tscn, src/ui/streaming/WorldMapOverlay.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs, src/core/world/BiomeType.cs, src/core/world/activearea/ActiveArea.cs, src/core/world/activearea/ActiveAreaRegistry.cs, src/core/world/streaming/generation/RegionKey.cs

[tests/TestWorldGenSeamAudit.cs] (C#, LoC: 184, I: 0.94)
  ← Входящие (1): tests/run_world_gen_seam_audit.gd
  → Исходящие (17): scenes/main/Main.cs, scenes/main/Main.tscn, src/ui/renderers/MapRenderer.cs, src/core/world/generation/UnifiedWorldGenerator.cs, src/core/world/streaming/generation/WorldCoordinates.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs, src/core/world/BiomeMapper.cs, src/core/world/BiomeType.cs, src/core/world/activearea/ActiveArea.cs, src/core/world/streaming/integration/ChunkMutations.cs, src/core/world/streaming/integration/IBlockWorld.cs, src/core/world/streaming/integration/IWallWorld.cs, src/ui/CameraController.cs, src/ui/DayNightCycle.cs, src/ui/ICellProbeOverlay.cs

[tests/TestWorldGenV2.cs] (C#, LoC: 594, I: 0.96)
  ← Входящие (1): tests/run_worldgen_v2.gd
  → Исходящие (22): src/core/world/generation/WorldGenV2.cs, src/core/world/MapData.cs, src/ui/streaming/StreamingWorldView.cs, src/core/world/generation/UnifiedWorldGenerator.cs, src/core/world/streaming/generation/WorldChunk.cs, src/core/world/streaming/generation/WorldClimateGenerator.cs, src/core/world/streaming/generation/WorldCoordinates.cs, src/core/world/streaming/integration/WorldDecor.cs, src/core/world/generation/WorldGenV2.cs, src/core/world/streaming/generation/WorldRegions.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs, src/core/world/BiomeMapper.cs, src/core/world/BiomeType.cs, src/core/world/activearea/ActiveArea.cs, src/core/world/streaming/integration/ChunkMutations.cs, src/core/world/streaming/integration/IBlockWorld.cs, src/core/world/streaming/integration/IWallWorld.cs, src/ui/streaming/StreamingToolPlacement.cs, src/ui/streaming/WorldJobBoard.cs, src/ui/streaming/WorldMapOverlay.cs

[tests/TestWorldStoreExit.cs] (C#, LoC: 240, I: 0.93)
  ← Входящие (1): tests/run_store_exit_test.gd
  → Исходящие (14): src/core/world/streaming/integration/StreamingWorldManager.cs, src/ui/streaming/StreamingWorldView.cs, src/core/world/generation/UnifiedWorldGenerator.cs, src/core/world/streaming/integration/WorldDeltaKind.cs, src/core/world/streaming/coordinator/WorldSaveCoordinator.cs, src/core/world/BiomeMapper.cs, src/core/world/BiomeType.cs, src/core/world/activearea/ActiveArea.cs, src/core/world/streaming/integration/ChunkMutations.cs, src/core/world/streaming/integration/IBlockWorld.cs, src/core/world/streaming/integration/IWallWorld.cs, src/ui/streaming/StreamingToolPlacement.cs, src/ui/streaming/WorldJobBoard.cs, src/ui/streaming/WorldMapOverlay.cs

[tests/diag_csharp.gd] (GDScript, LoC: 31, I: 1.0)
  → Исходящие (2): tests/Phase3StressTestRunner.cs, tests/TestGeneratorBench.cs

[tests/probe_controls.gd] (GDScript, LoC: 13, I: 0.5)
  (изолированный файл)

[tests/probe_plansource.gd] (GDScript, LoC: 11, I: 0.5)
  (изолированный файл)

[tests/run_activearea_test.gd] (GDScript, LoC: 31, I: 1.0)
  → Исходящие (1): tests/TestActiveArea.cs

[tests/run_adversarial_construction_test.gd] (GDScript, LoC: 31, I: 1.0)
  → Исходящие (1): tests/TestAdversarialConstruction.cs

[tests/run_adversarial_job_distribution_test.gd] (GDScript, LoC: 31, I: 1.0)
  → Исходящие (1): tests/TestAdversarialJobDistribution.cs

[tests/run_bench_bulk.gd] (GDScript, LoC: 30, I: 1.0)
  → Исходящие (1): tests/TestGeneratorBench.cs

[tests/run_bench_cellat.gd] (GDScript, LoC: 30, I: 1.0)
  → Исходящие (1): tests/TestGeneratorBench.cs

[tests/run_bench_chunks.gd] (GDScript, LoC: 30, I: 1.0)
  → Исходящие (1): tests/TestGeneratorBench.cs

[tests/run_bench_micro.gd] (GDScript, LoC: 30, I: 1.0)
  → Исходящие (1): tests/TestGeneratorBench.cs

[tests/run_bench_windows.gd] (GDScript, LoC: 33, I: 1.0)
  → Исходящие (1): tests/TestGeneratorBench.cs

[tests/run_central_block_test.gd] (GDScript, LoC: 26, I: 1.0)
  → Исходящие (1): tests/test_central_block_zone.cs

[tests/run_destroyer_camera_test.gd] (GDScript, LoC: 31, I: 1.0)
  → Исходящие (1): tests/TestDestroyerCamera.cs

[tests/run_destroyer_streaming_test.gd] (GDScript, LoC: 31, I: 1.0)
  → Исходящие (1): tests/TestDestroyerStreaming.cs

[tests/run_destroyer_worldgen_test.gd] (GDScript, LoC: 31, I: 1.0)
  → Исходящие (1): tests/TestDestroyerWorldGen.cs

[tests/run_generator_bench.gd] (GDScript, LoC: 33, I: 1.0)
  → Исходящие (1): tests/TestGeneratorBench.cs

[tests/run_island_store_test.gd] (GDScript, LoC: 30, I: 1.0)
  → Исходящие (1): tests/TestIslandMirror.cs

[tests/run_island_zone_persistence_test.gd] (GDScript, LoC: 37, I: 1.0)
  → Исходящие (1): tests/TestIslandZonePersistence.cs

[tests/run_job_assignment_bench.gd] (GDScript, LoC: 29, I: 1.0)
  → Исходящие (1): tests/BenchJobAssignment.cs

[tests/run_job_race_stress.gd] (GDScript, LoC: 29, I: 1.0)
  → Исходящие (1): tests/TestJobRaceStress.cs

[tests/run_pathfinding_bench.gd] (GDScript, LoC: 25, I: 1.0)
  → Исходящие (1): tests/BenchPathfinding.cs

[tests/run_phase3_suite.gd] (GDScript, LoC: 32, I: 1.0)
  → Исходящие (1): tests/Phase3StressTestRunner.cs

[tests/run_phase4_suite.gd] (GDScript, LoC: 32, I: 1.0)
  → Исходящие (1): tests/Phase4StressTestRunner.cs

[tests/run_phase5_suite.gd] (GDScript, LoC: 33, I: 1.0)
  → Исходящие (1): tests/Phase5StressTestRunner.cs

[tests/run_store_exit_test.gd] (GDScript, LoC: 31, I: 1.0)
  → Исходящие (1): tests/TestWorldStoreExit.cs

[tests/run_terrain_window_bench.gd] (GDScript, LoC: 32, I: 1.0)
  → Исходящие (1): tests/TerrainWindowBench.cs

[tests/run_work_progress_persistence_test.gd] (GDScript, LoC: 38, I: 1.0)
  → Исходящие (1): tests/TestWorkProgressPersistence.cs

[tests/run_world_gen_seam_audit.gd] (GDScript, LoC: 13, I: 1.0)
  → Исходящие (1): tests/TestWorldGenSeamAudit.cs

[tests/run_worldgen_v2.gd] (GDScript, LoC: 31, I: 1.0)
  → Исходящие (1): tests/TestWorldGenV2.cs

[tests/run_zone_agents_test.gd] (GDScript, LoC: 26, I: 1.0)
  → Исходящие (1): tests/test_zone_agents.cs

[tests/run_zone_model_test.gd] (GDScript, LoC: 26, I: 1.0)
  → Исходящие (1): tests/test_zone_model.cs

[tests/run_zone_router_test.gd] (GDScript, LoC: 26, I: 1.0)
  → Исходящие (1): tests/test_zone_router.cs

[tests/smoke_build_menu.gd] (GDScript, LoC: 34, I: 1.0)
  → Исходящие (1): scenes/ui/Build.tscn

[tests/test_central_block_zone.cs] (C#, LoC: 167, I: 0.94)
  ← Входящие (1): tests/run_central_block_test.gd
  → Исходящие (16): src/simulation/building/BuildConfig.cs, src/ui/renderers/MapRenderer.cs, src/simulation/structures/StructureManager.cs, src/simulation/structures/StructureRegistry.cs, src/simulation/zones/WorkZoneManagerFacade.cs, src/simulation/zones/ZoneJobRouter.cs, src/simulation/zones/ZoneManager.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs, src/simulation/AgentSimulationThread.cs, src/simulation/CellProgressStore.cs, src/simulation/DynamicWorkBalancer.cs, src/ui/CameraController.cs, src/ui/DayNightCycle.cs, src/ui/ICellProbeOverlay.cs

[tests/test_phase0.gd] (GDScript, LoC: 60, I: 1.0)
  → Исходящие (3): scenes/main/Main.tscn, scenes/ui/Build.tscn, scenes/ui/JobDisplay.tscn

[tests/test_phase1.gd] (GDScript, LoC: 24, I: 1.0)
  → Исходящие (2): tests/Phase1StressTestRunner.cs, scenes/ui/menu.tscn

[tests/test_phase2.gd] (GDScript, LoC: 16, I: 1.0)
  → Исходящие (1): tests/Phase2StressTestRunner.cs

[tests/test_phase3.gd] (GDScript, LoC: 16, I: 1.0)
  → Исходящие (1): tests/Phase3StressTestRunner.cs

[tests/test_phase4.gd] (GDScript, LoC: 16, I: 1.0)
  → Исходящие (1): tests/Phase4StressTestRunner.cs

[tests/test_phase5.gd] (GDScript, LoC: 16, I: 1.0)
  → Исходящие (1): tests/Phase5StressTestRunner.cs

[tests/test_streamingview_decor_seam.cs] (C#, LoC: 304, I: 0.95)
  ← Входящие (1): tests/test_streamingview_decor_seam.gd
  → Исходящие (21): src/core/world/MapData.cs, src/ui/streaming/StreamingWorldView.cs, src/core/world/generation/UnifiedWorldGenerator.cs, src/core/world/streaming/generation/WorldChunk.cs, src/core/world/streaming/generation/WorldClimateGenerator.cs, src/core/world/streaming/generation/WorldCoordinates.cs, src/core/world/streaming/integration/WorldDecor.cs, src/core/world/streaming/generation/WorldRegions.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs, src/core/world/BiomeMapper.cs, src/core/world/BiomeType.cs, src/core/world/activearea/ActiveArea.cs, src/core/world/streaming/integration/ChunkMutations.cs, src/core/world/streaming/integration/IBlockWorld.cs, src/core/world/streaming/integration/IWallWorld.cs, src/ui/streaming/StreamingToolPlacement.cs, src/ui/streaming/WorldJobBoard.cs, src/ui/streaming/WorldMapOverlay.cs, src/core/world/streaming/generation/GeneratedCell.cs

[tests/test_streamingview_decor_seam.gd] (GDScript, LoC: 17, I: 1.0)
  → Исходящие (1): tests/test_streamingview_decor_seam.cs

[tests/test_streamingview_jobdisplay_smoke.cs] (C#, LoC: 102, I: 0.67)
  ← Входящие (1): tests/test_streamingview_jobdisplay_smoke.gd
  → Исходящие (2): scenes/ui/JobDisplay.tscn, scenes/ui/Prioritet.tscn

[tests/test_streamingview_jobdisplay_smoke.gd] (GDScript, LoC: 19, I: 1.0)
  → Исходящие (1): tests/test_streamingview_jobdisplay_smoke.cs

[tests/test_zone_agents.cs] (C#, LoC: 434, I: 0.92)
  ← Входящие (2): tests/run_zone_agents_test.gd, tests/TestJobRaceStress.cs
  → Исходящие (22): src/simulation/jobs/IJobHandler.cs, src/simulation/agents/AgentDataPool.cs, src/simulation/agents/AgentMovementService.cs, src/simulation/agents/AgentDataPool.cs, src/simulation/agents/AgentSpatialGrid.cs, tests/TestJobRaceStress.cs, src/simulation/CellProgressStore.cs, src/simulation/jobs/IJobHandler.cs, src/core/jobs/JobData.cs, src/simulation/jobs/JobDispatcher.cs, src/simulation/jobs/JobRegistry.cs, src/simulation/SimulationContext.cs, src/simulation/zones/WorkZoneManagerFacade.cs, src/simulation/agents/WorkerZoneJobSystem.cs, src/simulation/zones/ZoneJobRouter.cs, src/simulation/zones/ZoneManager.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs, src/simulation/AgentSimulationThread.cs, src/simulation/DynamicWorkBalancer.cs, src/simulation/GameSpeed.cs

[tests/test_zone_model.cs] (C#, LoC: 225, I: 0.88)
  ← Входящие (1): tests/run_zone_model_test.gd
  → Исходящие (7): scenes/ui/Farm.tscn, src/simulation/zones/WorkZone.cs, src/simulation/zones/WorkZoneManagerFacade.cs, src/simulation/zones/ZoneManager.cs, src/simulation/AgentSimulationThread.cs, src/simulation/CellProgressStore.cs, src/simulation/DynamicWorkBalancer.cs

[tests/test_zone_router.cs] (C#, LoC: 408, I: 0.92)
  ← Входящие (1): tests/run_zone_router_test.gd
  → Исходящие (11): src/core/jobs/JobData.cs, src/simulation/jobs/JobDispatcher.cs, src/simulation/zones/WorkZoneManagerFacade.cs, src/simulation/zones/ZoneJobRouter.cs, src/simulation/zones/ZoneManager.cs, src/core/common/CpuTopology.cs, src/core/common/GameProfiler.cs, src/core/common/ProfilerLogService.cs, src/simulation/AgentSimulationThread.cs, src/simulation/CellProgressStore.cs, src/simulation/DynamicWorkBalancer.cs

[tools/atlas_probe.gd] (GDScript, LoC: 47, I: 0.5)
  (изолированный файл)
