using System;
using System.Collections.Generic;

namespace SuperNova.ValheimSpawner
{
    internal enum CreatureCategory
    {
        Regular = 0,
        Boss = 1
    }

    internal sealed class CreatureMetadata
    {
        public string English;
        public string Russian;
        public CreatureCategory Category;
        public string[] Biomes;

        public CreatureMetadata(string english, string russian, CreatureCategory category, string biomeList)
        {
            English = english;
            Russian = russian;
            Category = category;
            Biomes = string.IsNullOrWhiteSpace(biomeList) ? new string[0] : biomeList.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        }
    }

    internal static class CreatureCatalog
    {
        // Biome names and display names are shown in English. Russian creature names remain searchable aliases.
        // The prefab name remains in the search index for unmapped creatures.
        private const string CatalogData = @"
Eikthyr|Eikthyr|Эйктюр|Boss|Meadows
gd_king|The Elder|Древний|Boss|Black Forest
Bonemass|Bonemass|Масса костей|Boss|Swamp
Dragon|Moder|Модер|Boss|Mountains
GoblinKing|Yagluth|Яглут|Boss|Plains
SeekerQueen|The Queen|Королева|Boss|Mistlands
Fader|Fader|Прародитель|Boss|Ashlands
FrozenKing|Kall Fimbulbringer|Калль Фимбульбрингер|Boss|Deep North
FrozenKing_p2|Kall Fimbulbringer (phase 2)|Калль Фимбульбрингер (фаза 2)|Boss|Deep North
FrozenKing_p3|Kall Fimbulbringer (phase 3)|Калль Фимбульбрингер (фаза 3)|Boss|Deep North
KallFimbulbringer|Kall Fimbulbringer|Калль Фимбульбрингер|Boss|Deep North
Brenna|Brenna|Бренна|Boss|Black Forest
Geirrhafa|Geirrhafa|Гейрафа|Boss|Mountains
Zil|Zil|Зил|Boss|Mistlands
Thungr|Thungr|Тангр|Boss|Mistlands
Charred_Melee_Dyrnwyn|Lord Reto|Владыка Рето|Boss|Ashlands
Boar|Boar|Кабан|Regular|Meadows;Black Forest
Boar_piggy|Boar piglet|Поросёнок кабана|Regular|Meadows;Black Forest
Deer|Deer|Олень|Regular|Meadows;Black Forest
Deer_White|White deer|Белый олень|Regular|Deep North
Neck|Neck|Никс|Regular|Meadows
Greyling|Greyling|Грейлинг|Regular|Meadows
Greydwarf|Greydwarf|Грейдворф|Regular|Black Forest
Greydwarf_Elite|Greydwarf Brute|Грейдворф-берсерк|Regular|Black Forest
Greydwarf_Shaman|Greydwarf Shaman|Грейдворф-шаман|Regular|Black Forest
Greydwarf_Spawn|Greydwarf nest spawn|Порождение грейдворфов|Regular|Black Forest
Greydwarf_Frozen|Frozen greydwarf|Ледяной грейдворф|Regular|Deep North
Greydwarf_Shaman_Frozen|Frozen greydwarf shaman|Ледяной грейдворф-шаман|Regular|Deep North
Troll|Troll|Тролль|Regular|Black Forest
TrollFrost|Gammeltroll|Гаммельтролль|Regular|Deep North
Troll_sleeping|Sleeping troll|Спящий тролль|Regular|Deep North
Skeleton|Skeleton|Скелет|Regular|Black Forest;Swamp;Mountains
Skeleton_NoArcher|Skeleton|Скелет|Regular|Black Forest;Swamp;Mountains
Skeleton_Poison|Poison skeleton|Ядовитый скелет|Regular|Swamp
Skeleton_Meadows|Meadows skeleton|Скелет лугов|Regular|Deep North
Skeleton_Meadows_noarcher|Meadows skeleton|Скелет лугов|Regular|Deep North
Skeleton_Mountains|Mountain skeleton|Горный скелет|Regular|Deep North
Skeleton_Mountains_noarcher|Mountain skeleton|Горный скелет|Regular|Deep North
Skeleton_Swamps|Swamp skeleton|Болотный скелет|Regular|Deep North
Skeleton_Swamps_noarcher|Swamp skeleton|Болотный скелет|Regular|Deep North
Skeleton_aspect|Skeleton aspect|Аспект скелета|Regular|Deep North
Draugr|Draugr|Драугр|Regular|Swamp
Draugr_Elite|Draugr Elite|Драугр-элита|Regular|Swamp
Draugr_Ranged|Draugr Archer|Драугр-лучник|Regular|Swamp
Draugr_Ranged_down|Draugr Archer (down)|Драугр-лучник (лежит)|Regular|Swamp
Draugr_Elite_sleeping|Sleeping draugr elite|Спящий драугр-элита|Regular|Deep North
Draugr_Ranged_sleeping|Sleeping draugr archer|Спящий драугр-лучник|Regular|Deep North
Draugr_sleeping|Sleeping draugr|Спящий драугр|Regular|Deep North
Blob|Blob|Сгустень|Regular|Swamp
BlobElite|Blob elite|Большой сгустень|Regular|Swamp
BlobTar|Tar blob|Дегтевой сгустень|Regular|Plains
BlobMork|Shapeless Pulp|Бесформенная слизь|Regular|Deep North
BlobMorkMini|Tiny Pulp|Крошечная слизь|Regular|Deep North
BlobAspect|Blob aspect|Аспект сгустня|Regular|Deep North
Leech|Leech|Пиявка|Regular|Swamp
Wraith|Wraith|Призрак|Regular|Swamp
Ghost|Ghost|Призрак|Regular|Black Forest
Ghost_old|Old ghost|Старый призрак|Regular|Deep North
Ghost_sleeping|Sleeping ghost|Спящий призрак|Regular|Deep North
Ghost_Void|Void ghost|Пустотный призрак|Regular|Deep North
Abomination|Abomination|Мерзость|Regular|Swamp
Surtling|Surtling|Суртлинг|Regular|Swamp;Ashlands
Wolf|Wolf|Волк|Regular|Mountains
Wolf_cub|Wolf cub|Волчонок|Regular|Mountains
Wolf_spiritcaller|Spirit-caller wolf|Волк призывателя духов|Regular|Deep North
Fenring|Fenring|Фенринг|Regular|Mountains
Fenring_Cultist|Fenring Cultist|Фенринг-культист|Regular|Mountains
Hatchling|Drake|Дракончик|Regular|Mountains
StoneGolem|Stone Golem|Каменный голем|Regular|Mountains
Ulv|Ulv|Ульв|Regular|Mountains
Bat|Bat|Летучая мышь|Regular|Mountains
Bear|Bear|Медведь|Regular|Deep North
Cultist|Cultist|Культист|Regular|Mountains
Serpent|Serpent|Морской змей|Regular|Ocean
Deathsquito|Deathsquito|Смертожал|Regular|Plains
Lox|Lox|Локс|Regular|Plains
Goblin|Fuling|Фулинг|Regular|Plains
GoblinArcher|Fuling Archer|Фулинг-лучник|Regular|Plains
GoblinShaman|Fuling Shaman|Фулинг-шаман|Regular|Plains
GoblinBrute|Fuling Berserker|Фулинг-берсерк|Regular|Plains
Asksvin|Asksvin|Асксвин|Regular|Ashlands
Asksvin_cub|Asksvin calf|Детёныш асксвина|Regular|Ashlands
Morgen|Morgen|Морген|Regular|Ashlands
Morgen_Naked|Morgen (unarmored)|Морген без брони|Regular|Ashlands
FallenValkyrie|Charred Valkyrie|Обугленная валькирия|Regular|Ashlands
Charred_Twitcher|Charred Twitcher|Обугленный прыгун|Regular|Ashlands
Charred_Melee|Charred Warrior|Обугленный воин|Regular|Ashlands
Charred_Archer|Charred Marksman|Обугленный стрелок|Regular|Ashlands
Charred_Mage|Charred Mage|Обугленный маг|Regular|Ashlands
Volture|Volture|Вольтур|Regular|Ashlands
Tick|Tick|Клещ|Regular|Mistlands
Seeker|Seeker|Искатель|Regular|Mistlands
SeekerBrood|Seeker Brood|Выводок искателя|Regular|Mistlands
SeekerBrute|Seeker Soldier|Искатель-солдат|Regular|Mistlands
Gjall|Gjall|Гьялль|Regular|Mistlands
Hare|Hare|Заяц|Regular|Mistlands
Dverger|Dvergr|Дверг|Regular|Mistlands
DvergerMage|Dvergr Mage|Дверг-маг|Regular|Mistlands
DvergerMageFire|Dvergr Fire Mage|Дверг-маг огня|Regular|Mistlands
DvergerMageIce|Dvergr Frost Mage|Дверг-маг льда|Regular|Mistlands
DvergerMageSupport|Dvergr Support Mage|Дверг-маг поддержки|Regular|Mistlands
DvergerRogue|Dvergr Rogue|Дверг-разведчик|Regular|Mistlands
Chicken|Chicken|Курица|Regular|Any Biome
Hen|Hen|Курица-несушка|Regular|Any Biome
Fish1|Fish|Рыба|Regular|Rivers;Ocean
Fish2|Fish|Рыба|Regular|Rivers;Ocean
Fish3|Fish|Рыба|Regular|Ocean
Fish4_cave|Cave fish|Пещерная рыба|Regular|Mountain Caves
Crow|Crow|Ворон|Regular|Any Biome
AshCrow|Ash crow|Пепельный ворон|Regular|Ashlands
Gull|Gull|Чайка|Regular|Ocean;Meadows;Black Forest;Plains
Leviathan|Leviathan|Левиафан|Regular|Ocean
Hugin|Hugin|Хугин|Regular|Any Biome
Munin|Munin|Мунин|Regular|Mistlands;Ashlands
Odin|Odin|Один|Regular|Any Biome
Valkyrie|Valkyrie|Валькирия|Regular|Any Biome
Haldor|Haldor|Хальдор|Regular|Black Forest
Hildir|Hildir|Хильдир|Regular|Meadows
BogWitch|The Bog Witch|Болотная ведьма|Regular|Swamp
Halstein|Halstein|Хальштейн|Regular|Meadows
Blub|Blåbær|Блубэр|Regular|Meadows
Hallon|Hallon|Халлон|Regular|Meadows
Boar_spiritcaller|Spirit-caller boar|Кабан призывателя духов|Regular|Deep North
Moose|Moose|Лось|Regular|Deep North
Moose_calf|Moose calf|Лосёнок|Regular|Deep North
Moose_spiritcaller|Spirit-caller moose|Лось призывателя духов|Regular|Deep North
Seal|Seal|Тюлень|Regular|Deep North
Seal_Pup|Seal pup|Детёныш тюленя|Regular|Deep North
Frysling|Frysling|Фризлинг|Regular|Deep North
FrostWisp|Frost Wisp|Ледяной дух|Regular|Deep North
Bat_Swamp|Swamp bat|Болотная летучая мышь|Regular|Swamp
Barka|Barka|Барка|Regular|Deep North
Elaking|Elaking|Элакинг|Regular|Deep North
ElakingLantern|Elaking Lantern|Элакинг с фонарём|Regular|Deep North
ElakingMole|Eyeless One|Безглазый|Regular|Deep North
FallenWarrior|Fallen Warrior|Павший воин|Regular|Deep North
JotunWarrior|Krigen|Криген|Regular|Deep North
JotunWarriorDualWield|Krigen (dual wield)|Криген с двумя оружиями|Regular|Deep North
JotunWitch|Hexen|Хексен|Regular|Deep North
ShadowPerson|Shadow|Тень|Regular|Deep North
Tendril|Tendril|Усик|Regular|Deep North
Tendril_back|Back tendril|Задний усик|Regular|Deep North
TentaRoot_wild|Wild tentaroot|Дикая тентакорень|Regular|Deep North
Writhan|Writhan|Ритхан|Regular|Deep North
GoblinDeepNorth|Deep North goblin|Гоблин Дальнего севера|Regular|Deep North
DvergerDeepNorth|Imprisoned Dvergr|Пленённый дверг|Regular|Deep North
Bjorn_sleeping|Sleeping Bjorn|Спящий Бьёрн|Regular|Deep North
Bjorn_spiritcaller|Spirit-caller Bjorn|Бьёрн призывателя духов|Regular|Deep North
Aspect_Eikthyr|Aspect of the Lightning Stag|Аспект Эйктюра|Boss|Any Biome
Aspect_Elder|Aspect of the Living Forest|Аспект Древнего|Boss|Any Biome
Aspect_Bonemass|Aspect of the Twisted Soul|Аспект Массы костей|Boss|Any Biome
Aspect_Moder|Aspect of the Dragon Mother|Аспект Модер|Boss|Any Biome
Aspect_Yagluth|Aspect of the Writhing Dead|Аспект Яглута|Boss|Any Biome
Aspect_SeekerQueen|Aspect of the Crawling Matriarch|Аспект Королевы|Boss|Any Biome
Aspect_Fader|Aspect of the Emerald Flame|Аспект Прародителя|Boss|Any Biome
Aspect_TentaRoot|Aspect of the Tentaroot|Аспект тентакорня|Boss|Any Biome
"
;

        private static readonly Dictionary<string, CreatureMetadata> Metadata = Build();

        private static Dictionary<string, CreatureMetadata> Build()
        {
            var result = new Dictionary<string, CreatureMetadata>(StringComparer.OrdinalIgnoreCase);
            string[] lines = CatalogData.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                string[] p = line.Split('|');
                if (p.Length < 5) continue;
                CreatureCategory category = CreatureCategory.Regular;
                if (p[3].Equals("Boss", StringComparison.OrdinalIgnoreCase)) category = CreatureCategory.Boss;
                result[p[0].Trim()] = new CreatureMetadata(p[1].Trim(), p[2].Trim(), category, p[4].Trim());
            }
            return result;
        }

        public static bool TryGet(string prefabName, out CreatureMetadata metadata)
        {
            return Metadata.TryGetValue(prefabName ?? string.Empty, out metadata);
        }
    }
}
