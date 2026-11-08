using Godot;
using System;
using System.Collections.Generic;

namespace Game.UI;

/// <summary>
/// Тени мелких штук (предметы на земле, урожай, склад) — как у Syx ShadowBatch:
/// тот же силуэт рисуется второй раз, чёрным и растянутым вдоль солнца.
/// Геометрия пересчитывается каждый кадр в _Process в соответствии с текущим положением солнца,
/// а видимость и отсечение по камере динамически обновляются без задержек.
/// Поддерживает раздельные источники (земля, урожай, склад), исключая их взаимное вытеснение.
/// </summary>
public partial class ItemShadowRenderer : Node2D
{
    public const int MaxShadows = 16384;
    // Мелочь низкая: зерно/бревно/росток почти не отбрасывают.
    // Короткая тень вместо полноценной: длина солнца × 0.35, ширина × 0.5.
    private const float ItemLengthScale = 0.35f;
    private const float ItemWidthScale = 0.5f;
    private const float ItemAlphaScale = 0.5f;

    public enum ShadowSource
    {
        Default = 0,
        Ground = 1,
        Crop = 2,
        Stockpile = 3
    }

    public readonly struct ShadowSpot
    {
        public readonly float X;
        public readonly float Y;
        public readonly float SizePx;

        public ShadowSpot(float x, float y, float sizePx)
        {
            X = x;
            Y = y;
            SizePx = sizePx;
        }
    }

    private MultiMeshInstance2D _instance;
    private MultiMesh _multiMesh;
    private readonly float[] _buffer = new float[MaxShadows * 8];

    private readonly List<ShadowSpot>[] _sourceSpots = new List<ShadowSpot>[4]
    {
        new List<ShadowSpot>(4096),
        new List<ShadowSpot>(4096),
        new List<ShadowSpot>(4096),
        new List<ShadowSpot>(4096)
    };

    private DayNightCycle.SunState _sun;
    private bool _shadowOn;
    private Vector2 _viewCenter;
    private Vector2 _viewHalf;
    private bool _hasView;

    // FIX (bughunt 14-5): раньше полный буфер MaxShadows×8 float (16384×8 = 512 КБ)
    // заливался в MultiMesh.Buffer каждый кадр, даже если ничего не менялось.
    // Теперь заливка идёт только при реальном изменении входа:
    //   • _bufferDirty — поменялся набор спотов (Clear/Add/Set/Push);
    //   • !_bufferValid — буфер ещё не собран либо был скрыт (ночь/зум) и требует пересборки;
    //   • GeometryChanged() — уплыло солнце (Dir/LengthPx/WidthScale), видимость или окно
    //     куллинга камеры. Alpha в геометрию не входит: она живёт в Modulate и обновляется
    //     отдельно, поэтому закат/рассвет не «замораживают» цвет и тени не застывают.
    private bool _bufferDirty;
    private bool _bufferValid;
    private bool _appliedShadowOn;
    private Vector2 _appliedDir;
    private float _appliedLen;
    private float _appliedWidth;
    private Vector2 _appliedViewCenter;
    private Vector2 _appliedViewHalf;
    private bool _appliedHasView;
    private float _appliedAlpha;

    public const float ItemShadowZoomCutoff = 0.28f;

    public override void _Ready()
    {
        ZIndex = 3; // Под самими предметами (Z 5-6), над землёй.
        // R2-02: AABB охватывает мир с запасом (-262144..+262144 px = ±4096 тайлов), чтобы тени предметов за кромкой 1024 не отсекались.
        var mapAabb = new Aabb(new Godot.Vector3(-262144f, -262144f, -1000f), new Godot.Vector3(524288f, 524288f, 2000f));

        _multiMesh = new MultiMesh
        {
            Mesh = new QuadMesh { Size = new Vector2(1f, 1f) },
            TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,
            UseColors = false,
            UseCustomData = false,
            InstanceCount = MaxShadows,
            VisibleInstanceCount = 0,
            CustomAabb = mapAabb
        };
        _instance = new MultiMeshInstance2D
        {
            Name = "ItemShadows",
            Multimesh = _multiMesh,
            Modulate = new Color(0f, 0f, 0f, 0f)
        };
        AddChild(_instance);
        Visible = false;
    }

    /// <summary>Солнце на тик (O(1)). Звать из Main рядом с остальными тенями.</summary>
    public void SetSun(DayNightCycle.SunState sun)
    {
        _sun = sun;
        var cam = GetViewport()?.GetCamera2D();
        float zoom = cam?.Zoom.X ?? 1f;
        bool zoomAllowed = zoom >= ItemShadowZoomCutoff;
        _shadowOn = zoomAllowed && sun.Alpha > 0.004f && sun.LengthPx >= 0.5f;
        UpdateView();
    }

    public void ClearSourceSpots(ShadowSource source)
    {
        int idx = (int)source;
        if (idx >= 0 && idx < _sourceSpots.Length)
        {
            var list = _sourceSpots[idx];
            // FIX (bughunt 14-5): dirty только при фактическом изменении — BeginFrame
            // чистит Default каждый кадр, и безусловный флаг убил бы весь смысл кэша.
            if (list.Count > 0)
            {
                list.Clear();
                _bufferDirty = true;
            }
        }
    }

    public void AddSourceSpots(ShadowSource source, System.Numerics.Vector2[] positions, int count, float sizePx)
    {
        if (positions == null || count <= 0) return;
        int idx = (int)source;
        if (idx < 0 || idx >= _sourceSpots.Length) return;

        var list = _sourceSpots[idx];
        int limit = Math.Min(count, positions.Length);
        for (int i = 0; i < limit; i++)
        {
            list.Add(new ShadowSpot(positions[i].X, positions[i].Y, sizePx));
        }
        // FIX (bughunt 14-5): появились новые споты — буфер надо собрать заново.
        if (limit > 0)
            _bufferDirty = true;
    }

    public void SetSourceSpots(ShadowSource source, System.Numerics.Vector2[] positions, int count, float sizePx)
    {
        ClearSourceSpots(source);
        AddSourceSpots(source, positions, count, sizePx);
    }

    /// <summary>Начать кадр: сбросить аккумулятор Default-спотов (обратная совместимость).</summary>
    public void BeginFrame()
    {
        ClearSourceSpots(ShadowSource.Default);
    }

    /// <summary>Слить аккумулятор на GPU немедленно (обратная совместимость).</summary>
    public void Flush()
    {
        UpdateAndRender();
    }

    /// <summary>
    /// Добавить тени из позиций предметов (уже в мировых px, центр спрайта).
    /// </summary>
    public int PushSpots(System.Numerics.Vector2[] positions, int count, float sizePx)
    {
        if (positions == null || count <= 0) return 0;
        AddSourceSpots(ShadowSource.Default, positions, count, sizePx);
        return Math.Min(count, positions.Length);
    }

    public override void _Process(double delta)
    {
        UpdateAndRender();
    }

    private void UpdateAndRender()
    {
        UpdateView();

        if (!_shadowOn || _multiMesh == null)
        {
            if (_multiMesh != null) _multiMesh.VisibleInstanceCount = 0;
            if (_instance != null) _instance.Visible = false;
            Visible = false;
            // FIX (bughunt 14-5): после скрытия (ночь/сильный зум) буфер устарел —
            // при возврате солнца соберём его заново, иначе тени «застынут» на старом кадре.
            _bufferValid = false;
            return;
        }

        int visibleCount;
        // FIX (bughunt 14-5): тяжёлая заливка 512 КБ только по dirty-флагу/изменению входа.
        if (_bufferDirty || !_bufferValid || GeometryChanged())
        {
            float len = _sun.LengthPx * ItemLengthScale;
            float hx = _sun.Dir.X * len * 0.5f;
            float hy = _sun.Dir.Y * len * 0.5f;
            float px = -_sun.Dir.Y;
            float py = _sun.Dir.X;

            visibleCount = 0;

            for (int s = 0; s < _sourceSpots.Length && visibleCount < MaxShadows; s++)
            {
                var list = _sourceSpots[s];
                if (list == null) continue;
                int c = list.Count;
                for (int i = 0; i < c && visibleCount < MaxShadows; i++)
                {
                    var spot = list[i];
                    float sizePx = spot.SizePx;
                    float ax = spot.X;
                    float ay = spot.Y + sizePx * 0.25f; // якорь у низа предмета

                    if (!InView(ax, ay))
                        continue;

                    // FIX (bughunt 14-1): меш здесь — QuadMesh 1×1 (см. _multiMesh.Mesh), поэтому
                    // базис задаётся В ПИКСЕЛЯХ. Раньше базис делился на sizePx (формула для
                    // квада размером sizePx), из-за чего тень выходила ~1.4×0.5 px вместо ~60 px,
                    // то есть практически невидимой.
                    float halfLen = len * 0.5f + sizePx * 0.25f;                    // вдоль солнца
                    float halfW = _sun.WidthScale * sizePx * 0.5f * ItemWidthScale * 0.5f; // поперёк

                    int idx = visibleCount * 8;
                    // X-ось: длина тени вдоль направления солнца (квад 1×1 → *2 даёт полный размер).
                    _buffer[idx + 0] = _sun.Dir.X * halfLen * 2f;
                    _buffer[idx + 1] = _sun.Dir.Y * halfLen * 2f;
                    _buffer[idx + 2] = 0f;
                    _buffer[idx + 3] = ax + hx;
                    // Y-ось: ширина тени.
                    _buffer[idx + 4] = px * halfW * 2f;
                    _buffer[idx + 5] = py * halfW * 2f;
                    _buffer[idx + 6] = 0f;
                    _buffer[idx + 7] = ay + hy;

                    visibleCount++;
                }
            }

            _multiMesh.VisibleInstanceCount = visibleCount;
            if (visibleCount > 0)
                _multiMesh.Buffer = _buffer;

            // Запоминаем состояние, под которое собран буфер (с ним сверяется GeometryChanged).
            _appliedShadowOn = _shadowOn;
            _appliedDir = _sun.Dir;
            _appliedLen = _sun.LengthPx;
            _appliedWidth = _sun.WidthScale;
            _appliedViewCenter = _viewCenter;
            _appliedViewHalf = _viewHalf;
            _appliedHasView = _hasView;
            _bufferValid = true;
            _bufferDirty = false;
        }
        else
        {
            // Ничего не изменилось: буфер уже на GPU, состав инстансов тот же.
            visibleCount = _multiMesh.VisibleInstanceCount;
        }

        if (visibleCount > 0)
        {
            Visible = true;
            if (_instance != null)
            {
                bool wasHidden = !_instance.Visible;
                _instance.Visible = true;
                // Alpha меняется непрерывно (день/ночь), но в геометрию не входит —
                // обновляем только Modulate, без пересборки буфера.
                if (wasHidden || Math.Abs(_sun.Alpha - _appliedAlpha) > 1e-4f)
                {
                    _instance.Modulate = new Color(0f, 0f, 0f, _sun.Alpha * ItemAlphaScale);
                    _appliedAlpha = _sun.Alpha;
                }
            }
        }
        else
        {
            Visible = false;
            if (_instance != null) _instance.Visible = false;
        }
    }

    /// <summary>
    /// FIX (bughunt 14-5): изменилось ли всё, из чего собирается буфер (кроме спотов —
    /// за них отвечает _bufferDirty). Пороги — субпиксельные: дрейф направления солнца
    /// накапливается и рано или поздно пересобирает буфер, поэтому застыть тени не могут.
    /// </summary>
    private bool GeometryChanged()
    {
        if (_shadowOn != _appliedShadowOn)
            return true;
        if (Math.Abs(_sun.LengthPx - _appliedLen) > 0.01f)
            return true;
        if (Math.Abs(_sun.WidthScale - _appliedWidth) > 1e-5f)
            return true;

        Vector2 dd = _sun.Dir - _appliedDir;
        if (dd.X * dd.X + dd.Y * dd.Y > 1e-8f) // |Δdir| > 1e-4 — недостижимо глазу, но не «заморозка»
            return true;

        if (_hasView != _appliedHasView)
            return true;
        if (_hasView)
        {
            // Сдвиг камеры/зума меняет результат куллинга InView — 0.5 px запаса,
            // чтобы дрожание субпикселя не пересобирало буфер каждый кадр.
            Vector2 dc = _viewCenter - _appliedViewCenter;
            if (dc.X * dc.X + dc.Y * dc.Y > 0.25f)
                return true;
            Vector2 dh = _viewHalf - _appliedViewHalf;
            if (dh.X * dh.X + dh.Y * dh.Y > 0.25f)
                return true;
        }
        return false;
    }

    private void UpdateView()
    {
        var cam = GetViewport()?.GetCamera2D();
        if (cam == null || cam.Zoom.X <= 0f || cam.Zoom.Y <= 0f)
        {
            _hasView = false;
            return;
        }
        Vector2 vp = GetViewportRect().Size;
        _viewCenter = cam.GetScreenCenterPosition();
        _viewHalf = new Vector2(
            vp.X / cam.Zoom.X * 0.5f + DayNightCycle.MaxShadowLengthPx,
            vp.Y / cam.Zoom.Y * 0.5f + DayNightCycle.MaxShadowLengthPx);
        _hasView = true;
    }

    private bool InView(float x, float y)
    {
        if (!_hasView)
            return true;
        return Math.Abs(x - _viewCenter.X) <= _viewHalf.X
            && Math.Abs(y - _viewCenter.Y) <= _viewHalf.Y;
    }
}
