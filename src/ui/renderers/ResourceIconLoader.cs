using Godot;
using System.Collections.Generic;

namespace Game.UI.Resources;

/// <summary>
/// Загрузчик иконок ресурсов для UI: <c>res://ui/assets/textures/drop/&lt;id&gt;.png</c>
/// с кэшем и безопасным null-фолбэком.
///
/// Иконки 64×64 в стиле игры. Отсутствующий файл не роняет UI: возвращается null,
/// вызывающая сторона просто не рисует иконку.
/// </summary>
public static class ResourceIconLoader
{
    private const string Root = "res://ui/assets/textures/drop/";

    private static readonly Dictionary<string, Texture2D> _cache = new(96);
    private static readonly HashSet<string> _missing = new(96);

    /// <summary>Текстура дропа ресурса по его id из атласа (например "wheat").</summary>
    public static Texture2D Load(string resourceId)
    {
        if (string.IsNullOrEmpty(resourceId))
            return null;

        if (_cache.TryGetValue(resourceId, out Texture2D cached))
            return cached;
        if (_missing.Contains(resourceId))
            return null;

        string path = Root + resourceId + ".png";
        Texture2D tex = ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;

        if (tex == null)
        {
            _missing.Add(resourceId);
            GD.PushWarning($"[ResourceIconLoader] нет иконки: {path}");
        }
        else
        {
            _cache[resourceId] = tex;
        }

        return tex;
    }

    /// <summary>Сбросить кэш (смена профиля/темы).</summary>
    public static void Clear()
    {
        _cache.Clear();
        _missing.Clear();
    }
}