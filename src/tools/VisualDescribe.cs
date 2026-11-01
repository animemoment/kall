using System.Globalization;
using System.Text;
using Godot;

namespace Game.Tools;

/// <summary>
/// Единое описание текстуры/тайла для инспекторов (SeamInspector и будущий hover-инспектор).
/// Отвечает на вопрос «что это за картинка и откуда она взялась»:
/// файл на диске, атлас-регион или сгенерирована в рантайме (ImageTexture/ViewportTexture).
///
/// Зачем отдельный класс: MapRenderer и StreamingWorldView оба должны описывать
/// текстуры ОДИНАКОВО, иначе «диагноз» на стыке зависит от того, кто его печатает.
/// </summary>
public static class VisualDescribe
{
    /// <summary>
    /// Описать текстуру. atlasSource/atlasCoords — если тайл взят из TileSetAtlasSource:
    /// тогда дополнительно печатается пиксельный регион тайла в атласе.
    /// </summary>
    public static string Texture(
        Texture2D tex,
        TileSetAtlasSource atlasSource = null,
        Vector2I atlasCoords = default)
    {
        if (tex == null)
            return "tex=null";

        string path = tex.ResourcePath;
        bool fromFile = !string.IsNullOrEmpty(path);

        var sb = new StringBuilder(192);
        if (fromFile)
        {
            sb.Append("tex=").Append(path);
            sb.Append(ResourceLoader.Exists(path) ? " file=ok" : " file=MISSING");
            sb.Append(ResourceLoader.HasCached(path) ? " cached=y" : " cached=n");
        }
        else
        {
            // Пустой ResourcePath = ресурса-файла нет: ImageTexture.CreateFromImage,
            // ViewportTexture, GradientTexture и прочая генерация в рантайме.
            sb.Append("tex=<runtime:").Append(tex.GetClass()).Append('>');
        }

        sb.Append(" size=").Append(tex.GetSize());

        if (tex is AtlasTexture atlasTexture)
        {
            string atlasPath = atlasTexture.Atlas?.ResourcePath;
            sb.Append(" atlasOf=")
              .Append(string.IsNullOrEmpty(atlasPath) ? "<runtime>" : atlasPath)
              .Append(" region=").Append(atlasTexture.Region);
        }

        if (atlasSource != null && atlasSource.Texture == tex)
        {
            // GetTileTextureRegion требует существующий тайл: у «пустых» ячеек
            // атласа движок ругается в консоль. Проверяем явно.
            if (atlasSource.HasTile(atlasCoords))
            {
                Rect2I region = atlasSource.GetTileTextureRegion(atlasCoords, 0);
                sb.Append(" tile=").Append(atlasCoords).Append(" px=").Append(region);
            }
            else
            {
                sb.Append(" tile=").Append(atlasCoords).Append(" px=<no such tile>");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Короткий токен для сетки 10×10: «wooden_wall.png#5,6» или «rt:ImageTexture».
    /// Полное описание (пути, кэш, регион) остаётся в <see cref="Texture"/>.
    /// </summary>
    public static string Short(
        Texture2D tex,
        TileSetAtlasSource atlasSource = null,
        Vector2I atlasCoords = default)
    {
        if (tex == null)
            return "null";

        string name = string.IsNullOrEmpty(tex.ResourcePath)
            ? "rt:" + tex.GetClass()
            : System.IO.Path.GetFileName(tex.ResourcePath);

        if (atlasSource != null && atlasSource.Texture == tex)
            name += "#" + atlasCoords.X + "," + atlasCoords.Y;

        return name;
    }

    /// <summary>Короткая запись модуляции: «a=.5» / «m=.65/.15/.9 a=.45» / пусто.</summary>
    public static string ShortTint(Color c)
    {
        var sb = new StringBuilder(24);
        if (c.R < 0.999f || c.G < 0.999f || c.B < 0.999f)
            sb.Append("m=")
              .Append(c.R.ToString("0.##", CultureInfo.InvariantCulture)).Append('/')
              .Append(c.G.ToString("0.##", CultureInfo.InvariantCulture)).Append('/')
              .Append(c.B.ToString("0.##", CultureInfo.InvariantCulture));
        if (c.A < 0.999f)
        {
            if (sb.Length > 0)
                sb.Append(' ');
            sb.Append("a=").Append(c.A.ToString("0.##", CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }
}