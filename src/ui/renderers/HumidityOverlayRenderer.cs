using Godot;
using Game.Core;
using Game.Core.WorldStreaming.Integration;
using Game.Core.WorldStreaming;
using System;

namespace Game.UI;

/// <summary>
/// Оверлей влажности: адаптивная текстура 128×128 с билинейной фильтрацией,
/// динамически следующая за видимым экраном игрока и бесшовно объединяющая
/// динамическую карту колонии 512×512 и бесконечный процедурный мир за ее пределами.
/// Слой виден на 80%: сухое — почти белое, мокрое — тёмно-синий.
/// Троттлинг 1с, ноль GC аллокаций в главном потоке.
/// </summary>
public partial class HumidityOverlayRenderer : TextureRect
{
    private const int TexSize = 128;
    private const float RefreshIntervalSec = 1.0f;

    private ImageTexture _texture;
    private float _timer;
    private Image _reuseImg;
    // FIX (bughunt 14-6): переиспользуемый RGBA8-буфер (128×128×4 = 64 КБ): собираем
    // пиксели в обычном массиве и заливаем одной операцией Image.SetData, а не 16 384
    // интероп-вызовами SetPixel на главном потоке. Аллокация одна, поле переиспользуется.
    private byte[] _pixels;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.TopLeft);
        Size = new Vector2(MapRenderer.MapWidth * MapRenderer.TileSizePx,
            MapRenderer.MapHeight * MapRenderer.TileSizePx);
        Position = Vector2.Zero;
        StretchMode = StretchModeEnum.Scale;
        TextureFilter = CanvasItem.TextureFilterEnum.Linear;
        MouseFilter = Control.MouseFilterEnum.Ignore;

        _texture = new ImageTexture();
        Texture = _texture;
    }

    /// <summary>Принудительно перестроить текстуру из карты влажности и стримингового мира.</summary>
    public void Refresh(HumidityMap humidity)
    {
        if (_texture == null)
            return;
        if (!Visible)
        {
            _timer = 0f;
            return;
        }

        int tileMinX = 0;
        int tileMinY = 0;
        int tileMaxX = MapRenderer.MapWidth;
        int tileMaxY = MapRenderer.MapHeight;

        var cam = GetViewport()?.GetCamera2D();
        if (cam != null)
        {
            Rect2 viewport = GetViewport().GetVisibleRect();
            float zoom = Math.Max(0.01f, cam.Zoom.X);
            float halfW = viewport.Size.X / (2f * zoom);
            float halfH = viewport.Size.Y / (2f * zoom);
            float minX = cam.Position.X - halfW;
            float minY = cam.Position.Y - halfH;
            float maxX = cam.Position.X + halfW;
            float maxY = cam.Position.Y + halfH;

            tileMinX = (int)MathF.Floor(minX / 64f) - 2;
            tileMinY = (int)MathF.Floor(minY / 64f) - 2;
            tileMaxX = (int)MathF.Ceiling(maxX / 64f) + 2;
            tileMaxY = (int)MathF.Ceiling(maxY / 64f) + 2;
        }

        int viewTilesW = Math.Max(1, tileMaxX - tileMinX);
        int viewTilesH = Math.Max(1, tileMaxY - tileMinY);

        Position = new Vector2(tileMinX * 64f, tileMinY * 64f);
        Size = new Vector2(viewTilesW * 64f, viewTilesH * 64f);

        _reuseImg ??= Image.CreateEmpty(TexSize, TexSize, false, Image.Format.Rgba8);
        byte[] px = _pixels ??= new byte[TexSize * TexSize * 4];
        var img = _reuseImg;
        int humW = humidity?.Width ?? 0;
        int humH = humidity?.Height ?? 0;

        for (int ty = 0; ty < TexSize; ty++)
        {
            // FIX (bughunt 14-7): единственное центрирование — по ЦЕНТРУ текселя.
            // Текстура растянута (StretchMode.Scale) ровно на viewTilesH тайлов, поэтому
            // центр текселя ty лежит в тайле floor((ty + 0.5) * viewTilesH / TexSize).
            // Целочисленный эквивалент (все величины > 0): ((2*ty + 1) * viewTilesH) / (2*TexSize).
            int mapY = tileMinY + ((ty * 2 + 1) * viewTilesH) / (TexSize * 2);
            int row = ty * TexSize * 4;
            for (int tx = 0; tx < TexSize; tx++)
            {
                int mapX = tileMinX + ((tx * 2 + 1) * viewTilesW) / (TexSize * 2);
                int moisture;
                if (humidity != null && (uint)mapX < (uint)humW && (uint)mapY < (uint)humH)
                {
                    moisture = humidity.Get(mapX, mapY);
                }
                else
                {
                    // ДВЕРЬ: за картой — WorldModel (сид/версия/решётка 16×16 — единые;
                    // последовательные клетки цикка держат ThreadStatic-кэш блока).
                    ushort q16 = Game.Core.World.WorldModel.MoistureQ16AtLocal(mapX, mapY);
                    moisture = (int)(q16 * 200L / 65535);
                }
                // ToRgba32 — та же упаковка, которой Image.SetPixel писал в RGBA8:
                // r | g<<8 | b<<16 | a<<24, байт в памяти: R,G,B,A.
                uint rgba = MoistureColor(moisture).ToRgba32();
                int o = row + tx * 4;
                px[o + 0] = (byte)rgba;
                px[o + 1] = (byte)(rgba >> 8);
                px[o + 2] = (byte)(rgba >> 16);
                px[o + 3] = (byte)(rgba >> 24);
            }
        }
        // FIX (bughunt 14-6): одна заливка всего кадра вместо 16 384 SetPixel.
        img.SetData(TexSize, TexSize, false, Image.Format.Rgba8, px);
        _texture.SetImage(img);
        _timer = 0f;
    }

    /// <summary>Обновление с троттлингом — звать каждый кадр, реально строит раз в 1с.</summary>
    public void RefreshThrottled(HumidityMap humidity, double delta)
    {
        _timer += (float)delta;
        if (_timer >= RefreshIntervalSec)
            Refresh(humidity);
    }

    /// <summary>
    /// Цвет по влажности (верха нет — всё что ≥200 клампим в тёмно-синий).
    /// Весь слой виден на 80% (alpha = 0.8): сухое — почти белый,
    /// мокрое — тёмно-синий. Каждый процент влияет.
    /// </summary>
    public static Color MoistureColor(int moisture)
    {
        float t = moisture / 200f; // 0..1, всё что выше 1 → 1 (тёмно-синий)
        if (t > 1f) t = 1f;
        // Белый (1,1,1) → средний голубой (0.35,0.65,1.0) → тёмно-синий (0,0.15,0.55).
        // Alpha всегда 0.8 — слой виден на 80%.
        const float Alpha = 0.8f;
        float r, g, b;
        if (t <= 0.5f)
        {
            float k = t * 2f;
            r = 1f + (0.35f - 1f) * k;
            g = 1f + (0.65f - 1f) * k;
            b = 1f;
        }
        else
        {
            float k = (t - 0.5f) * 2f;
            r = 0.35f * (1f - k);
            g = 0.65f + (0.15f - 0.65f) * k;
            b = 1f + (0.55f - 1f) * k;
        }
        return new Color(r, g, b, Alpha);
    }
}
