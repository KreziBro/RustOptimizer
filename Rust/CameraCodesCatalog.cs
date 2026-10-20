using System.Collections.Generic;

namespace RustOptimizer.Rust;

public sealed record CameraCodeItem(string Monument, string Location, string Code, string Notes = "");

/// <summary>
/// Built-in offline catalog of all standard Rust monument CCTV camera identifiers.
/// </summary>
public static class CameraCodesCatalog
{
    public static readonly IReadOnlyList<CameraCodeItem> All = new List<CameraCodeItem>
    {
        // Город ученых (Outpost / Compound)
        new("Город учёных (Outpost)", "Центральная площадь / переработчик", "COMPOUNDSTREET", "Вид на улицу и зону переработчика"),
        new("Город учёных (Outpost)", "Музыкальная сцена", "COMPOUNDMUSIC", "Площадка со сценой"),
        new("Город учёных (Outpost)", "Охлаждающая башня / чилл-зона", "COMPOUNDCHILL", "Внутренний двор"),
        new("Город учёных (Outpost)", "Вертолётная площадка / магазин Air Wolf", "COMPOUNDCRUDE", "Покупка миникоптеров"),

        // Лагерь бандитов (Bandit Camp)
        new("Лагерь бандитов (Bandit Camp)", "Казино (Колесо фортуны)", "CASINO", "Внутри комнаты со ставками скрапа"),
        new("Лагерь бандитов (Bandit Camp)", "Оружейная лавка / причал", "TOWNWEAPONS", "Вход в оружейный магазин и переработчик"),

        // Купол (The Dome)
        new("Купол (The Dome)", "Верхушка купола (военные ящики)", "DOME1", "Контроль4 военных ящиков на вершине"),
        new("Купол (The Dome)", "Основание и заход на купол", "DOMETOP", "Обзор подходов к куполу"),

        // Аэродром (Airfield)
        new("Аэродром (Airfield)", "Гаражный комплекс / вход в подземку", "AIRFIELDGARAGE", "Вид на ангары и спуск к синей комнате"),

        // Малая нефтяная вышка (Small Oil Rig)
        new("Малая нефтевышка (Small Oil Rig)", "Причал (Док)", "OILRIG1DOCK", "Место причаливания лодок"),
        new("Малая нефтевышка (Small Oil Rig)", "Вертолётная площадка", "OILRIG1HELI", "Верхняя площадка с крейтом и посадкой тяжелых ученых"),
        new("Малая нефтевышка (Small Oil Rig)", "Уровень L1", "OILRIG1L1", "Первый ярус"),
        new("Малая нефтевышка (Small Oil Rig)", "Уровень L2", "OILRIG1L2", "Второй ярус"),
        new("Малая нефтевышка (Small Oil Rig)", "Уровень L3", "OILRIG1L3", "Третий ярус"),
        new("Малая нефтевышка (Small Oil Rig)", "Уровень L4 (Красная карта)", "OILRIG1L4", "Ярус с комнатой за красной картой"),

        // Большая нефтяная вышка (Large Oil Rig)
        new("Большая нефтевышка (Large Oil Rig)", "Причал (Док)", "OILRIG2DOCK", "Нижний причал лодок"),
        new("Большая нефтевышка (Large Oil Rig)", "Вертолётная площадка", "OILRIG2HELI", "Площадка с закрытым ящиком и чинуком"),
        new("Большая нефтевышка (Large Oil Rig)", "Уровень L1", "OILRIG2L1", "Первый ярус"),
        new("Большая нефтевышка (Large Oil Rig)", "Уровень L2", "OILRIG2L2", "Второй ярус"),
        new("Большая нефтевышка (Large Oil Rig)", "Уровень L3A", "OILRIG2L3A", "Третий ярус (сектор A)"),
        new("Большая нефтевышка (Large Oil Rig)", "Уровень L3B", "OILRIG2L3B", "Третий ярус (сектор B)"),
        new("Большая нефтевышка (Large Oil Rig)", "Уровень L4", "OILRIG2L4", "Четвёртый ярус"),
        new("Большая нефтевышка (Large Oil Rig)", "Уровень L5", "OILRIG2L5", "Пятый ярус"),
        new("Большая нефтевышка (Large Oil Rig)", "Уровень L6A", "OILRIG2L6A", "Шестой ярус (сектор A)"),
        new("Большая нефтевышка (Large Oil Rig)", "Уровень L6B", "OILRIG2L6B", "Шестой ярус (сектор B)"),
        new("Большая нефтевышка (Large Oil Rig)", "Уровень L6C", "OILRIG2L6C", "Шестой ярус (сектор C)"),
        new("Большая нефтевышка (Large Oil Rig)", "Уровень L6D", "OILRIG2L6D", "Шестой ярус (сектор D)"),
        new("Большая нефтевышка (Large Oil Rig)", "Выхлопная труба (Факел)", "OILRIG2EXHAUST", "Вершина факела нефтевышки"),

        // Космодром (Launch Site)
        new("Космодром (Launch Site)", "Красное здание — крыша (Элитные ящики)", "MAINBUILDING5", "Площадка на крыше главного здания"),

        // Военные туннели (Military Tunnels)
        new("Военные туннели (Military Tunnels)", "Вход в шахту", "MILTUNNEL", "Проверка наличия учёных и игроков"),

        // Ядерная ракетная шахта (Missile Silo)
        new("Ракетная шахта (Missile Silo)", "Входная палуба на поверхности", "SILOEXIT1", "Въездные ворота на поверхности"),
        new("Ракетная шахта (Missile Silo)", "Выход из шахты", "SILOEXIT2", "Точка выхода из бункера"),
        new("Ракетная шахта (Missile Oil/Missile)", "Отсек ракеты", "SILOMISSILE", "Центральная шахта с боеголовкой"),
        new("Ракетная шахта (Missile Silo)", "Погрузочная площадка", "SILOSHIPPING", "Складской сектор"),
        new("Ракетная шахта (Missile Silo)", "Башня", "SILOTOWER", "Вышка наблюдения"),

        // Паромный терминал (Ferry Terminal)
        new("Паромный терминал (Ferry Terminal)", "Причал парома", "FERRYDOCK", "Зона швартовки"),
        new("Паромный терминал (Ferry Terminal)", "Парковка", "FERRYPARKING", "Въездная парковка"),
        new("Паромный терминал (Ferry Terminal)", "Зона логистики", "FERRYLOGISTICS", "Контейнерная площадка"),
        new("Паромный терминал (Ferry Terminal)", "Мастерская / переработчик", "FERRYUTILITIES", "Зона переработчика"),

        // Рэдтаун (Radtown)
        new("Рэдтаун (Radtown)", "Апартаменты", "RADTOWNAPARTMENTS", "Жилой блок"),
        new("Рэдтаун (Radtown)", "Малый склад", "RADTOWNSSMALL", "Складская зона"),
        new("Рэдтаун (Radtown)", "Отстойник / Бочки", "RADTOWNSBL", "Промышленная зона"),

        // Подводные лаборатории (Underwater Labs — имеют 4-значный суффикс на карте)
        new("Подводная лаборатория (Underwater Lab)", "Синяя ключ-карта", "LABBLUE****", "В игре добавляется 4-значный код лаборатории"),
        new("Подводная лаборатория (Underwater Lab)", "Зелёная ключ-карта", "LABGREEN****", "В игре добавляется 4-значный код лаборатории"),
        new("Подводная лаборатория (Underwater Lab)", "Красная ключ-карта", "LABRED****", "В игре добавляется 4-значный код лаборатории"),
        new("Подводная лаборатория (Underwater Lab)", "Технический отсек", "LABTECH****", "В игре добавляется 4-значный код лаборатории")
    };
}
