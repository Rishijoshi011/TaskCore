using System;
using System.Collections.Generic;

namespace TaskCore.Services
{
    /// <summary>
    /// Classifies processes by expected behavior for context-aware anomaly detection.
    /// Uses HashSet-based lookups for O(1) performance instead of repeated string scanning.
    /// </summary>
    public enum ExpectedBehavior
    {
        Standard,    // Normal apps - use default thresholds
        Gaming,      // Games - expect high CPU/GPU
        Creative,    // Video editors, 3D software - expect high usage during work
        Interactive  // Browsers, video calls - moderate usage expected
    }

    internal static class ProcessBehaviorClassifier
    {
        #region Gaming Applications (High CPU/GPU expected)

        // Game launchers and platforms
        private static readonly HashSet<string> GameLaunchers = new(StringComparer.OrdinalIgnoreCase)
        {
            "steam", "epicgameslauncher", "origin", "eaapp", "eadesktop",
            "battle.net", "battlenet", "blizzard", "riotgames",
            "xbox", "xboxapp", "gamebar", "gamebarftserver",
            "ubisoftconnect", "uplay", "gog", "galaxy",
            "bethesda", "bethesdanet", "minecraftlauncher",
            "roblox", "robloxplayer", "robloxstudiobeta"
        };

        // First-person shooters (FPS)
        private static readonly HashSet<string> FpsGames = new(StringComparer.OrdinalIgnoreCase)
        {
            "csgo", "cs2", "counter-strike", "valorant",
            "overwatch", "overwatch2", "apex", "apexlegends",
            "callofduty", "cod", "modernwarfare", "warzone",
            "battlefield", "rainbowsix", "siege", "r6",
            "destiny2", "halo", "haloinfinite", "halomcc",
            "doom", "doometernal", "quake", "quakechampions",
            "titanfall", "titanfall2", "splitgate",
            "teamfortress", "tf2", "halflife", "halflife2",
            "left4dead", "left4dead2", "back4blood",
            "borderlands", "borderlands2", "borderlands3",
            "outriders", "remnant", "darktide", "vermintide",
            "killingfloor", "killingfloor2", "deceit",
            "thefinals", "xdefiant", "shatterline",
            "ironsight", "blackshot", "suddenattack",
            "crossfire", "pointblank", "specialforce"
        };

        // Battle Royale games
        private static readonly HashSet<string> BattleRoyaleGames = new(StringComparer.OrdinalIgnoreCase)
        {
            "fortnite", "fortnitelauncher",
            "pubg", "pubg_lite", "playerunknown",
            "apexlegends", "warzone", "warzone2",
            "fallguys", "hyperscape", "realmroyale",
            "ringofelysium", "ros", "knivesout",
            "rules", "creative", "destruction",
            "freefire", "ff", "callofduty", "codm",
            "superpeople", "naraka", "bladepoint",
            "bloodhunt", "vampire", "crabgame",
            "secondextinction", "scavengers",
            "spellbreak", "crushtime", "crushon",
            "tothemoon", "towers", "battleroyale"
        };

        // RPGs and open world games
        private static readonly HashSet<string> RpgGames = new(StringComparer.OrdinalIgnoreCase)
        {
            "witcher", "witcher3", "cyberpunk", "cyberpunk2077",
            "skyrim", "skyrimse", "skyrimvr", "oblivion", "morrowind",
            "fallout", "fallout4", "fallout76", "falloutnv",
            "elderscrolls", "starfield",
            "dragonage", "dragonage2", "dai", "daorigins",
            "masseffect", "me2", "me3", "meandromeda",
            "divinity", "dos2", "baldursgate", "bg3",
            "pathfinder", "wrathoftherighteous", "kingmaker",
            "poe", "pillarsofeternity", "deadfire",
            "tyranny", "torment", "planescape",
            "discoelysium", "expeditions", "conquistador",
            "wasteland", "wasteland2", "wasteland3",
            "underrail", "atomrpg", "deus ex",
            "systemshock", "systemshock2", "prey",
            "dishonored", "dishonored2", "deathloop",
            "bioshock", "bioshock2", "bioshockinfinite",
            "darkmessiah", "arx", "fatalis"
        };

        // Souls-like and challenging action games
        private static readonly HashSet<string> SoulsGames = new(StringComparer.OrdinalIgnoreCase)
        {
            "darksouls", "darksouls2", "darksouls3", "darksoulsremastered",
            "demonssouls", "bloodborne", "sekiro",
            "eldenring", "armoredcore", "ac6",
            "lords", "fallen", "liesofp", "thymesia",
            "steelrising", "bleakfaith", "forspoken",
            "starwarsjedifallenorder", "starwarssurvivor",
            "hellblade", "senua", "ashen", "immortal",
            "mortal", "remnant", "codevein", "nioh", "nioh2",
            "stranger", "paradise", "sinner", "tencent"
        };

        // MMOs and online games
        private static readonly HashSet<string> MmoGames = new(StringComparer.OrdinalIgnoreCase)
        {
            "wow", "worldofwarcraft", "warcraft",
            "ffxiv", "finalfantasyxiv", "finalfantasy14",
            "guildwars", "gw2", "guildwars2",
            "eso", "elderscrollsonline",
            "runescape", "osrs", "oldschool",
            "blackdesert", "bdo", "lostark",
            "newworld", "newworldlauncher",
            "archeage", "bladeandsoul", "bns",
            "tera", "aion", "lineage", "lineage2",
            "maplestory", "vindictus",
            "neverwinter", "swtor", "sto",
            "eve", "eveonline", "elite", "elitedangerous",
            "albion", "albiononline", "mortalonline",
            "wurm", "projectgorgon", "rift",
            "wildstar", "tera", "revelation",
            "swordsman", "ageofwushu", "ageofwulin"
        };

        // Racing games
        private static readonly HashSet<string> RacingGames = new(StringComparer.OrdinalIgnoreCase)
        {
            "forza", "forzahorizon", "forzamotorsport",
            "granturismo", "gt7", "gt6", "gt5", "gtsport",
            "f1", "f12023", "f12022", "f1manager",
            "needforspeed", "nfs", "heat", "payback",
            "carbon", "mostwanted", "underground",
            "grid", "dirt", "wrc", "rally",
            "assettocorsa", "acc", "competizione",
            "beamng", "beamngdrive",
            "projectcars", "projectcars2", "projectcars3",
            "automobilista", "rfactor", "rfactor2",
            "iracing", "liveforspeed", "netkar",
            "raceroom", "race07", "gtr", "gtlegends",
            "trackmania", "nations", "united", "turbo",
            "burnout", "paradise", "revenge",
            "midnightclub", "thecrew", "crew2",
            "nfsheat", "nfspayback", "nfsrivals",
            "hotwheels", "unleashed", "acceleracers"
        };

        // Fighting games
        private static readonly HashSet<string> FightingGames = new(StringComparer.OrdinalIgnoreCase)
        {
            "streetfighter", "sf6", "sf5", "sf4",
            "tekken", "tekken7", "tekken8",
            "mortalkombat", "mk11", "mk1",
            "guiltygear", "strive", "xrd", "accentcore",
            "blazblue", "centralfiction", "crosstag",
            "soulcalibur", "soulcalibur6",
            "kingoffighters", "kof", "kof15",
            "samuraishodown", "garou", "fatalfury",
            "thelastblade", "gundam", "extremevs",
            "dragonballfighterz", "dbfz",
            "jumpforce", "myhero", "bokunohero",
            "naruto", "storm4", "boruto",
            "bleach", "bravesouls",
            "jojo", "allstarbattle",
            "granblue", "versus", "rising",
            "skullgirls", "rivalsofaether",
            "brawlhalla", "multiversus",
            "smash", "smashbros", "ultimate",
            "nickelodeon", "allstarbrawl"
        };

        // Strategy games (RTS, TBS, 4X)
        private static readonly HashSet<string> StrategyGames = new(StringComparer.OrdinalIgnoreCase)
        {
            "starcraft", "starcraft2", "sc2",
            "ageofempires", "aoe", "aoe2", "aoe3", "aoe4",
            "warcraft", "warcraft3", "wc3",
            "commandandconquer", "cnc", "generals",
            "redalert", "tiberium", "kane",
            "dawnofwar", "dow", "companyofheroes", "coh",
            "totalwar", "totalwarhammer", "warhammer3",
            "rome", "medieval", "attila", "thrones",
            "civilization", "civ", "civ5", "civ6", "civ7",
            "humankind", "oldworld",
            "stellaris", "endless", "endlesslegend", "endlessspace",
            "crusaderkings", "ck2", "ck3",
            "europauniversalis", "eu4",
            "hearts", "hoi4",
            "victoria", "vic3",
            "imperator", "rome",
            "totalwar", "threekingdoms", "troy",
            "xcom", "xcom2", "xcomchimera",
            "phoenixpoint", "battletech",
            "unityofcommand", "panzercorps",
            "wargame", "warno", "steeldivision",
            "menofwar", "facesofwar", "gatesofhell",
            "companyofcrime", "cityofgangsters"
        };

        // Survival and crafting games
        private static readonly HashSet<string> SurvivalGames = new(StringComparer.OrdinalIgnoreCase)
        {
            "minecraft", "minecraftlauncher",
            "terraria", "starbound",
            "valheim", "raft", "subnautica", "subnautica2",
            "theforest", "sonsoftheforest", "greenhell",
            "dayz", "arma", "arma3", "arma2",
            "rust", "fortify", "hurtworld",
            "ark", "arksurvival", "survivalevolved",
            "conan", "conanexiles", "exiles",
            "dontstarve", "dst", "hamlet",
            "7daystodie", "7dtd",
            "projectzomboid", "zomboid",
            "scum", "deadside", "miscreated",
            "grounded", "smalland", "abandoned",
            "icarus", "icarussurvival",
            "satisfactory", "factorio",
            "dyson", "dysonsphere",
            "oxygen", "oxygennotincluded", "oni",
            "rimworld", "dwarf", "fortress",
            "goingmedieval", "medievaldynasty",
            "soulmask", "palworld",
            "enshrouded", "nightingale",
            "corekeeper", "forestsalvation"
        };

        // Sports games
        private static readonly HashSet<string> SportsGames = new(StringComparer.OrdinalIgnoreCase)
        {
            "fifa", "fifa23", "fifa24", "fifa25",
            "ea fc", "eafc", "fc24", "fc25",
            "pes", "efootball", "winningeleven",
            "nba", "nba2k", "nba2k24", "nba2k25",
            "nhl", "nhl24", "nhl25",
            "madden", "madden24", "madden25",
            "mlb", "mlbtheshow", "superbaseball",
            "tonyhawk", "skater", "skaterxl", "session",
            "ridersrepublic", "steep",
            "golf", "pgatour", "2kgolf", "eagolf",
            "topgolf", "wgt", "worldgolftour",
            "tennis", "topspin", "matchpoint",
            "cricket", "ashes", "bigbash",
            "rugby", "rugbychallenge",
            "f1", "formula1", "f123", "f124",
            "grid", "dirt", "wrc", "rally"
        };

        // Simulation games
        private static readonly HashSet<string> SimulationGames = new(StringComparer.OrdinalIgnoreCase)
        {
            "sims", "sims4", "sims3", "sims2",
            "simcity", "cities", "citiesskylines", "skylines2",
            "two-point", "twopoint", "hospital", "campus",
            "planetcoaster", "planetzoo",
            "jurassic", "jurassicworld", "evolution",
            "rollercoaster", "rct", "rct3",
            "transportfever", "trainfever",
            "trainsimulator", "trainz", "railsimulator",
            "bussimulator", "trucksimulator",
            "eurotruck", "americantruck", "ets2", "ats",
            "farmingsimulator", "fs22", "fs19",
            "purefarming", "cattleandcrops",
            "flight", "msfs", "msfs2020", "flightsimulator",
            "xplane", "prepar3d", "fsx",
            "dcs", "dcs world", "il2", "il2sturmovik",
            "warthunder", "enlisted",
            "spacet Engineers", "spacetengineers",
            "empyrion", "starbase", "dualuniverse",
            "kerbal", "ksp", "kerbalspaceprogram",
            "simplerockets", "junior", "simplerockets2",
            "universesandbox", "spaceengine"
        };

        // Horror games
        private static readonly HashSet<string> HorrorGames = new(StringComparer.OrdinalIgnoreCase)
        {
            "residentevil", "re2", "re3", "re4", "re7", "re8",
            "silent", "hill", "sh2", "sh3", "downpour",
            "outlast", "outlast2", "whistleblower",
            "amnesia", "darkdescent", "machineforpigs", "rebirth",
            "alien", "isolation", "aliens",
            "deadspace", "deadspace2", "deadspace3", "remake",
            "layers", "layersoffear", "layersoffear2",
            "visage", "madison", "summerford",
            "phasmophobia", "devour", "forewarned",
            "ghostwatchers", "demonologist",
            "lethalcompany", "contentwarning",
            "darkwood", "dontstarve",
            "little", "littlehope", "manofmedan",
            "until", "dawn", "quarry", "darkpictures",
            "blairwitch", "observer", "observer2",
            "tormented", "tormentedsouls",
            "songofhorror", "remothered", "tormentedfathers",
            "monstrum", "monstrum2",
            "darkdeception", "monkeybusiness",
            "bendy", "inkmachine", "darkrevival"
        };

        // Indie and popular games not covered above
        private static readonly HashSet<string> PopularIndieGames = new(StringComparer.OrdinalIgnoreCase)
        {
            "hades", "hades2",
            "celeste", "deadcells", "enter", "gungeon",
            "bindingofisaac", "isaac", "repentance",
            "spelunky", "spelunky2", "cavestory",
            "undertale", "deltarune", "deltarune2",
            "hollow", "hollowknight", "silksong",
            "stardewvalley", "concernedape",
            "shovel", "shovelknight", "specterknight",
            "ori", "oriblind", "oriwill", "wisps",
            "cuphead", "mugman", "mdhr",
            "hades", "supergiant", "transistor", "bastion",
            "pyre", "crypt", "necrodancer", "cadence",
            "risk", "riskofrain", "ror2",
            "slaythespire", "monstertrain", "griftlands",
            "inscryption", "loophero", "cultofthelamb",
            "vampire", "vampiresurvivors", "holocure",
            " Amongus", "amongus", "townofus",
            "fallguys", "gangbeasts", "partyanimals",
            "overcooked", "overcooked2",
            "movingout", "toolsup", "plateup",
            "uncharted", "lastofus", "tlou",
            "godofwar", "gow", "gowragnarok",
            "spiderman", "spidermanmm", "spiderman2",
            "ghost", "ghostoftsushima",
            "horizon", "horizonzerodawn", "horizonforbiddenwest",
            "zerodawn", "forbiddenwest",
            "deathstranding", "deathstrandingdc",
            "daysgone", "detroit", "becomehuman",
            "heavrain", "beyond", "twosouls",
            "fahrenheit", "indigoprophecy",
            "life", "lifeisstrange", "lis", "lis2", "lis3",
            "truecolors", "beforethestorm",
            "telltale", "walkingdead", "wolfamongus",
            "tales", "talesfromtheborderlands",
            "minecraft", "storymode", "minecraftdungeons",
            "hyrule", "cadence", "crypt",
            "crosscode", "eastward", "unsighted",
            "chicory", "chicorycolorful",
            "a short hike", "webfishing",
            "pico", "picopark", "picopark2",
            "brotato", "20minutestilldawn",
            "vampire", "vampiresurvivors",
            "holocure", "holo", "cure"
        };

        #endregion

        #region Creative/Production Applications (Moderate-High usage expected)

        // Adobe Creative Suite
        private static readonly HashSet<string> AdobeApps = new(StringComparer.OrdinalIgnoreCase)
        {
            "photoshop", "illustrator", "indesign",
            "premiere", "premierepro", "aftereffects",
            "audition", "bridge", "lightroom",
            "animate", "characteranimator",
            "dimension", "fuse", "xd",
            "mediaencoder", "prelude",
            "substance", "substancepainter", "substancedesigner",
            "substancesampler", "substancestager", "substancemodeler",
            "acrobat", "acrobatreader", "reader"
        };

        // 3D Modeling and Animation
        private static readonly HashSet<string> ModelingApps = new(StringComparer.OrdinalIgnoreCase)
        {
            "blender", "maya", "3dsmax", "max",
            "cinema4d", "c4d", "cinebench",
            "modo", "mari", "nuke",
            "houdini", "houdinifx", "houdiniengine",
            "katana", "clarisse", "gaffer",
            "zbrush", "zbrushcore", "sculptris",
            "marmoset", "toolbag", "bag",
            "keyshot", "lumion", "d5", "enscape",
            "twinmotion", "vray", "corona",
            "sketchup", "layout", "stylebuilder",
            "rhino", "rhinoceros", "grasshopper",
            "fusion360", "inventor", "revit",
            "autocad", "civil3d", "navisworks",
            "recap", "recappro", "bentley",
            "microstation", "archicad", "vectorworks",
            "allplan", "tekla", "teklastructures",
            "solidworks", "solidworkslauncher",
            "catia", "siemens", "nx", "ug"
        };

        // Rendering engines
        private static readonly HashSet<string> RenderingEngines = new(StringComparer.OrdinalIgnoreCase)
        {
            "renderman", "prman", "rendermanit",
            "arnold", "arnoldrender", "kick",
            "vray", "vraystandalone", "vrayrt",
            "octane", "octanerender", "octanestandalone",
            "redshift", "redshiftcmdline",
            "cycles", "blender", "eevee", "workbench",
            "mantra", "houdini", "karma",
            "moonray", "pixar", "disney",
            "amd", "prorender", "cycles",
            "intel", "ospray", "embree",
            "chaos", "vantage", "corona",
            "fstorm", "thea", "maxwell"
        };

        // Video editing and color grading
        private static readonly HashSet<string> VideoEditingApps = new(StringComparer.OrdinalIgnoreCase)
        {
            "davinci", "resolve", "fusion",
            "fairlight", "edit", "cut", "color",
            "avid", "mediacomposer", "protools",
            "finalcut", "fcp", "fcpX", "motion",
            "compressor", "logic", "logicprox",
            "garageband", "imovie", "clips",
            "camtasia", "snagit", "filmora",
            "movavi", "bandicam", "obs",
            "streamlabs", "xsplit", "vmix",
            "wirecast", "ecamm", "restream",
            "elgato", "4k", "capture",
            "filmic", "pro", "luma", "touch",
            "capcut", "vn", "kinemaster",
            "powerdirector", "videoleap"
        };

        // Audio production
        private static readonly HashSet<string> AudioApps = new(StringComparer.OrdinalIgnoreCase)
        {
            "ableton", "live", "live11", "live12",
            "flstudio", "fl", "fruity", "producer",
            "reaper", "cakewalk", "bandlab",
            "protools", "avid", "mediacomposer",
            "logic", "garageband", "mainstage",
            "reason", "reasonstudios",
            "bitwig", "studioone", "presonus",
            "cubase", "nuendo", "steinberg",
            "ableton", "live", "push",
            "serato", "djpro", "dj", "scratch",
            "traktor", "rekordbox", "pioneer",
            "audacity", "ocenaudio", "wavepad",
            "izotope", "rx", "neutron", "ozone",
            "nativeinstruments", "kontakt", "massive",
            "serum", "xfer", "vital", "sylenth1"
        };

        // Development and coding (heavy when building/compiling)
        private static readonly HashSet<string> DevBuildApps = new(StringComparer.OrdinalIgnoreCase)
        {
            "visualstudio", "devenv", "msbuild",
            "dotnet", "dotnetcli", "roslyn",
            "rider", "resharper", "dottrace",
            "intellij", "idea", "pycharm",
            "webstorm", "phpstorm", "clion",
            "goland", "rubymine", "appcode",
            "androidstudio", "studio64",
            "xcode", "xcbuild",
            "unity", "unityeditor", "unityhub",
            "unreal", "ue4", "ue5", "unrealengine",
            "godot", "godotengine", "defold",
            "cocos", "cocos2d", "cocosstudio",
            "gamemaker", "gms", "yoyogames",
            "construct", "scirra",
            "rpgmaker", "rpgmz", "rpgmv",
            "cryengine", "crytek", "sandbox",
            "source", "source2", "hammer",
            "cmake", "make", "ninja", "bazel",
            "gradle", "maven", "ant",
            "docker", "dockerdesktop", "containerd",
            "kubernetes", "kubectl", "minikube",
            "nodejs", "node", "npm", "yarn",
            "webpack", "vite", "parcel", "esbuild",
            "typescript", "tsc", "babel",
            "electron", "electronbuilder",
            "cl", "clang", "gcc", "g++", "mingw",
            "llvm", "lld", "lld-link"
        };

        #endregion

        #region Interactive Applications (Moderate usage expected)

        // Web browsers
        private static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase)
        {
            "chrome", "googlechrome", "chromium",
            "firefox", "firefoxdeveloper", "waterfox",
            "edge", "microsoftedge", "msedge",
            "opera", "operagx", "brave",
            "vivaldi", "arc", "arcbrowser",
            "tor", "torbrowser",
            "safari", "webkit",
            "electron", "discord", "slack",
            "notion", "obsidian", "figma",
            "miro", "lucidchart", "draw.io"
        };

        // Communication apps
        private static readonly HashSet<string> CommunicationApps = new(StringComparer.OrdinalIgnoreCase)
        {
            "discord", "discordptb", "discordcanary",
            "teams", "msteams", "skype",
            "zoom", "webex", "gotomeeting",
            "slack", "telegram", "whatsapp",
            "signal", "element", "riot",
            "jitsi", "mattermost", "rocket",
            "bluejeans", "bigbluebutton",
            "googlemeet", "hangouts", "duo",
            "facetime", "messages", "imessage",
            "viber", "line", "kakao",
            "wechat", "qq", "tim",
            "thunderbird", "outlook", "mail",
            "spark", "airmail", "newton"
        };

        // Streaming and media consumption
        private static readonly HashSet<string> StreamingApps = new(StringComparer.OrdinalIgnoreCase)
        {
            "spotify", "tidal", "deezer",
            "applemusic", "itunes", "amazonmusic",
            "youtube", "youtubemusic", "ytmusic",
            "netflix", "primevideo", "disneyplus",
            "hulu", "hbo", "max", "peacock",
            "crunchyroll", "funimation", "vrv",
            "twitch", "obs", "streamlabs",
            "vlc", "mpc", "potplayer",
            "kodi", "plex", "emby",
            "jellyfin", "mpchc", "mediaplayer"
        };

        #endregion

        /// <summary>
        /// Determines the expected behavior category for a process based on its name.
        /// Games and creative apps are expected to use high resources.
        /// </summary>
        public static ExpectedBehavior GetExpectedBehavior(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName))
                return ExpectedBehavior.Standard;

            var name = processName.ToLowerInvariant();

            // Check gaming categories first (most specific)
            if (GameLaunchers.Contains(name) ||
                FpsGames.Contains(name) ||
                BattleRoyaleGames.Contains(name) ||
                RpgGames.Contains(name) ||
                SoulsGames.Contains(name) ||
                MmoGames.Contains(name) ||
                RacingGames.Contains(name) ||
                FightingGames.Contains(name) ||
                StrategyGames.Contains(name) ||
                SurvivalGames.Contains(name) ||
                SportsGames.Contains(name) ||
                SimulationGames.Contains(name) ||
                HorrorGames.Contains(name) ||
                PopularIndieGames.Contains(name))
                return ExpectedBehavior.Gaming;

            // Check creative/production apps
            if (AdobeApps.Contains(name) ||
                ModelingApps.Contains(name) ||
                RenderingEngines.Contains(name) ||
                VideoEditingApps.Contains(name) ||
                AudioApps.Contains(name) ||
                DevBuildApps.Contains(name))
                return ExpectedBehavior.Creative;

            // Check interactive apps
            if (Browsers.Contains(name) ||
                CommunicationApps.Contains(name) ||
                StreamingApps.Contains(name))
                return ExpectedBehavior.Interactive;

            // Contains checks for patterns not in exact sets
            if (ContainsGamingPattern(name))
                return ExpectedBehavior.Gaming;

            if (ContainsCreativePattern(name))
                return ExpectedBehavior.Creative;

            return ExpectedBehavior.Standard;
        }

        /// <summary>
        /// Checks if process name contains gaming-related patterns
        /// </summary>
        private static bool ContainsGamingPattern(string name)
        {
            // Common game-related keywords
            string[] gamePatterns = new[]
            {
                "game", "play", "player", "multiplayer",
                "battle", "arena", "fighter", "shooter",
                "war", "combat", "tactical", "ops",
                "sniper", "marksman", "hunter",
                "quest", "adventure", "dungeon", "raid",
                "pvp", "pve", "mmorpg", "moba",
                "rogu", "roguelike", "roguelite",
                "tycoon", "simulator", "tycoon",
                "chess", "poker", "cards", "casino",
                "slots", "bingo", "mahjong", "solitaire"
            };

            foreach (var pattern in gamePatterns)
            {
                if (name.Contains(pattern))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Checks if process name contains creative/production patterns
        /// </summary>
        private static bool ContainsCreativePattern(string name)
        {
            // Rendering or heavy processing indicators
            string[] creativePatterns = new[]
            {
                "render", "encode", "convert", "compile",
                "build", "export", "bake", "simulate",
                "raytrace", "path", "trace", "denoise",
                "substance", "texture", "material",
                "shader", "lighting", "gi", "ao"
            };

            foreach (var pattern in creativePatterns)
            {
                if (name.Contains(pattern))
                    return true;
            }

            return false;
        }
    }
}
