using System.Collections.Generic;

static class RankingProfiles
{
    public sealed record class ClassAxisWeights(
        Dictionary<string, double> DpsWeights,
        Dictionary<string, double> SustainWeights);

    public static readonly Dictionary<string, ClassAxisWeights> ClassStatProfiles =
        new(StringComparer.OrdinalIgnoreCase)
        {
            {
                "Bard",
                new ClassAxisWeights(
                    new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "CHA", 1.2 },
                        { "INT", 1.0 },
                        { "WIS", 0.6 },
                        { "MANA", 0.8 }
                    },
                    new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "AC", 0.5 },
                        { "STA", 0.7 },
                        { "HP", 0.6 },
                        { "WIS", 0.6 },
                        { "MANA", 0.4 }
                    })
            },
            {
                "Beastlord",
                new ClassAxisWeights(
                    new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "STR", 1.4 },
                        { "AGI", 1.2 },
                        { "WIS", 0.9 },
                        { "INT", 0.8 },
                        { "STA", 0.7 }
                    },
                    new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "STA", 1.0 },
                        { "HP", 0.9 },
                        { "AC", 0.4 },
                        { "WIS", 0.4 },
                        { "INT", 0.2 },
                        { "MANA", 0.4 }
                    })
            },
            {
                "Berserker",
                new ClassAxisWeights(
                    new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "STR", 1.5 },
                        { "DEX", 0.8 },
                        { "STA", 0.8 }
                    },
                    new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "HP", 1.0 },
                        { "STA", 1.0 },
                        { "AC", 0.5 },
                        { "STR", 0.3 }
                    })
            },
            {
                "Enchanter",
                new ClassAxisWeights(
                    new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "INT", 2.0 },
                        { "WIS", 1.5 },
                        { "MANA", 1.0 }
                    },
                    new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "MANA", 0.8 },
                        { "WIS", 0.6 },
                        { "AC", 0.3 },
                        { "STA", 0.5 },
                        { "INT", 0.2 }
                    })
            },
            {
                "Magician",
                new ClassAxisWeights(
                    new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "INT", 2.0 },
                        { "WIS", 1.5 },
                        { "MANA", 1.0 }
                    },
                    new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "MANA", 0.8 },
                        { "WIS", 0.6 },
                        { "AC", 0.3 },
                        { "STA", 0.5 },
                        { "INT", 0.2 }
                    })
            },
            {
                "Necromancer",
                new ClassAxisWeights(
                    new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "INT", 1.9 },
                        { "WIS", 1.2 },
                        { "MANA", 1.0 }
                    },
                    new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "MANA", 0.7 },
                        { "WIS", 0.6 },
                        { "AC", 0.3 },
                        { "STA", 0.4 }
                    })
            },
            {
                "Wizard",
                new ClassAxisWeights(
                    new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "INT", 2.0 },
                        { "WIS", 1.5 },
                        { "MANA", 1.0 }
                    },
                    new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "MANA", 0.8 },
                        { "WIS", 0.7 },
                        { "AC", 0.3 },
                        { "STA", 0.4 }
                    })
            },
            {
                "Cleric",
                new ClassAxisWeights(
                    new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "WIS", 1.8 },
                        { "INT", 1.3 },
                        { "MANA", 1.1 }
                    },
                    new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "WIS", 0.7 },
                        { "STA", 0.6 },
                        { "AC", 0.4 },
                        { "MANA", 0.7 }
                    })
            },
            {
                "Druid",
                new ClassAxisWeights(
                    new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "WIS", 1.8 },
                        { "INT", 1.3 },
                        { "MANA", 1.1 }
                    },
                    new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "STA", 0.6 },
                        { "HP", 0.5 },
                        { "WIS", 0.7 },
                        { "AC", 0.4 },
                        { "MANA", 0.7 }
                    })
            },
            {
                "Shaman",
                new ClassAxisWeights(
                    new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "WIS", 1.6 },
                        { "INT", 1.2 },
                        { "MANA", 1.0 }
                    },
                    new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "STA", 0.6 },
                        { "AC", 0.4 },
                        { "MANA", 0.6 },
                        { "WIS", 0.6 }
                    })
            },
            {
                "Monk",
                new ClassAxisWeights(
                    new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "AGI", 1.3 },
                        { "DEX", 1.2 },
                        { "STR", 0.8 }
                    },
                    new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "STR", 0.2 },
                        { "AGI", 0.4 },
                        { "STA", 0.8 },
                        { "AC", 0.5 },
                        { "HP", 0.8 }
                    })
            },
            {
                "Shadow Knight",
                new ClassAxisWeights(
                    new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "STR", 1.3 },
                        { "DEX", 0.8 },
                        { "STA", 0.6 },
                        { "WIS", 0.4 }
                    },
                    new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "HP", 1.0 },
                        { "STA", 0.9 },
                        { "AC", 0.5 },
                        { "STR", 0.4 }
                    })
            },
            {
                "Ranger",
                new ClassAxisWeights(
                    new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "AGI", 1.3 },
                        { "DEX", 1.2 },
                        { "STR", 0.9 }
                    },
                    new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "AC", 0.5 },
                        { "STA", 0.7 },
                        { "HP", 0.6 },
                        { "AGI", 0.4 }
                    })
            },
            {
                "Rogue",
                new ClassAxisWeights(
                    new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "DEX", 1.4 },
                        { "AGI", 1.2 },
                        { "STR", 1.0 }
                    },
                    new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "AC", 0.5 },
                        { "STA", 0.6 },
                        { "HP", 0.6 },
                        { "DEX", 0.3 }
                    })
            },
            {
                "Warrior",
                new ClassAxisWeights(
                    new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "STR", 1.5 },
                        { "STA", 1.0 },
                        { "DEX", 0.6 }
                    },
                    new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "HP", 0.9 },
                        { "AC", 0.5 },
                        { "STA", 1.0 }
                    })
            },
            {
                "Paladin",
                new ClassAxisWeights(
                    new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "STR", 1.2 },
                        { "WIS", 0.8 },
                        { "MANA", 0.6 }
                    },
                    new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "AC", 0.6 },
                        { "STA", 0.8 },
                        { "HP", 0.8 },
                        { "WIS", 0.6 },
                        { "MANA", 0.4 }
                    })
            }
        };
}
