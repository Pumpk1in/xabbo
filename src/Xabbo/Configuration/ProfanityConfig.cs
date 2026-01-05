using System.Collections.ObjectModel;
using ReactiveUI;

namespace Xabbo.Configuration;

public sealed class ProfanityConfig : ReactiveObject
{
    /// <summary>
    /// Whether profanity detection is enabled.
    /// </summary>
    [Reactive] public bool Enabled { get; set; } = true;

    /// <summary>
    /// Custom words added by the user (separate from default words).
    /// Only these are saved to settings.json - default words are not persisted.
    /// </summary>
    [Reactive] public ObservableCollection<string> CustomWords { get; set; } = [];

    /// <summary>
    /// Combined list of all words (default + custom) - not persisted.
    /// Use this property for detection. For adding/removing custom words, use CustomWords.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IEnumerable<string> AllWords => DefaultWords.Concat(CustomWords);

    /// <summary>
    /// Default profanity words (French and English).
    /// The service automatically handles obfuscation variants (c0nne, c*nne, etc.)
    /// </summary>
    public static readonly string[] DefaultWords =
    [
        // French
        "abruti", "andouille", "avorton",
        "batard", "bâtard", "beauf", "biatch", "bicot", "bite", "bitembois", "bordel", "bouffon",
        "bougnoule", "bougnouliser", "bougre", "boukak", "bounioul", "bourdille", "bouseux",
        "branler", "branleur", "branque", "brise-burnes",
        "casse-bonbon", "casse-couille", "casse-couilles", "cacou", "cafre", "caldoche",
        "chier", "chieur", "chieurs", "chiennasse", "chinetoc", "chinetoque", "chintok", "chleuh", "chnoque",
        "coche", "con", "conard", "conasse", "conchier", "connard", "connarde", "connasse", "conne",
        "couille", "couilles", "couillon", "couillonner", "counifle", "courtaud",
        "cretin", "crétin", "crevard", "crevure", "cricri", "crotte", "crotté", "crouillat", "crouille", "croûton", "cul",
        "debile", "débile", "deguelasse", "déguelasse", "demerder", "démerder", "drouille",
        "ducon", "duconnot", "dugenoux", "dugland", "duschnock",
        "emmanche", "emmanché", "emmerder", "emmerdeur", "emmerdeuse",
        "empafe", "empafé", "empapaoute", "empapaouté",
        "encule", "enculé", "enculer", "enculeur", "enflure", "enfoire", "enfoiré", "envaselineur",
        "epais", "épais", "espingoin", "etron", "étron",
        "fdp", "fiotte", "fouteur", "foutre", "fritz", "fumier",
        "garce", "gaupe", "gdm", "gland", "glandeur", "glandeuse", "glandouillou", "glandu",
        "gnoul", "gnoule", "godon", "gogol", "goï", "gouilland", "gouine", "gourde",
        "gourgandine", "grognasse", "guindoule", "gueniche",
        "imbecile", "imbécile",
        "jean-foutre",
        "kraut",
        "lacheux", "lâcheux", "lavette", "lopette",
        "makoume", "makoumé", "manche", "mange-merde", "marchandot", "margouilliste",
        "mauviette", "merdaillon", "merdaille", "merde", "merdeux", "merdouillard",
        "michto", "minable", "minus", "miserable", "misérable",
        "moinaille", "moins-que-rien", "monacaille", "moricaud","mort",
        "niaiseux", "niac", "niakoue", "niakoué", "nique", "niquer", "negro", "négro", "ntm", "ntgm",
        "pakos", "panoufle", "patarin", "pecque", "pedale", "pédale", "pede", "pédé", "pedoque", "pédoque",
        "pequenaud", "péquenaud", "pet", "petasse", "pétasse", "peteux", "péteux",
        "pignoufe", "pimbêche", "pisseux", "pissou", "pleutre", "plouc",
        "porcasse", "poucav", "pouf", "poufiasse", "pouffiasse", "pounde", "poundé", "pourriture",
        "punaise", "putain", "pute",
        "queutard",
        "raclure", "raton", "ripopee", "ripopée", "robespierrot", "rosbif", "roulure",
        "sagouin", "salaud", "sale", "salop", "salope", "salopard", "saloperie", "satrouille",
        "schbeb", "schleu", "schnoc", "schnock", "schnoque", "sent-la-pisse",
        "sottiseux", "sous-merde", "stearique", "stéarique",
        "tafiole", "tantouse", "tantouserie", "tantouze", "tapette", "tarlouze",
        "tebe", "tebé", "teteux", "téteux", "teube", "teubé", "tocard",
        "trainee", "traînée", "trouduc", "truiasse",
        "vaurien", "viedase", "viédase", "vier", "vide-couilles",
        "xeropineur", "xéropineur",
        "yeule", "youd", "youpin", "youpine", "youpinisation", "youtre",
        "zguegue", "zguègue",

        // English
        "ahole", "anus", "asshole", "asswipe",
        "bastard", "bitch", "bitches", "blowjob", "boffing", "boobs", "butthole", "buttwipe",
        "chink", "clit", "clits", "cock", "cockhead", "cocksucker", "crap", "cum", "cunt", "cunts",
        "damn", "dick", "dildo", "dildos", "dyke",
        "enema", "ejaculate",
        "fag", "faggot", "fags", "fart", "fatass", "fuck", "fucker", "fucking", "fucks",
        "gay", "gook",
        "hell", "hoar", "hooker", "hore", "whore",
        "jackoff", "jap", "japs", "jerk-off", "jism", "jizz",
        "kike", "knob", "kraut", "kunt",
        "lesbian", "lesbo",
        "masochist", "masturbate", "mofo", "motherfucker",
        "nazi", "nigga", "nigger", "nutsack",
        "orgasm", "orifice",
        "paki", "pecker", "penis", "phuck", "piss", "poop", "porn", "preteen", "prick", "pussy", "puta", "puto",
        "queer", "queef",
        "rectum", "retard",
        "sadist", "scank", "schlong", "screw", "screwing", "scrotum", "semen", "sex", "sexy",
        "shemale", "shit", "shits", "shitter", "shitty", "skank", "skanky", "slag", "slut", "sluts", "slutty", "smut", "spic", "splooge",
        "testicle", "tit", "tits", "turd", "twat",
        "vagina", "vulva",
        "wank", "wetback", "whore", "wop",
        "xxx"
    ];
}
