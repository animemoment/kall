using System.Reflection;
using Godot;
using Game.Core;
using Game.Core.World;
using Game.Core.WorldStreaming;
using Game.UI;

namespace Game.Tests;

/// <summary>
/// §03-03: влажность/плодородие за кромкой острова. Два атома:
/// (1) дверь в <see cref="WorldModel"/> (SampleLatticeAbsolute) считает
/// off-island координаты — без данных тултип показывать нечего;
/// (2) HUDController.CellTooltipLayer работает по режиму (28-06): в режиме
/// Humidity тултип влажности показан, а не погашен "вторым вызовом".
/// TickSoilTooltip (фикс §03-03) теперь доходит до ShowHumidityAt/ShowFertilityAt
/// для tx/ty за кромкой — здесь проверяем, что данные для них есть и слой
/// показывает корректно.
/// </summary>
public partial class TestSoilTooltipOffIsland : RefCounted
{
    public bool Run()
    {
        GD.Print("=====================================================");
        GD.Print("[TEST] §03-03: дверь считает off-island -> тултип показывает");
        GD.Print("=====================================================");

        try
        {
            ulong seed = 0xDE570001UL;
            WorldModel.Configure(seed, 3);
            WorldModel.BindResident(null);
            UnifiedWorldGenerator.SetWorldOrigin(0, 0);

            // Клетка ЗА кромкой: координаты за пределами MapData.Ground (1024^2).
            long offX = 1076L;
            long offY = 964L;

            var cell = WorldModel.CellAtLocal(offX, offY);
            GD.Print($"[off-island door: ({offX},{offY}) terrain={cell.Terrain} " +
                     $"moisture={cell.MoistureQ16} forest={cell.ForestQ16}]");

            if (cell.Terrain != BaseTerrainKind.Grass)
            {
                GD.PrintErr("[P1] (1076,964) не трава — подбираем ближайшую траву...");
                for (long dy = -200; dy <= 200; dy++)
                {
                    for (long dx = -200; dx <= 200; dx++)
                    {
                        var c = WorldModel.CellAtLocal(offX + dx, offY + dy);
                        if (c.Terrain == BaseTerrainKind.Grass)
                        {
                            offX += dx;
                            offY += dy;
                            cell = c;
                            GD.Print($"[найдена трава за кромкой: ({offX},{offY})]");
                            goto FoundGrass;
                        }
                    }
                }
            }
        FoundGrass:
            if (cell.Terrain != BaseTerrainKind.Grass)
            {
                GD.PrintErr("[FAIL] за кромкой в +/-200 клеток нет травы — дверь не считала");
                return false;
            }

            // (1) Дверь вернула данные: влажность и лес в Q16. Показ тултипа
            // переводит их в проценты точно так же, как HUDController.
            int humidity = (int)((long)cell.MoistureQ16 * 200 / 65535);
            string humidityWord = humidity < 40 ? "сухо" : humidity <= 130 ? "норма" : "мокро";
            string humidityText = $"{humidity}% {humidityWord}";

            int fertility = (int)((long)cell.ForestQ16 * 200 / 65535);
            string fertilityWord = fertility < 60 ? "бедно" : fertility <= 140 ? "норма" : "богато";
            string fertilityText = $"{fertility} {fertilityWord}";

            GD.Print($"[湿度] {humidityText}");
            GD.Print($"[плодородие] {fertilityText}");

            if (humidity < 0 || humidity > 200 || fertility < 0 || fertility > 200)
            {
                GD.PrintErr("[P1] дверь вернула невалидные проценты");
                return false;
            }

            // (2) Тултип-слой: в режиме Humidity должен показывать湿度 (как в
            // TestHudMouseProbe), а второй вызов ShowFertilityAt не гасит слой.
            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree == null)
            {
                GD.PrintErr("[SKIP] нет SceneTree — проверяем только дверь (1)");
                return true;
            }

            var holder = new Control { Name = "TooltipHolder" };
            tree.Root.AddChild(holder);

            var humidityLabel = new Label { Name = "HumidityTooltip", Visible = false };
            var fertilityLabel = new Label { Name = "FertilityTooltip", Visible = false };
            holder.AddChild(humidityLabel);
            holder.AddChild(fertilityLabel);

            var humidityTip = new HUDController.CellTooltipLayer(humidityLabel, MapMode.Humidity);
            var fertilityTip = new HUDController.CellTooltipLayer(fertilityLabel, MapMode.Fertility);
            var mouse = new Vector2(100, 100);

            bool ok = true;
            // Показ humidity в своём режиме.
            ok &= humidityTip.Present(MapMode.Humidity, humidityText, mouse)
                && humidityTip.Visible && humidityTip.Text == humidityText;
            // Второй вызов — ShowFertilityAt — не гасит влажность (28-06 / §03-03).
            ok &= !fertilityTip.Present(MapMode.Humidity, fertilityText, mouse)
                && humidityTip.Visible;
            // И наоборот: режим плодородия.
            ok &= fertilityTip.Present(MapMode.Fertility, fertilityText, mouse)
                && fertilityTip.Visible && fertilityTip.Text == fertilityText;
            ok &= !humidityTip.Present(MapMode.Fertility, humidityText, mouse)
                && fertilityTip.Visible;
            // Оба слоя — два независимых состояния.
            humidityTip.Present(MapMode.Humidity, humidityText, mouse);
            ok &= humidityTip.Visible && fertilityTip.Visible;
            fertilityTip.Hide();
            ok &= humidityTip.Visible && !fertilityTip.Visible;
            humidityTip.Hide();
            ok &= !humidityTip.Visible && !fertilityTip.Visible;
            ok &= humidityTip.Mode == MapMode.Humidity && fertilityTip.Mode == MapMode.Fertility;

            GD.Print($"[слой] humidity={humidityTip.Visible} fertility={fertilityTip.Visible}");
            GD.Print($"[слой] mode OK: {humidityTip.Mode} / {fertilityTip.Mode}");

            Drop(holder);

            if (!ok)
            {
                GD.PrintErr("[P1] тултип-слой ведёт себя неправильно за кромкой");
                return false;
            }

            GD.Print("[OK] off-island данные есть и тултип показывает верно");
            GD.Print("=====================================================");
            GD.Print("[OK] DONE");
            GD.Print("=====================================================");
            return true;
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[TEST FAILED] {ex}");
            return false;
        }
    }

    private static void Drop(Node node)
    {
        if (node == null) return;
        node.GetParent()?.RemoveChild(node);
        node.QueueFree();
    }
}
