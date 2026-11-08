using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.Simulation;

/// <summary>
/// Профиль ОТРАСЛИ зоны. Зона = условная отрасль (замысел владельца): у каждой
/// отрасли своё человекочитаемое имя, свой набор объектов стройки и ОДНА основная
/// работа (<see cref="PrimaryJob"/>), которую зона-бригадир обслуживает (§119 P0).
///
/// Список отраслей — единственный источник истины для UI: HUD строит кнопки
/// отраслей из <see cref="ZoneIndustryRegistry.All"/>, окно стройки фильтрует
/// объекты через <see cref="ZoneBuildCatalog"/>. Добавили отрасль здесь — UI
/// подхватил сам, править сцены/контроллеры не нужно.
/// </summary>
public sealed class ZoneIndustryProfile
{
    /// <summary>Отрасль (тип зоны).</summary>
    public ZoneKind Kind { get; }

    /// <summary>Стабильный строковый id для UI/сейва: "machine_building", "oil_refining".</summary>
    public string Id { get; }

    /// <summary>Человекочитаемое имя: «Станкостроение», «Нефтепереработка».</summary>
    public string Title { get; }

    /// <summary>Короткое пояснение для тултипа HUD.</summary>
    public string Description { get; }

    /// <summary>
    /// Основная работа зоны — ровно ОДИН бит. §119 P0: зона-бригадир диспатчит
    /// по одному типу работ, поэтому при создании зоны берётся именно этот бит.
    /// <see cref="JobTypeId.None"/> — отрасль не создаёт рабочих зон (жильё).
    ///
    /// AUDIT task-17 (M-5): раньше рядом жило поле <c>JobMask</c> («маска
    /// РАЗРЕШЁННЫХ работ отрасли», 2–3 бита). Оно не читалось ни одним
    /// потребителем и не попадало в <see cref="Zone.JobMask"/> — в зону всегда
    /// писался только <see cref="PrimaryJob"/>, а <see cref="ZoneCoverageMap"/>
    /// вообще отбрасывает зоны с ≠1 битом. То есть мультибитовое обещание было
    /// мёртвым и вводило в заблуждение. Поле удалено: единственный источник
    /// правды — PrimaryJob (см. <see cref="ZoneIndustryRegistry.PrimaryJobMask"/>).
    /// Расширение отрасли до нескольких работ = отдельная задача (coverage,
    /// диспатч, партиция), сейчас §119 запрещает.
    /// </summary>
    public JobTypeId PrimaryJob { get; }

    /// <summary>Объекты отрасли, которые попадают в окно строительства этой зоны.</summary>
    public IReadOnlyList<BuildingType> Buildings { get; }

    /// <summary>Иконка отрасли для HUD (ты можешь заменить на res://... или uid://...).</summary>
    public string IconPath { get; }

    /// <summary>Заявка на рабочих по умолчанию (создание зоны из черновика).</summary>
    public int DefaultWorkers { get; }

    public ZoneIndustryProfile(
        ZoneKind kind,
        string id,
        string title,
        string description,
        JobTypeId primaryJob,
        IReadOnlyList<BuildingType> buildings,
        string iconPath,
        int defaultWorkers)
    {
        Kind = kind;
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Title = string.IsNullOrEmpty(title) ? id : title;
        Description = description ?? string.Empty;
        PrimaryJob = primaryJob;
        Buildings = buildings ?? Array.Empty<BuildingType>();
        IconPath = iconPath ?? string.Empty;
        DefaultWorkers = Math.Clamp(defaultWorkers, 0, WorkZone.MaxDesiredWorkers);
    }

    /// <summary>Бит маски по типу работ (0 = None — бит не ставим).</summary>
    internal static ulong Bit(JobTypeId t) => t == JobTypeId.None ? 0UL : 1UL << (int)t;
}

/// <summary>
/// Реестр отраслей: ZoneKind ↔ строковый id ↔ профиль, плюс МИГРАЦИЯ старых
/// сейвов (.zones.json "kind":"work" без поля industry) по маске работ.
/// </summary>
public static class ZoneIndustryRegistry
{
    private static readonly List<ZoneIndustryProfile> _all = new(16);
    private static readonly Dictionary<ZoneKind, ZoneIndustryProfile> _byKind = new();
    private static readonly Dictionary<string, ZoneIndustryProfile> _byId =
        new(StringComparer.OrdinalIgnoreCase);

    static ZoneIndustryRegistry()
    {
        // Порядок = порядок кнопок в HUD и групп в окне стройки.
        Add(ZoneKind.Farm, "farm", "Сельское хозяйство",
            "Грядки: посев, уход, сбор урожая.",
            JobTypeId.Farming,
            new[] { BuildingType.Mill, BuildingType.WorkTable },
            "res://ui/assets/textures/icons/farming.png", 4);

        Add(ZoneKind.Forestry, "forestry", "Лесозаготовка",
            "Рубка леса и первичная распиловка.",
            JobTypeId.TreeChopping,
            new[] { BuildingType.Sawmill },
            "res://ui/assets/textures/workplaces/sawmill.png", 6);

        Add(ZoneKind.Mining, "mining", "Добыча",
            "Руда, камень, уголь: выработки и обогащение.",
            JobTypeId.OreMining,
            new[] { BuildingType.WorkTable },
            "res://ui/assets/textures/icons/Natural resource.png", 6);

        Add(ZoneKind.Metallurgy, "metallurgy", "Металлургия",
            "Чёрная металлургия: чугун и сталь.",
            JobTypeId.Production,
            new[] { BuildingType.Smelter, BuildingType.Anvil },
            "res://ui/assets/textures/workplaces/smelter.png", 8);

        Add(ZoneKind.NonFerrous, "non_ferrous", "Цветная металлургия",
            "Медь, свинец, цинк, алюминий: плавка и литьё.",
            JobTypeId.Production,
            new[] { BuildingType.Smelter, BuildingType.Anvil },
            "res://ui/assets/textures/icons/IndustrialIcon.png", 8);

        Add(ZoneKind.OilRefining, "oil_refining", "Нефтепереработка",
            "Перегонка нефти: керосин, мазут, смазки.",
            JobTypeId.Production,
            new[] { BuildingType.WorkTable },
            "res://ui/assets/textures/icons/humidity.png", 6);

        Add(ZoneKind.Chemistry, "chemistry", "Химия",
            "Кислоты, щёлочи, красители, реагенты.",
            JobTypeId.Production,
            new[] { BuildingType.Workbench, BuildingType.WorkTable },
            "res://ui/assets/textures/workplaces/workbench.png", 4);

        Add(ZoneKind.BuildingMaterials, "building_materials", "Стройматериалы",
            "Доска, кирпич, известь, стекло.",
            JobTypeId.Production,
            new[] { BuildingType.Sawmill, BuildingType.Mill, BuildingType.Workbench },
            "res://ui/assets/textures/icons/icon_construct.png", 6);

        Add(ZoneKind.Textile, "textile", "Текстиль",
            "Пряжа, ткань, одежда.",
            JobTypeId.Production,
            new[] { BuildingType.SpinningWheel, BuildingType.Loom },
            "res://ui/assets/textures/workplaces/loom.png", 6);

        Add(ZoneKind.MachineBuilding, "machine_building", "Станкостроение",
            "Станки, инструмент, оборудование.",
            JobTypeId.Production,
            new[] { BuildingType.Anvil, BuildingType.Workbench },
            "res://ui/assets/textures/workplaces/anvil.png", 8);

        Add(ZoneKind.Assembly, "assembly", "Сборка",
            "Машины, двигатели, готовые изделия.",
            JobTypeId.Production,
            new[] { BuildingType.AssemblyLine, BuildingType.Workbench },
            "res://ui/assets/textures/workplaces/assembly.png", 8);

        Add(ZoneKind.Warehouse, "warehouse", "Склад",
            "Приём, хранение и выдача грузов.",
            JobTypeId.StockpileHauling,
            Array.Empty<BuildingType>(),
            "res://ui/assets/textures/icons/logistic_zone.png", 4);

        Add(ZoneKind.Housing, "housing", "Жильё",
            "Кровати, скамьи, тумбочки для колонистов.",
            // «Жильё» как зона-бригадир: работы внутри — строительство (мебель/
            // постройки для колонистов). §119 требует ровно ОДИН бит работ на зону,
            // поэтому PrimaryJob — единственная работа зоны (см. M-5 в AUDIT_BUGS).
            JobTypeId.Construction,
            new[] { BuildingType.Bed, BuildingType.Bench, BuildingType.Nightstand },
            "res://ui/assets/textures/icons/people_icon.png", 4);
    }

    private static void Add(ZoneKind kind, string id, string title, string description,
        JobTypeId primaryJob, BuildingType[] buildings, string iconPath,
        int defaultWorkers)
    {
        var profile = new ZoneIndustryProfile(kind, id, title, description,
            primaryJob, buildings, iconPath, defaultWorkers);
        _all.Add(profile);
        _byKind[kind] = profile;
        _byKind[kind.NormalizeLegacy()] = profile;
        _byId[id] = profile;
    }

    /// <summary>Все отрасли (порядок = порядок кнопок/групп в UI).</summary>
    public static IReadOnlyList<ZoneIndustryProfile> All => _all;

    /// <summary>Отрасль по умолчанию (легаси-миграция).</summary>
    public static ZoneIndustryProfile Default => _byKind[ZoneKind.Warehouse];

    public static bool TryGet(ZoneKind kind, out ZoneIndustryProfile profile) =>
        _byKind.TryGetValue(kind.NormalizeLegacy(), out profile);

    /// <summary>Профиль отрасли; неизвестный/легаси Work → склад (не падаем).</summary>
    public static ZoneIndustryProfile Get(ZoneKind kind) =>
        _byKind.TryGetValue(kind.NormalizeLegacy(), out var p) ? p : Default;

    /// <summary>Профиль по строковому id ("machine_building"). Понимает легаси "farming"/"work".</summary>
    public static bool TryGet(string id, out ZoneIndustryProfile profile)
    {
        profile = null;
        if (string.IsNullOrWhiteSpace(id))
            return false;
        if (_byId.TryGetValue(id.Trim(), out profile))
            return true;
        // Легаси-идентификаторы окна стройки.
        if (id.Equals("farming", StringComparison.OrdinalIgnoreCase))
        {
            profile = _byKind[ZoneKind.Farm];
            return true;
        }
        if (id.Equals("work", StringComparison.OrdinalIgnoreCase))
        {
            profile = Default;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Строковый id зоны (HUD/сейв) → отрасль. Неизвестный id — fallback
    /// (по умолчанию склад), чтобы старые вызовы не падали.
    /// </summary>
    public static ZoneKind ResolveZoneKind(string zoneId,
        ZoneKind fallback = ZoneKind.Warehouse)
    {
        if (TryGet(zoneId, out var profile))
            return profile.Kind;
        return fallback.NormalizeLegacy();
    }

    public static string IdFor(ZoneKind kind) => Get(kind).Id;

    public static string TitleFor(ZoneKind kind) => Get(kind).Title;

    /// <summary>
    /// МИГРАЦИЯ старых сейвов: раньше зона была безымянной "Work" с одной маской
    /// работ. Отрасль восстанавливаем по биту работ; неизвестная маска — склад.
    /// </summary>
    public static ZoneKind FromLegacyJobMask(ulong jobMask)
    {
        if (jobMask == 0)
            return ZoneKind.Warehouse;
        if ((jobMask & ZoneIndustryProfile.Bit(JobTypeId.TreeChopping)) != 0)
            return ZoneKind.Forestry;
        if ((jobMask & ZoneIndustryProfile.Bit(JobTypeId.Mining)) != 0
            || (jobMask & ZoneIndustryProfile.Bit(JobTypeId.OreMining)) != 0)
            return ZoneKind.Mining;
        if ((jobMask & ZoneIndustryProfile.Bit(JobTypeId.StockpileHauling)) != 0)
            return ZoneKind.Warehouse;
        if ((jobMask & ZoneIndustryProfile.Bit(JobTypeId.Farming)) != 0
            || (jobMask & ZoneIndustryProfile.Bit(JobTypeId.Planting)) != 0
            || (jobMask & ZoneIndustryProfile.Bit(JobTypeId.Harvesting)) != 0)
        {
            // Farm-зона — это ТАЙЛЫ, а не радиус: рабочую зону с фермерской
            // маской трактуем как склад (старое поведение безымянной Work-зоны),
            // иначе получили бы Farm-kind без тайлов (пустая зона).
            return ZoneKind.Warehouse;
        }
        if ((jobMask & ZoneIndustryProfile.Bit(JobTypeId.Construction)) != 0
            || (jobMask & ZoneIndustryProfile.Bit(JobTypeId.BlueprintDelivery)) != 0
            || (jobMask & ZoneIndustryProfile.Bit(JobTypeId.ClearSite)) != 0)
            return ZoneKind.BuildingMaterials;
        // Легаси Work-зона с Crafting-маской (до task-20) и новая с Production —
        // обе означают «станок/цех», восстанавливаем как станкостроение.
        if ((jobMask & ZoneIndustryProfile.Bit(JobTypeId.Production)) != 0)
            return ZoneKind.MachineBuilding;
        if ((jobMask & ZoneIndustryProfile.Bit(JobTypeId.Crafting)) != 0)
            return ZoneKind.MachineBuilding;
        return ZoneKind.Warehouse;
    }

    /// <summary>
    /// Валидация маски отрасли для создания рабочей зоны: ровно один бит
    /// (<see cref="ZoneIndustryProfile.PrimaryJob"/>). 0 — отрасль без работ.
    /// </summary>
    public static ulong PrimaryJobMask(ZoneKind kind) =>
        ZoneIndustryProfile.Bit(Get(kind).PrimaryJob);

    /// <summary>
    /// Проверка C-2 (аудит): у КАЖДОЙ отрасли основная работа должна иметь
    /// зарегистрированный обработчик, иначе зона создаётся, но никогда не получает
    /// работ («зона-декорация»). Зовётся один раз после JobRegistry.Register всех
    /// хендлеров; возвращает число отраслей без обработчика.
    /// </summary>
    public static int ValidatePrimaryJobs(Func<JobTypeId, bool> hasHandler)
    {
        if (hasHandler == null)
            return 0;
        int missing = 0;
        for (int i = 0; i < _all.Count; i++)
        {
            ZoneIndustryProfile profile = _all[i];
            if (profile.PrimaryJob == JobTypeId.None)
                continue; // «зона без работ» — допустимо и явно описано профилем
            if (hasHandler(profile.PrimaryJob))
                continue;
            missing++;
            Godot.GD.PushError(
                $"[ZoneIndustry] C-2: у отрасли «{profile.Title}» PrimaryJob={profile.PrimaryJob}, " +
                "но обработчик не зарегистрирован — зона не получит работ.");
        }
        return missing;
    }
}
