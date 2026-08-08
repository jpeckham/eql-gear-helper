using System.Collections.Generic;

static class RankingProfiles
{
    public static readonly Dictionary<string, Dictionary<string, double>> ClassStatWeights =
        new(StringComparer.OrdinalIgnoreCase)
        {
            {
                "Enchanter",
                new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    { "INT", 2.0 },
                    { "WIS", 1.4 },
                    { "MANA", 1.2 },
                    { "STA", 0.5 },
                    { "AC", 0.3 }
                }
            },
            {
                "Magician",
                new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    { "INT", 2.0 },
                    { "WIS", 1.4 },
                    { "MANA", 1.2 },
                    { "STA", 0.5 },
                    { "AC", 0.3 }
                }
            },
            {
                "Necromancer",
                new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    { "INT", 1.9 },
                    { "WIS", 1.2 },
                    { "MANA", 1.1 },
                    { "STA", 0.5 },
                    { "AC", 0.3 }
                }
            },
            {
                "Wizard",
                new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    { "INT", 2.0 },
                    { "WIS", 1.5 },
                    { "MANA", 1.2 },
                    { "STA", 0.5 },
                    { "AC", 0.3 }
                }
            },
            {
                "Cleric",
                new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    { "WIS", 1.8 },
                    { "INT", 1.3 },
                    { "MANA", 1.1 },
                    { "STA", 0.4 },
                    { "AC", 0.4 }
                }
            },
            {
                "Druid",
                new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    { "WIS", 1.8 },
                    { "INT", 1.3 },
                    { "MANA", 1.1 },
                    { "STA", 0.4 },
                    { "AC", 0.4 }
                }
            },
            {
                "Shaman",
                new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    { "WIS", 1.6 },
                    { "INT", 1.2 },
                    { "MANA", 1.0 },
                    { "STA", 0.4 },
                    { "AC", 0.4 }
                }
            }
        };
}
