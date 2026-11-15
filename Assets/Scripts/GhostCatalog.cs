using System;
using System.Collections.Generic;

/// <summary>
/// Single source of truth for all 20 Pamahiin folklore ghost entities,
/// covering every unique 3-evidence combination of the 6 investigation proofs:
/// EMF, Freezing, GhostWriting, DOTS, SpiritBox, GhostOrbs (C(6,3) = 20).
/// </summary>
public static class GhostCatalog
{
    public sealed class GhostDefinition
    {
        public string Id { get; }
        public string Subtitle { get; }
        public string ScientificTag { get; }
        public string Lore { get; }
        public string[] Traits { get; }
        public string Tip { get; }
        public string ArtCssClass { get; }
        public EvidenceType[] Evidences { get; }
        public string[] EvidenceKeys { get; }

        public GhostDefinition(
            string id,
            string subtitle,
            string scientificTag,
            string lore,
            string[] traits,
            string tip,
            string artCssClass,
            EvidenceType ev1,
            EvidenceType ev2,
            EvidenceType ev3)
        {
            Id = id;
            Subtitle = subtitle;
            ScientificTag = scientificTag;
            Lore = lore;
            Traits = traits;
            Tip = tip;
            ArtCssClass = artCssClass;
            Evidences = new[] { ev1, ev2, ev3 };
            EvidenceKeys = new[]
            {
                EvidenceTypeToKey(ev1),
                EvidenceTypeToKey(ev2),
                EvidenceTypeToKey(ev3)
            };
        }
    }

    public static readonly EvidenceType[] SixProofs =
    {
        EvidenceType.EMF,
        EvidenceType.Freezing,
        EvidenceType.GhostWriting,
        EvidenceType.DOTS,
        EvidenceType.SpiritBox,
        EvidenceType.GhostOrbs
    };

    private static readonly GhostDefinition[] allGhosts =
    {
        // ── Prioritized Existing Ghost Types (1 - 3) ──────────────────────
        new GhostDefinition(
            "Tikbalang",
            "The Forest Trickster (Mapaglarong Nilalang)",
            "Fig 1.0 — Tikbalang (Equus daemonium)",
            "A towering half-human, half-horse demon of Philippine folklore. Roams dense forests and secluded properties, delighting in disorienting wanderers and leading them into endless loops.",
            new[]
            {
                "Provoked heavily by running and rapid investigator movement.",
                "Highly communicative; responds frequently through the Spirit Box.",
                "Emits intense electromagnetic surges and luminous Ghost Orbs."
            },
            "Tip: Invert your outerwear (baliktarin ang damit) if illusions cloud your path.",
            "ghost-art-tikbalang",
            EvidenceType.EMF, EvidenceType.SpiritBox, EvidenceType.GhostOrbs
        ),
        new GhostDefinition(
            "Kapre",
            "The Balete Giant (Higante sa Dilim)",
            "Fig 2.0 — Kapre (Gigantus balete)",
            "A colossal, dark-furred tree titan standing over seven feet tall. The Kapre dwells in ancient Balete trees and ceilings, perpetually smoking an amber-lit cigar that fills rooms with eerie supernatural haze.",
            new[]
            {
                "Extremely territorial; drains investigator sanity rapidly when near.",
                "Radiates severe supernatural cold, plunging temperatures below zero.",
                "Towering spectral form easily reveals itself in D.O.T.S. laser beams."
            },
            "Tip: A faint aroma of burning tobacco in the air signals its immediate presence.",
            "ghost-art-kapre",
            EvidenceType.EMF, EvidenceType.Freezing, EvidenceType.DOTS
        ),
        new GhostDefinition(
            "Manananggal",
            "The Severed Predator (Lumilipad na Aswang)",
            "Fig 3.0 — Manananggal (Vampyrus volans)",
            "The most feared vampiric demon of Philippine folklore. By night, it detaches its upper torso from its waist and takes flight with massive bat wings, descending from the rafters upon unsuspecting prey.",
            new[]
            {
                "Lethal hunter with rapid pursuit speed once a hunt commences.",
                "Chills ambient room temperatures into sub-zero freezing ranges.",
                "Obsessively scrawls demonic verses inside Ghost Writing Books.",
                "Floats silently through hallways shedding luminous Ghost Orbs."
            },
            "Tip: Listen closely for leathery wing flaps and rooftop ticking noises.",
            "ghost-art-manananggal",
            EvidenceType.Freezing, EvidenceType.GhostWriting, EvidenceType.GhostOrbs
        ),

        // ── 17 Folklore Ghost Types Completing All 20 Proof Combinations ────
        new GhostDefinition(
            "Multo",
            "The Restless Spirit (Ligaw na Kaluluwa)",
            "Fig 4.0 — Multo (Umbra anima)",
            "The quintessential lingering spirit of a departed soul bound to its place of death or unfinished sorrow. Appears as a translucent figure drifting through dimly lit hallways.",
            new[]
            {
                "Plummets ambient room temperatures when manifesting near investigators.",
                "Whispers mournful pleas and fragments of memory over the Spirit Box.",
                "Sheds pale, slow-drifting Ghost Orbs throughout its favorite room."
            },
            "Tip: Pay close attention to sudden cold spots and whispered static in the dark.",
            "ghost-art-multo",
            EvidenceType.Freezing, EvidenceType.SpiritBox, EvidenceType.GhostOrbs
        ),
        new GhostDefinition(
            "Kaperosa",
            "White Lady (Kaperosa sa Daan)",
            "Fig 5.0 — Kaperosa (Domina alba)",
            "A tragic spectral woman draped in a flowing white shroud, famously sighted along haunted lanes and shadowed corridors. Her face remains veiled behind long dark hair as she glides above the floorboards.",
            new[]
            {
                "Leaves an icy funeral chill wherever her spectral veil passes.",
                "Glides swiftly across D.O.T.S. laser grids in a long white silhouette.",
                "Trails luminous Ghost Orbs visible only through night-vision optics."
            },
            "Tip: Watch doorways and hallways via Video Camera—she favors silent optical sightings.",
            "ghost-art-kaperosa",
            EvidenceType.Freezing, EvidenceType.DOTS, EvidenceType.GhostOrbs
        ),
        new GhostDefinition(
            "Pasatsat",
            "The Shrouded Corpse (Balot sa Banig)",
            "Fig 6.0 — Pasatsat (Cadaver involutum)",
            "A vengeful wartime apparition of Pangasinan lore wrapped tightly in a woven reed mat (banig) from head to toe. Said to materialize in dark corridors and block passages until its remains find peace.",
            new[]
            {
                "Radiates grave-like cold that freezes breath upon close contact.",
                "Claws bitter warnings and tally marks into Ghost Writing Books.",
                "Its stiff, mat-wrapped form stands starkly within D.O.T.S. projections."
            },
            "Tip: Never trap yourself in narrow hallways; deploy D.O.T.S. at room choke points.",
            "ghost-art-pasatsat",
            EvidenceType.Freezing, EvidenceType.GhostWriting, EvidenceType.DOTS
        ),
        new GhostDefinition(
            "Kalag",
            "The Wandering Soul (Kalag ng Yumao)",
            "Fig 7.0 — Kalag (Spiritus vagans)",
            "In Visayan tradition, a disembodied soul lingering inside ancestral homes after death, seeking recognition and remembrance from the living before crossing over.",
            new[]
            {
                "Eagerly inscribes personal names and farewells into Ghost Writing Books.",
                "Answers direct questions clearly through EVP Spirit Box sweeps.",
                "Emits faint Ghost Orbs near doorways and family belongings."
            },
            "Tip: Place an open Writing Book beside an active Spirit Box to provoke fast replies.",
            "ghost-art-kalag",
            EvidenceType.GhostWriting, EvidenceType.SpiritBox, EvidenceType.GhostOrbs
        ),
        new GhostDefinition(
            "Kumakatok",
            "The Omen Knockers (Tagakatok ng Tadhana)",
            "Fig 8.0 — Kumakatok (Pulsator mortis)",
            "Hooded figures said to approach doors in the dead of night, rapping thrice upon timber thresholds as an ancient omen foretelling tragedy for the household within.",
            new[]
            {
                "Triggers sharp EMF Level 5 spikes whenever interacting with doors.",
                "Records ominous verses inside open Ghost Writing Books.",
                "Chants solemn warnings in hushed cadences over the Spirit Box."
            },
            "Tip: Sweep doors with an EMF Reader immediately after hearing distant knocks.",
            "ghost-art-kumakatok",
            EvidenceType.EMF, EvidenceType.GhostWriting, EvidenceType.SpiritBox
        ),
        new GhostDefinition(
            "Akop",
            "The Clinging Dread (Mapang-akit na Anino)",
            "Fig 9.0 — Akop (Tenebra adhaerens)",
            "A sinister nocturnal spirit known for latching onto solitary travelers from behind, weighing down their limbs while draining warmth and vitality from the air.",
            new[]
            {
                "Spikes EMF readings to Level 5 during sudden proximity manifestations.",
                "Drains ambient heat rapidly, forcing room temperatures below zero.",
                "Surrounds itself with dense clusters of cold Ghost Orbs."
            },
            "Tip: Avoid exploring deep interiors alone—isolated investigators lose sanity faster.",
            "ghost-art-akop",
            EvidenceType.EMF, EvidenceType.Freezing, EvidenceType.GhostOrbs
        ),
        new GhostDefinition(
            "Ibwa",
            "The Grave Stalker (Anino sa Lamay)",
            "Fig 10.0 — Ibwa (Necrophagus umbra)",
            "An elusive Ilocano shadow-entity that prowls wakes and isolated dwellings, drawn by grief and leaving behind unnatural mortuary frost and disturbed rooms.",
            new[]
            {
                "Produces intense EMF Level 5 surges around doors and thrown props.",
                "Brings biting mortuary frost into its favored room.",
                "Fills Ghost Writing Books with jagged, frantic markings."
            },
            "Tip: Track sudden Thermometer drops when objects are hurled nearby.",
            "ghost-art-ibwa",
            EvidenceType.EMF, EvidenceType.Freezing, EvidenceType.GhostWriting
        ),
        new GhostDefinition(
            "Paring Pugot",
            "Headless Priest (Paring Walang Ulo)",
            "Fig 11.0 — Paring Pugot (Sacerdos acephalus)",
            "The spectral remnant of a decapitated colonial-era friar who stalks stone corridors and old estates, repeating unfinished rites in eerie silence.",
            new[]
            {
                "Disrupts electronics with strong EMF Level 5 anomalies.",
                "Inscribes Latin prayers and penance lines into Ghost Writing Books.",
                "Its headless cassock silhouette becomes unmistakable inside D.O.T.S. beams."
            },
            "Tip: Pair a Ghost Writing Book with a D.O.T.S. Projector aimed down central halls.",
            "ghost-art-paring-pugot",
            EvidenceType.EMF, EvidenceType.GhostWriting, EvidenceType.DOTS
        ),
        new GhostDefinition(
            "Amalanhig",
            "The Risen Corpse (Buhay na Bangkay)",
            "Fig 12.0 — Amalanhig (Mortuus redivivus)",
            "A feared Visayan undead entity—an unburied or cursed corpse risen with stiff limbs and relentless hunger, known to pursue the living tirelessly on foot.",
            new[]
            {
                "Emits violent EMF Level 5 bursts when aggressively slamming doors.",
                "Exhales corpse-cold drafts that drive thermometers below freezing.",
                "Rasps guttural threats and breathless murmurs into the Spirit Box."
            },
            "Tip: Watch for relentless pursuits—break line of sight early and take cover.",
            "ghost-art-amalanhig",
            EvidenceType.EMF, EvidenceType.Freezing, EvidenceType.SpiritBox
        ),
        new GhostDefinition(
            "Santelmo",
            "Apoy ni San Telmo (Spirit Fire)",
            "Fig 13.0 — Santelmo (Ignis fatuus)",
            "A crackling orb of supernatural fire that hovers over fields, riverbanks, and haunted rafters, luring wandering travelers off safe paths into deadly disorientation.",
            new[]
            {
                "Generates severe EMF Level 5 interference across nearby sensors.",
                "Flashes vividly as a seared silhouette through D.O.T.S. laser matrices.",
                "Leaves glowing, ember-like Ghost Orbs drifting on night-vision feeds."
            },
            "Tip: Do not dismiss flickering lights as faulty wiring—verify with EMF and Camera.",
            "ghost-art-santelmo",
            EvidenceType.EMF, EvidenceType.DOTS, EvidenceType.GhostOrbs
        ),
        new GhostDefinition(
            "Bagat",
            "The Crossroads Omen (Halimaw sa Dilim)",
            "Fig 14.0 — Bagat (Bestia noctis)",
            "A monstrous nocturnal omen of Visayan folklore that lurks along dark thresholds and crossroads, shifting into towering beastly shapes to block those who pass after midnight.",
            new[]
            {
                "Creates erratic EMF Level 5 spikes around entryways and hallways.",
                "Casts a prowling, beast-like shadow inside D.O.T.S. laser grids.",
                "Growls distorted warnings directly through Spirit Box channels."
            },
            "Tip: Keep a Crucifix near main thresholds before probing the Spirit Box.",
            "ghost-art-bagat",
            EvidenceType.EMF, EvidenceType.DOTS, EvidenceType.SpiritBox
        ),
        new GhostDefinition(
            "Mantiw",
            "The Towering Specter (Matangkad na Multo)",
            "Fig 15.0 — Mantiw (Gigantus umbra)",
            "A gaunt, impossibly tall spirit of Negros and Panay folklore that leans against rooftops and wooden walls, peering silently down at residents through upper windows.",
            new[]
            {
                "Leaves wide, sweeping occult glyphs across Ghost Writing Books.",
                "Its elongated silhouette towers unmistakably across D.O.T.S. grids.",
                "Surrounds ceilings and corners with hovering Ghost Orbs."
            },
            "Tip: Angle Video Cameras wide across the ghost room to spot both D.O.T.S. and Orbs.",
            "ghost-art-mantiw",
            EvidenceType.GhostWriting, EvidenceType.DOTS, EvidenceType.GhostOrbs
        ),
        new GhostDefinition(
            "Batibat",
            "The Nightmare Hag (Tagapagdala ng Bangungot)",
            "Fig 16.0 — Batibat (Incubus arboris)",
            "A vengeful tree demon angered when its timber is felled into house posts. At night she sits heavily upon sleepers' chests, inducing suffocating nightmares (bangungot).",
            new[]
            {
                "Chills sleeping quarters and bedrooms into freezing temperatures.",
                "Scratches feverish nightmare warnings into Ghost Writing Books.",
                "Speaks suffocating whispers into the Spirit Box when lights are out."
            },
            "Tip: Keep room lights active whenever possible to preserve investigator sanity.",
            null,
            EvidenceType.Freezing, EvidenceType.GhostWriting, EvidenceType.SpiritBox
        ),
        new GhostDefinition(
            "Tiyanak",
            "The Crying Fiend (Demonyong Sanggol)",
            "Fig 17.0 — Tiyanak (Infans daemon)",
            "A malevolent vampiric demon disguised as an abandoned, weeping infant. Once an unsuspecting victim draws near to comfort it, it transforms into a feral, razor-fanged predator.",
            new[]
            {
                "Drops surrounding air to freezing temperatures prior to ambushes.",
                "Darts rapidly across D.O.T.S. laser grids with a low, crouched silhouette.",
                "Mimics infant cries and sudden snarls over the Spirit Box."
            },
            "Tip: Stay alert for deceptively small silhouettes and sudden Spirit Box cries.",
            "ghost-art-tiyanak",
            EvidenceType.Freezing, EvidenceType.DOTS, EvidenceType.SpiritBox
        ),
        new GhostDefinition(
            "Tamawo",
            "The Fair Trickster (Engkanto ng Kalikasan)",
            "Fig 18.0 — Tamawo (Fata occulta)",
            "Otherworldly, fair-skinned nature spirits of Visayan folklore who dwell in secluded groves and old estates, enchanting or misleading mortals who trespass without respect.",
            new[]
            {
                "Shimmers cleanly inside D.O.T.S. projections despite remaining unseen.",
                "Whispers enchanting, mocking riddles through the Spirit Box.",
                "Surrounds its domain with radiant, drifting Ghost Orbs."
            },
            "Tip: Confirm all optical signs from the truck or hallway before risking an exorcism.",
            "ghost-art-tamawo",
            EvidenceType.DOTS, EvidenceType.SpiritBox, EvidenceType.GhostOrbs
        ),
        new GhostDefinition(
            "Mangkukutod",
            "The Hex Weaver (Sumpa ng Dilim)",
            "Fig 19.0 — Mangkukutod (Maleficus umbra)",
            "A sinister occult entity bound to forbidden curses and dark rituals, feeding on dread inside shuttered rooms and leaving hex marks on personal belongings.",
            new[]
            {
                "Radiates intense EMF Level 5 charges when tampering with cursed objects.",
                "Fills Ghost Writing Books with intricate hex circles and sigils.",
                "Produces eerie, flickering Ghost Orbs around ritual spaces."
            },
            "Tip: Place your Ghost Writing Book early; this entity prioritizes occult inscriptions.",
            "ghost-art-mangkukutod",
            EvidenceType.EMF, EvidenceType.GhostWriting, EvidenceType.GhostOrbs
        ),
        new GhostDefinition(
            "Tagamaling",
            "The Lunar Fiend (Halimaw sa Bilog ng Buwan)",
            "Fig 20.0 — Tagamaling (Daemon lunaris)",
            "A ferocious shapeshifting giant from Mindanao folklore that alternates between calm dormancy and bloodthirsty nocturnal hunts under shifting moon phases.",
            new[]
            {
                "Gashes deep, erratic warnings across Ghost Writing Books.",
                "Lunges across D.O.T.S. laser fields with a broad, menacing silhouette.",
                "Echoes primal roars and guttural commands over the Spirit Box."
            },
            "Tip: Once Spirit Box aggression escalates, keep a clear escape route to cover.",
            null,
            EvidenceType.GhostWriting, EvidenceType.DOTS, EvidenceType.SpiritBox
        )
    };

    private static readonly Dictionary<string, GhostDefinition> byId = BuildLookup();

    private static Dictionary<string, GhostDefinition> BuildLookup()
    {
        var map = new Dictionary<string, GhostDefinition>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < allGhosts.Length; i++)
        {
            map[allGhosts[i].Id] = allGhosts[i];
        }
        return map;
    }

    public static IReadOnlyList<GhostDefinition> AllGhosts => allGhosts;

    public static bool TryGetGhost(string ghostName, out GhostDefinition ghost)
    {
        if (string.IsNullOrEmpty(ghostName))
        {
            ghost = null;
            return false;
        }
        return byId.TryGetValue(ghostName, out ghost);
    }

    public static GhostDefinition GetRandomGhost()
    {
        int index = UnityEngine.Random.Range(0, allGhosts.Length);
        return allGhosts[index];
    }

    public static Dictionary<string, HashSet<string>> BuildJournalEvidenceMap()
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < allGhosts.Length; i++)
        {
            var g = allGhosts[i];
            map[g.Id] = new HashSet<string>(g.EvidenceKeys);
        }
        return map;
    }

    public static string EvidenceTypeToKey(EvidenceType type)
    {
        switch (type)
        {
            case EvidenceType.EMF: return "EMF";
            case EvidenceType.Freezing: return "Freezing";
            case EvidenceType.GhostWriting: return "GhostWriting";
            case EvidenceType.DOTS: return "DOTS";
            case EvidenceType.SpiritBox: return "SpiritBox";
            case EvidenceType.GhostOrbs: return "GhostOrbs";
            default: return type.ToString();
        }
    }

    public static string EvidenceTypeToDisplayLabel(EvidenceType type)
    {
        switch (type)
        {
            case EvidenceType.EMF: return "EMF Level 5";
            case EvidenceType.Freezing: return "Freezing Temps";
            case EvidenceType.GhostWriting: return "Ghost Writing";
            case EvidenceType.DOTS: return "D.O.T.S. Projector";
            case EvidenceType.SpiritBox: return "Spirit Box";
            case EvidenceType.GhostOrbs: return "Ghost Orbs";
            default: return type.ToString();
        }
    }
}
