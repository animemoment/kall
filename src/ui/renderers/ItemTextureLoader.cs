using Godot;
using Game.Core;
using System;
using System.Collections.Generic;

namespace Game.UI;

/// <summary>
/// Загрузка спрайта предмета (<see cref="ItemDefinition.TextureUid"/>) с корректным
/// поведением для арта, который ещё не проимпортирован Godot:
///  1) <c>uid://</c> и <c>res://</c> — через <see cref="ResourceLoader"/> (штатный путь);
///  2) если файл на диске есть, а импорт ещё не прошёл — читаем PNG напрямую
///     (<c>FileAccess.GetFileAsBytes</c> + <c>Image.LoadPngFromBuffer</c>);
///  3) нет файла — ОДИН warning на предмет (не спамим каждый кадр), рендерер не создаём.
///
/// Кэш положительный и отрицательный: повторные вызовы в _Process бесплатны.
/// Только главный поток (Texture2D/Image).
/// </summary>
public static class ItemTextureLoader
{
    private static readonly Dictionary<string, Texture2D> _cache = new();
    private static readonly HashSet<string> _missing = new();
    private static readonly HashSet<string> _warned = new();

    /// <summary>Загруженная текстура предмета или null (уже предупреждали).</summary>
    public static Texture2D Load(ItemDefinition definition)
    {
        if (definition == null)
            return null;
        string reference = definition.TextureUid;
        if (string.IsNullOrEmpty(reference))
        {
            Warn(definition, reference);
            return null;
        }
        if (_cache.TryGetValue(reference, out Texture2D cached))
            return cached;
        if (_missing.Contains(reference))
            return null;

        Texture2D texture = LoadReference(reference);
        if (texture == null)
        {
            _missing.Add(reference);
            Warn(definition, reference);
            return null;
        }
        _cache[reference] = texture;
        return texture;
    }

    /// <summary>Сбросить кэш (тесты/горячая подмена арта).</summary>
    public static void Reset()
    {
        _cache.Clear();
        _missing.Clear();
        _warned.Clear();
    }

    private static Texture2D LoadReference(string reference)
    {
        try
        {
            if (reference.StartsWith("res://", StringComparison.Ordinal))
            {
                // Неимпортированный PNG: ResourceLoader вернёт null, ошибку движка
                // не глушим — поэтому сначала проверяем существование файла.
                if (!Godot.FileAccess.FileExists(reference))
                    return null;
                Texture2D imported = ResourceLoader.Load<Texture2D>(reference);
                if (imported != null)
                    return imported;

                byte[] bytes = Godot.FileAccess.GetFileAsBytes(reference);
                if (bytes == null || bytes.Length == 0)
                    return null;
                var image = new Image();
                if (image.LoadPngFromBuffer(bytes) != Error.Ok)
                    return null;
                return ImageTexture.CreateFromImage(image);
            }

            return ResourceLoader.Load<Texture2D>(reference);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void Warn(ItemDefinition definition, string reference)
    {
        if (!_warned.Add(reference ?? ""))
            return;
        GD.PushWarning(
            $"[Items] спрайт '{reference}' для «{definition.Name}» ({definition.Id}) не найден — " +
            "предмет на земле/складе не рисуется. Остальная игра работает.");
    }
}