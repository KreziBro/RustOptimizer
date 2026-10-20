using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using RustOptimizer.Core;

namespace RustOptimizer.Rust;

public sealed record ConfigNode(string Key, string Value);

public sealed record InGameTweakDefinition(
    string Id,
    string Group,
    string Name,
    string Description,
    ConfigNode[] ConfigParams,
    ConfigNode[] LaunchParams = null);

public sealed record GraphicsLevelOption(int Level, string Label, ConfigNode[] ConfigParams, ConfigNode[] LaunchParams = null);

public sealed record GraphicsCategoryDefinition(
    string Id,
    string Name,
    string Description,
    GraphicsLevelOption[] Levels);

public sealed record GraphicsPresetDefinition(string Id, string Name, Dictionary<string, int> Values);

public sealed class BindEntry
{
    public string Key { get; set; } = "";
    public string Command { get; set; } = "";
    public string RawLine { get; set; } = "";
}

public sealed record ReadyBindTemplate(string Title, string DefaultKey, string Command, string Description);

/// <summary>
/// Full local implementation of Rust in-game tweaks (Params.Tweaks), graphics settings & presets (Params.Graphics),
/// and keys.cfg bind management without requiring the remote API server.
/// </summary>
public static class RustGameTweaksAndGraphics
{
    public static readonly IReadOnlyList<InGameTweakDefinition> InGameTweaks = new List<InGameTweakDefinition>
    {
        // Рекомендуемые
        new("trash_settings", "Рекомендуемые",
            "Отключить все паразитные параметры",
            "Отключает ненужные настройки, которые затрудняют геймплей или снижают производительность (блики, размытия, подсказки, анимации и т.д.).",
            new ConfigNode[]
            {
                new("client.headbob", "\"False\""),
                new("client.hurtpunch", "\"False\""),
                new("global.showblood", "\"False\""),
                new("global.censorrecordings", "\"False\""),
                new("shoutcaststreamer.allowinternetstreams", "\"False\""),
                new("effects.hurtoverlay", "\"False\""),
                new("effects.hurtoverleyapplylighting", "\"False\""),
                new("effects.bloom", "\"False\""),
                new("effects.shafts", "\"False\""),
                new("effects.lensdirt", "\"False\""),
                new("graphics.branding", "\"False\""),
                new("gametip.showgametips", "\"False\""),
                new("graphicssettings.particleraycastbudget", "\"0\""),
                new("graphicssettings.pixellightcount", "\"0\""),
                new("ui.showbeltbarbinds", "\"False\""),
                new("water.quality", "\"0\""),
                new("effects.vignet", "\"False\""),
                new("global.processmidiinput", "\"False\""),
                new("player.cold_breath", "\"False\""),
                new("client.hascompletedtutorial", "\"True\""),
                new("render.instanced_rendering", "\"0\""),
                new("graphicssettings.billboardsfacecameraposition", "\"False\"")
            }),
        new("legs_vision", "Рекомендуемые",
            "Отключить отображение ног",
            "Отключает отображение ног персонажа от первого лица, помогая целиться вниз.",
            new ConfigNode[] { new("legs.enablelegs", "\"False\"") }),
        new("shake_cam", "Рекомендуемые",
            "Уменьшить тряску камеры",
            "Минимизирует тряску камеры при беге, стрельбе, взрывах и получении урона.",
            new ConfigNode[]
            {
                new("client.clampscreenshake", "\"True\""),
                new("client.allowcameratiltondpv", "\"False\""),
                new("client.headbob", "\"False\""),
                new("client.hurtpunch", "\"False\"")
            }),
        new("cross_on_the_threes", "Рекомендуемые",
            "Улучшить видимость крестиков на деревьях",
            "Меняет цвет отображения маркеров на деревьях на более контрастный.",
            new ConfigNode[] { new("accessibility.treemarkercolor", "\"2\"") }),
        new("disable_secure_occlusion", "Рекомендуемые",
            "Отключить безопасный режим отсечения окклюзии",
            "Проверяет, что безопасный режим отсечения окклюзии (culling.safemode), сильно снижающий FPS, отключен.",
            new ConfigNode[] { new("culling.safemode", "\"False\"") }),
        new("admin_tp", "Рекомендуемые",
            "Админский телепорт по маркеру",
            "При наличии админки автоматически телепортирует игрока в точку установки маркера на карте.",
            null,
            new ConfigNode[] { new("global.enable_marker_teleport", "\"True\"") }),

        // Визуальные эффекты
        new("disable_the_wreckage", "Визуальные эффекты",
            "Полностью отключить обломки",
            "Полностью отключает обломки при разрушении сооружений и объектов (звуки разрушения сохраняются).",
            new ConfigNode[]
            {
                new("effects.maxgibdist", "\"150\""),
                new("effects.maxgibs", "\"0\""),
                new("effects.maxgiblife", "\"0\""),
                new("effects.mingiblife", "\"0\"")
            }),
        new("disable_eyes_animation", "Визуальные эффекты",
            "Отключить анимацию глаз игроков",
            "Полностью отключает анимацию движения и моргания глаз у всех персонажей.",
            null,
            new ConfigNode[]
            {
                new("player.eye_blinking", "\"False\""),
                new("player.eye_movement", "\"False\"")
            }),
        new("disable_leg_deformity", "Визуальные эффекты",
            "Отключить деформацию ног (Foot IK)",
            "Возвращает старую анимацию ног персонажа, которая не подстраивается под рельеф. Помогает в PvP и немного снижает нагрузку.",
            new ConfigNode[] { new("player.footik", "\"False\"") }),
        new("disable_stroboscope", "Визуальные эффекты",
            "Отключить стробоскопы",
            "Полностью отключает стробоскопы в игре, оставляя только звук их работы. Повышает FPS рядом со стробоскопами.",
            new ConfigNode[] { new("strobelight.forceoff", "\"True\"") }),
        new("fast_head_rotate", "Визуальные эффекты",
            "Ускорить поворот головы через ALT",
            "Голова персонажа быстрее возвращается в исходное состояние при отпускании клавиши ALT.",
            null,
            new ConfigNode[]
            {
                new("client.headlerp", "\"10\""),
                new("headlerp_inertia", "\"0\"")
            }),
        new("small_item_in_hand", "Визуальные эффекты",
            "Уменьшить предметы в руках",
            "Делает оружие и предметы в руках персонажа меньше (graphics.vm_fov_scale False).",
            new ConfigNode[] { new("graphics.vm_fov_scale", "\"False\"") }),
        new("glass_reflection", "Визуальные эффекты",
            "Отключить отражение зеркал и стёкол",
            "Отключает планарные отражения на зеркалах и стеклах (reflection.planarreflections False).",
            new ConfigNode[] { new("reflection.planarreflections", "\"False\"") }),

        // Интерфейс
        new("return_events_textannounce", "Интерфейс",
            "Вернуть текстовые уведомления об ивентах",
            "Возвращает всплывающие уведомления о старте ивентов (карго, патрульный вертолет, чинук и т.п.).",
            new ConfigNode[] { new("ui.monumentnotificationtoasts", "\"True\"") }),
        new("disable_craft_delay", "Интерфейс",
            "Убрать задержку в меню автокрафта",
            "Отключает провисание интерфейса при попытке скрафтить предмет через меню быстрого крафта.",
            new ConfigNode[] { new("inventory.quickcraftdelay", "\"0\"") }),
        new("smalltime_bag_unclaim", "Интерфейс",
            "Снизить задержку при удалении спальников",
            "Позволяет практически мгновенно удалять спальники на карте (0.1 сек).",
            new ConfigNode[] { new("client.bag_unclaim_duration", "\"0.1\"") }),
        new("add_map_info", "Интерфейс",
            "Добавить информацию о карте в меню F8",
            "Добавляет информацию о типе, размере и сиде карты в меню отладки F8.",
            new ConfigNode[] { new("debug.showworldinfoinperformancereadout", "\"True\"") }),
        new("disable_show_errors", "Интерфейс",
            "Отключить отображение клиентских ошибок",
            "Полностью отключает красные ошибки в углу экрана (console.erroroverlay False).",
            new ConfigNode[] { new("console.erroroverlay", "\"False\"") }),
        new("add_admin_gestures", "Интерфейс",
            "Добавить админские жесты в игровое меню",
            "Добавляет скрытые кинематографические анимации в меню настройки жестов.",
            new ConfigNode[] { new("gesturecollection.showadmincinematicgesturesinbindings", "\"True\"") }),
        new("convenient_skin_sorting", "Интерфейс",
            "Удобная сортировка скинов",
            "Сортирует скины в меню крафта по дате последнего использования.",
            new ConfigNode[] { new("client.sortskinsrecentlyused", "\"True\"") }),
        new("enlarged_console", "Интерфейс",
            "Увеличенная консоль",
            "Увеличивает размер шрифта в консоли F1 до 16.",
            new ConfigNode[] { new("global.consolescale", "\"16\"") }),

        // Экспериментальные
        new("server_hitmarks", "Экспериментальные",
            "Серверный хитмаркер",
            "Хитмаркер отображается только когда сервер подтверждает регистрацию попадания (избавляет от инвалидов).",
            null,
            new ConfigNode[] { new("hitnotify.notification_level", "\"2\"") }),
        new("old_announce_for_take_item", "Экспериментальные",
            "Старые уведомления о подборе предметов",
            "Возвращает старый способ отображения подобранных предметов: каждый предмет отображается отдельно.",
            null,
            new ConfigNode[]
            {
                new("global.showitempickupnotices", "\"1\""),
                new("global.showitemcountsonpickup", "\"False\""),
                new("global.usesingleitempickupnotice", "\"False\"")
            }),
        new("small_time_use_menu", "Экспериментальные",
            "Снизить задержку вызова радиального меню",
            "Ускоряет появление радиального меню при зажатии кнопки взаимодействия E (input.holdtime 0.15).",
            new ConfigNode[] { new("input.holdtime", "\"0.15\"") }),
        new("left_handed", "Экспериментальные",
            "Режим левой руки",
            "Переносит оружие и предметы в левую руку персонажа (graphics.vm_horizontal_flip True).",
            new ConfigNode[] { new("graphics.vm_horizontal_flip", "\"True\"") })
    };

    public static readonly IReadOnlyList<GraphicsCategoryDefinition> GraphicsCategories = new List<GraphicsCategoryDefinition>
    {
        new("shadows", "Качество теней",
            "Производительность — максимум FPS. Незаметные тени — мягкие тени без потери FPS. Мягкие тени — баланс. Четкие тени — детализированные тени.",
            new GraphicsLevelOption[]
            {
                new(0, "0 — Производительность",
                    new ConfigNode[] { new("graphics.shadowlights", "\"1\""), new("graphicssettings.shadowqualitypreset", "\"0\"") },
                    new ConfigNode[] { new("graphics.shadowmode", "\"1\"") }),
                new(1, "1 — Незаметные тени",
                    new ConfigNode[] { new("graphics.shadowlights", "\"1\""), new("graphicssettings.shadowqualitypreset", "\"0\"") },
                    new ConfigNode[] { new("graphics.shadowmode", "\"2\"") }),
                new(2, "2 — Мягкие тени",
                    new ConfigNode[] { new("graphics.shadowlights", "\"1\""), new("graphicssettings.shadowqualitypreset", "\"2\"") },
                    new ConfigNode[] { new("graphics.shadowmode", "\"2\"") }),
                new(3, "3 — Четкие тени",
                    new ConfigNode[] { new("graphics.shadowlights", "\"1\""), new("graphicssettings.shadowqualitypreset", "\"2\"") },
                    new ConfigNode[] { new("graphics.shadowmode", "\"1\"") })
            }),
        new("textures", "Качество текстур",
            "Управляет качеством отрисовки текстур предметов, построек, скал и скинов. Требовательно к видеопамяти (при <4 ГБ VRAM выбирайте Картошка или Низкое).",
            new GraphicsLevelOption[]
            {
                new(0, "0 — Картошка", new ConfigNode[]
                {
                    new("graphicssettings.globaltexturemipmaplimit", "\"3\""),
                    new("graphics.af", "\"1\""),
                    new("graphics.lodbias", "\"0.5\""),
                    new("graphics.shaderlod", "\"1\""),
                    new("graphicssettings.anisotropicfiltering", "\"0\""),
                    new("mesh.quality", "\"0\""),
                    new("terrain.quality", "\"100\"")
                }),
                new(1, "1 — Низкое", new ConfigNode[]
                {
                    new("graphicssettings.globaltexturemipmaplimit", "\"2\""),
                    new("graphics.af", "\"2\""),
                    new("graphics.lodbias", "\"0.6\""),
                    new("graphics.shaderlod", "\"2\""),
                    new("graphicssettings.anisotropicfiltering", "\"1\""),
                    new("mesh.quality", "\"30\""),
                    new("terrain.quality", "\"100\"")
                }),
                new(2, "2 — Среднее", new ConfigNode[]
                {
                    new("graphicssettings.globaltexturemipmaplimit", "\"1\""),
                    new("graphics.af", "\"8\""),
                    new("graphics.lodbias", "\"1\""),
                    new("graphics.shaderlod", "\"3\""),
                    new("graphicssettings.anisotropicfiltering", "\"1\""),
                    new("mesh.quality", "\"150\""),
                    new("terrain.quality", "\"100\"")
                }),
                new(3, "3 — Высокое", new ConfigNode[]
                {
                    new("graphicssettings.globaltexturemipmaplimit", "\"0\""),
                    new("graphics.af", "\"8\""),
                    new("graphics.lodbias", "\"1\""),
                    new("graphics.shaderlod", "\"5\""),
                    new("graphicssettings.anisotropicfiltering", "\"0\""),
                    new("mesh.quality", "\"150\""),
                    new("terrain.quality", "\"100\"")
                })
            }),
        new("lighting", "Качество освещения",
            "Управляет глобальным затенением (Ambient Occlusion) и контактными тенями.",
            new GraphicsLevelOption[]
            {
                new(0, "0 — Низкое", new ConfigNode[] { new("graphics.contactshadows", "\"False\""), new("effects.ao", "\"False\"") }),
                new(1, "1 — Среднее", new ConfigNode[] { new("graphics.contactshadows", "\"False\""), new("effects.ao", "\"True\"") }),
                new(2, "2 — Высокое", new ConfigNode[] { new("graphics.contactshadows", "\"True\""), new("effects.ao", "\"True\"") })
            }),
        new("trees", "Качество деревьев",
            "Управляет детализацией деревьев и их 3D-моделей на расстоянии.",
            new GraphicsLevelOption[]
            {
                new(0, "0 — Низкое", new ConfigNode[] { new("tree.meshes", "\"10\""), new("tree.quality", "\"30\"") }),
                new(1, "1 — Среднее", new ConfigNode[] { new("tree.meshes", "\"50\""), new("tree.quality", "\"100\"") }),
                new(2, "2 — Высокое", new ConfigNode[] { new("tree.meshes", "\"100\""), new("tree.quality", "\"150\"") }),
                new(3, "3 — Запредельное",
                    new ConfigNode[] { new("tree.meshes", "\"100\""), new("tree.quality", "\"500\"") },
                    new ConfigNode[] { new("tree.quality", "\"500\"") })
            }),
        new("reflections_on_the_water", "Отражения на воде",
            "Отражения на воде делают картинку приятнее, но усложняют PvP и снижают FPS.",
            new GraphicsLevelOption[]
            {
                new(0, "0 — Отключены", new ConfigNode[] { new("water.quality", "\"0\""), new("water.reflections", "\"0\"") }),
                new(1, "1 — Средние", new ConfigNode[] { new("water.quality", "\"0\""), new("water.reflections", "\"1\"") }),
                new(2, "2 — Высокие", new ConfigNode[] { new("water.quality", "\"0\""), new("water.reflections", "\"2\"") })
            }),
        new("grass", "Качество травы",
            "Чем ниже качество травы и теней на ней, тем проще заметить противников и предметы.",
            new GraphicsLevelOption[]
            {
                new(0, "0 — Комбат", new ConfigNode[] { new("grass.displacement", "\"True\""), new("grass.quality", "\"0\""), new("graphics.grassshadows", "\"False\"") }),
                new(1, "1 — Среднее", new ConfigNode[] { new("grass.displacement", "\"True\""), new("grass.quality", "\"50\""), new("graphics.grassshadows", "\"False\"") }),
                new(2, "2 — Высокое", new ConfigNode[] { new("grass.displacement", "\"True\""), new("grass.quality", "\"100\""), new("graphics.grassshadows", "\"True\"") })
            }),
        new("clouds", "Качество облаков",
            "Управляет объемными облаками (graphics.volumetric_clouds).",
            new GraphicsLevelOption[]
            {
                new(0, "0 — Минимальное", new ConfigNode[] { new("graphics.volumetric_clouds", "\"0\"") }),
                new(1, "1 — Оптимальное", new ConfigNode[] { new("graphics.volumetric_clouds", "\"1\"") }),
                new(2, "2 — Высокое", new ConfigNode[] { new("graphics.volumetric_clouds", "\"4\"") })
            }),
        new("smoothing", "Сглаживание",
            "Управляет сглаживанием (FXAA/SMAA/TSSAA) и резкостью изображения.",
            new GraphicsLevelOption[]
            {
                new(0, "0 — Отключено", new ConfigNode[] { new("effects.sharpen", "\"True\""), new("effects.antialiasing", "\"0\"") }),
                new(1, "1 — Слабое (FXAA)", new ConfigNode[] { new("effects.sharpen", "\"True\""), new("effects.antialiasing", "\"1\"") }),
                new(2, "2 — Оптимальное (SMAA)", new ConfigNode[] { new("effects.sharpen", "\"True\""), new("effects.antialiasing", "\"2\"") }),
                new(3, "3 — Сильное (TSSAA)", new ConfigNode[] { new("effects.sharpen", "\"True\""), new("effects.antialiasing", "\"3\"") })
            })
    };

    public static readonly IReadOnlyList<GraphicsPresetDefinition> GraphicsPresets = new List<GraphicsPresetDefinition>
    {
        new("efficiency", "Производительность", new Dictionary<string, int>
        {
            ["shadows"] = 0, ["textures"] = 0, ["lighting"] = 0, ["trees"] = 0,
            ["reflections_on_the_water"] = 0, ["grass"] = 0, ["clouds"] = 0, ["smoothing"] = 0
        }),
        new("combat", "Комбат (PvP)", new Dictionary<string, int>
        {
            ["shadows"] = 1, ["textures"] = 1, ["lighting"] = 0, ["trees"] = 2,
            ["reflections_on_the_water"] = 0, ["grass"] = 0, ["clouds"] = 0, ["smoothing"] = 0
        }),
        new("balance", "Баланс", new Dictionary<string, int>
        {
            ["shadows"] = 2, ["textures"] = 2, ["lighting"] = 1, ["trees"] = 2,
            ["reflections_on_the_water"] = 2, ["grass"] = 2, ["clouds"] = 1, ["smoothing"] = 2
        }),
        new("graphic", "Графика (Максимум)", new Dictionary<string, int>
        {
            ["shadows"] = 2, ["textures"] = 3, ["lighting"] = 2, ["trees"] = 3,
            ["reflections_on_the_water"] = 2, ["grass"] = 2, ["clouds"] = 2, ["smoothing"] = 3
        })
    };

    public static readonly IReadOnlyList<ReadyBindTemplate> PopularBinds = new List<ReadyBindTemplate>
    {
        new("Автобег", "q", "forward;sprint", "Бежать вперёд без удержания W и Shift"),
        new("Быстрое лутание (Hover Loot)", "h", "+hoverloot", "Быстрый перенос предметов при наведении"),
        new("Вывести пинг и FPS в консоль", "f2", "perf 1;ping", "Включает отображение FPS и выводит пинг"),
        new("Очистить память и буфер (GC)", "k", "gc.collect;pool.clear_assets;pool.clear_memory", "Принудительная очистка памяти при просадках FPS"),
        new("Зум (приближение FOV)", "mouse3", "+graphics.fov 90;graphics.fov 70", "Уменьшает угол обзора при зажатии клавиши, возвращая 90 при отпускании"),
        new("Быстрый крафт бинтов", "z", "craft.add -2072273936 1", "Добавляет 1 бинт в очередь крафта"),
        new("Быстрый крафт шприца", "x", "craft.add 1079279582 1", "Добавляет 1 медицинский шприц в очередь крафта"),
        new("Комбатлог по кнопке", "f3", "consoletoggle;combatlog", "Открывает консоль и сразу показывает combatlog"),
        new("Прыжок в окно (Jump + Duck)", "c", "+jump;+duck", "Одновременный прыжок с приседанием"),
        new("Суицид (Kill)", "end", "kill", "Мгновенное самоубийство персонажа"),
        new("Смена руки (левая/правая)", "n", "~graphics.vm_horizontal_flip True;graphics.vm_horizontal_flip False", "Переключает отображение оружия между левой и правой рукой"),
        new("Отключение звука игры (Mute)", "m", "~audio.game 0;audio.game 1", "Переключает громкость звуков игры между 0 и 1")
    };

    public static string NormalizeValue(string s)
    {
        if (s == null) return "";
        return s.Trim().Replace("\\\"", "\"").Trim('"');
    }

    public static Dictionary<string, string> ReadClientCfgDictionary()
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string path = SteamAndRustService.GetClientCfgPath();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return dict;

        foreach (string rawLine in File.ReadAllLines(path))
        {
            string line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//") || line.StartsWith("#"))
                continue;

            string[] parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 1)
            {
                string key = parts[0].Trim();
                string val = parts.Length > 1 ? parts[1].Trim() : "\"\"";
                dict[key] = val;
            }
        }
        return dict;
    }

    public static void WriteClientCfgDictionary(Dictionary<string, string> dict)
    {
        string path = SteamAndRustService.GetClientCfgPath();
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("Папка Rust не выбрана. Выберите папку Rust во вкладке «Конфиги и Бинды».");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        SteamAndRustService.ClearReadOnlySafe(path);
        var lines = dict.Select(kv => $"{kv.Key} {kv.Value}");
        File.WriteAllLines(path, lines);
    }

    public static List<(string Key, string Value)> ParseLaunchParamsToKeyValues(string launchOptions)
    {
        var result = new List<(string Key, string Value)>();
        if (string.IsNullOrWhiteSpace(launchOptions))
            return result;

        var regex = new Regex(@"(?:^|\s)-(?<token>[^\s]+)(?:\s+(?<val>(?:\\?""[^""]*\\?""|[^-\s][^\s]*)))?", RegexOptions.Compiled);
        foreach (Match m in regex.Matches(launchOptions))
        {
            string k = m.Groups["token"].Value.Trim();
            string v = m.Groups["val"].Success ? m.Groups["val"].Value.Trim() : "";
            if (!string.IsNullOrWhiteSpace(k))
                result.Add((k, v));
        }
        return result;
    }

    public static string BuildLaunchParamsFromKeyValues(List<(string Key, string Value)> items)
    {
        return string.Join(" ", items.Select(x =>
            string.IsNullOrWhiteSpace(x.Value)
                ? $"-{x.Key}"
                : $"-{x.Key} {x.Value}"));
    }

    public static bool IsInGameTweakEnabled(InGameTweakDefinition tweak, Dictionary<string, string> cfg = null, List<(string Key, string Value)> launch = null)
    {
        cfg ??= ReadClientCfgDictionary();
        launch ??= ParseLaunchParamsToKeyValues(SteamAndRustService.ReadLaunchOptions());

        if (tweak.ConfigParams != null)
        {
            foreach (var node in tweak.ConfigParams)
            {
                if (!cfg.TryGetValue(node.Key, out string actual) ||
                    !string.Equals(NormalizeValue(actual), NormalizeValue(node.Value), StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
        }

        if (tweak.LaunchParams != null)
        {
            foreach (var node in tweak.LaunchParams)
            {
                var found = launch.FirstOrDefault(x => string.Equals(x.Key, node.Key, StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrEmpty(found.Key) ||
                    !string.Equals(NormalizeValue(found.Value), NormalizeValue(node.Value), StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
        }

        return true;
    }

    public static void SetInGameTweak(InGameTweakDefinition tweak, bool enable)
    {
        var cfg = ReadClientCfgDictionary();
        string rawLaunch = SteamAndRustService.ReadLaunchOptions();
        var launch = ParseLaunchParamsToKeyValues(rawLaunch);
        bool launchChanged = false;

        if (tweak.ConfigParams != null)
        {
            foreach (var node in tweak.ConfigParams)
            {
                if (enable)
                    cfg[node.Key] = node.Value;
                else
                    cfg.Remove(node.Key);
            }
            WriteClientCfgDictionary(cfg);
        }

        if (tweak.LaunchParams != null)
        {
            foreach (var node in tweak.LaunchParams)
            {
                launch.RemoveAll(x => string.Equals(x.Key, node.Key, StringComparison.OrdinalIgnoreCase));
                if (enable)
                    launch.Add((node.Key, node.Value));
                launchChanged = true;
            }
        }

        if (launchChanged)
        {
            string updated = BuildLaunchParamsFromKeyValues(launch);
            SteamAndRustService.WriteLaunchOptions(updated);
        }
    }

    public static int GetCurrentGraphicsLevel(GraphicsCategoryDefinition category, Dictionary<string, string> cfg = null, List<(string Key, string Value)> launch = null)
    {
        cfg ??= ReadClientCfgDictionary();
        launch ??= ParseLaunchParamsToKeyValues(SteamAndRustService.ReadLaunchOptions());

        foreach (var level in category.Levels)
        {
            bool match = true;
            if (level.ConfigParams != null)
            {
                foreach (var node in level.ConfigParams)
                {
                    if (!cfg.TryGetValue(node.Key, out string val) ||
                        !string.Equals(NormalizeValue(val), NormalizeValue(node.Value), StringComparison.OrdinalIgnoreCase))
                    {
                        match = false;
                        break;
                    }
                }
            }
            if (match && level.LaunchParams != null)
            {
                foreach (var node in level.LaunchParams)
                {
                    var found = launch.FirstOrDefault(x => string.Equals(x.Key, node.Key, StringComparison.OrdinalIgnoreCase));
                    if (string.IsNullOrEmpty(found.Key) ||
                        !string.Equals(NormalizeValue(found.Value), NormalizeValue(node.Value), StringComparison.OrdinalIgnoreCase))
                    {
                        match = false;
                        break;
                    }
                }
            }
            if (match)
                return level.Level;
        }
        return -1;
    }

    public static void ApplyGraphicsSelections(Dictionary<string, int> selections)
    {
        var cfg = ReadClientCfgDictionary();
        var launch = ParseLaunchParamsToKeyValues(SteamAndRustService.ReadLaunchOptions());
        bool launchModified = false;

        foreach (var cat in GraphicsCategories)
        {
            if (!selections.TryGetValue(cat.Id, out int targetLevel) || targetLevel < 0)
                continue;

            var levelDef = cat.Levels.FirstOrDefault(l => l.Level == targetLevel);
            if (levelDef == null)
                continue;

            // Remove any launch params from other levels of this category first
            foreach (var lv in cat.Levels)
            {
                if (lv.LaunchParams != null)
                {
                    foreach (var lp in lv.LaunchParams)
                    {
                        if (launch.RemoveAll(x => string.Equals(x.Key, lp.Key, StringComparison.OrdinalIgnoreCase)) > 0)
                            launchModified = true;
                    }
                }
            }

            if (levelDef.ConfigParams != null)
            {
                foreach (var cp in levelDef.ConfigParams)
                    cfg[cp.Key] = cp.Value;
            }

            if (levelDef.LaunchParams != null)
            {
                foreach (var lp in levelDef.LaunchParams)
                {
                    launch.Add((lp.Key, lp.Value));
                    launchModified = true;
                }
            }
        }

        WriteClientCfgDictionary(cfg);
        if (launchModified)
            SteamAndRustService.WriteLaunchOptions(BuildLaunchParamsFromKeyValues(launch));
    }

    public static void ResetGraphicsSettings()
    {
        var cfg = ReadClientCfgDictionary();
        var launch = ParseLaunchParamsToKeyValues(SteamAndRustService.ReadLaunchOptions());

        foreach (var cat in GraphicsCategories)
        {
            foreach (var lv in cat.Levels)
            {
                if (lv.ConfigParams != null)
                {
                    foreach (var cp in lv.ConfigParams)
                        cfg.Remove(cp.Key);
                }
                if (lv.LaunchParams != null)
                {
                    foreach (var lp in lv.LaunchParams)
                        launch.RemoveAll(x => string.Equals(x.Key, lp.Key, StringComparison.OrdinalIgnoreCase));
                }
            }
        }

        WriteClientCfgDictionary(cfg);
        SteamAndRustService.WriteLaunchOptions(BuildLaunchParamsFromKeyValues(launch));
    }

    public static List<BindEntry> ReadBinds()
    {
        var list = new List<BindEntry>();
        string path = SteamAndRustService.GetKeysCfgPath();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return list;

        foreach (string raw in File.ReadAllLines(path))
        {
            string line = raw.Trim();
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("bind ", StringComparison.OrdinalIgnoreCase))
                continue;

            string rest = line.Substring(5).Trim();
            int sp = rest.IndexOf(' ');
            if (sp <= 0)
                continue;

            string key = rest.Substring(0, sp).Trim();
            string cmd = rest.Substring(sp + 1).Trim().Trim('"');
            list.Add(new BindEntry { Key = key, Command = cmd, RawLine = line });
        }
        return list;
    }

    public static void AddOrReplaceBind(string key, string command, string previousRawLine = null)
    {
        string path = SteamAndRustService.GetKeysCfgPath();
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("Папка Rust не выбрана.");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        SteamAndRustService.ClearReadOnlySafe(path);

        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
        string cleanCmd = command.Trim().Trim('"');
        string newLine = $"bind {key.Trim()} \"{cleanCmd}\"";

        int idx = -1;
        if (!string.IsNullOrWhiteSpace(previousRawLine))
            idx = lines.FindIndex(l => string.Equals(l.Trim(), previousRawLine.Trim(), StringComparison.OrdinalIgnoreCase));

        if (idx == -1)
            idx = lines.FindIndex(l => l.Trim().StartsWith($"bind {key.Trim()} ", StringComparison.OrdinalIgnoreCase));

        if (idx >= 0)
            lines[idx] = newLine;
        else
            lines.Add(newLine);

        File.WriteAllLines(path, lines);
    }

    public static void DeleteBind(BindEntry entry)
    {
        string path = SteamAndRustService.GetKeysCfgPath();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        SteamAndRustService.ClearReadOnlySafe(path);
        var lines = File.ReadAllLines(path).ToList();
        lines.RemoveAll(l => string.Equals(l.Trim(), entry.RawLine.Trim(), StringComparison.OrdinalIgnoreCase));
        File.WriteAllLines(path, lines);
    }
}
