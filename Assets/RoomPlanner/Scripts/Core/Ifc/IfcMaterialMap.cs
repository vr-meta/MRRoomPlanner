using System;

namespace RoomPlanner.Core.Ifc
{
    /// <summary>
    /// IFC material / surface-style name → id of a finish in our CC0 catalog
    /// (design/29 §3). Revit exports no textures at all and its colours are often
    /// placeholders (walnut veneer arrives rgb(127,127,127), concrete rgb(0,0,0)), so the
    /// NAME is the most reliable signal a file carries.
    ///
    /// Deliberately a table of substrings, not a parser: names come from Revit libraries
    /// in two languages with three different separators («Metal - Stainless Steel»,
    /// «Инсталляция_Рама», «Walnut wood veneer (F900)»). Order matters — the first hit
    /// wins, so specific entries («leather») stand before generic ones («textile»).
    ///
    /// Rule from issue #124: an unknown name gets NO finish. Better the file's honest
    /// colour than oak on a plastic chair.
    /// </summary>
    public static class IfcMaterialMap
    {
        /// <summary>What a name resolved to. <see cref="Tintable"/> marks white-based
        /// textures (plastic, ceramic, painted panel, plain fabric) that may be multiplied
        /// by the file's own colour — yellow plastic really is white plastic × yellow.
        /// Wood, leather and metal carry their colour in the texture and are never
        /// tinted.</summary>
        public readonly struct Match
        {
            public readonly string FinishId;
            public readonly bool Tintable;
            public Match(string finishId, bool tintable) { FinishId = finishId; Tintable = tintable; }
            public bool IsNone => string.IsNullOrEmpty(FinishId);
            public static readonly Match None = new(null, false);
        }

        // (any of these substrings) → (finish id, tintable). First match wins.
        private static readonly (string[] Keys, string Id, bool Tint)[] Table =
        {
            // ---- glass: no texture, the transparent material already says it all ----
            (new[] { "glass", "стекл" }, null, false),

            // ---- leather before textile: «Textile - Leather - Black» is leather ----
            (new[] { "leather black", "leather - black", "black leather" }, "leather-black", false),
            (new[] { "leather", "кожа", "кожзам" }, "leather-brown", false),

            // ---- textile, split by the colour word the name carries ----
            (new[] { "textile blue", "textile - slate", "slate blue", "ткань син", "ткань голуб" },
                "fabric-blue", false),
            (new[] { "textile", "fabric", "upholster", "ткан", "обивк", "велюр", "рогожк" },
                "fabric-grey", true),

            // ---- wood species ----
            (new[] { "walnut", "орех", "cherry", "вишн", "махагон", "mahogan" }, "wood-walnut", false),
            (new[] { "birch", "берёз", "берез", "maple", "клён", "клен", "ash", "ясен" },
                "wood-birch", false),
            (new[] { "wenge", "венге", "black brown", "stained", "морён", "морен", "dark wood",
                "wood dark" }, "wood-dark", false),
            (new[] { "oak", "дуб", "natural wood", "wood veneer", "шпон", "timber", "древес",
                "plywood", "фанер", "мдф", "mdf", "chipboard", "лдсп", "дсп" }, "wood-oak", false),

            // ---- ceramics and enamel: sanitary ware, tiled counters ----
            (new[] { "ceramic", "керамик", "porcelain", "фарфор", "фаянс", "эмаль", "enamel",
                "умывальник", "унитаз", "раковин", "sanitary" }, "ceramic-white", true),

            // ---- metals: painted first (its own look), then the bright ones ----
            (new[] { "powder", "painted black", "painted - black", "steel black",
                "steel - black", "metal black", "чёрный металл", "черный металл" },
                "metal-painted-black", false),
            (new[] { "alumin", "алюмин" }, "metal-aluminium", false),
            (new[] { "stainless", "нержав", "chrome", "хром", "polished steel", "смесител",
                "кран" }, "metal-steel", false),
            (new[] { "steel", "metal", "сталь", "металл", "железо", "iron", "опор", "рама",
                "profile", "профил" }, "metal-brushed", false),

            // ---- plastics: colour lives in the name more often than in the style ----
            (new[] { "plastic black", "plastic - black", "opaque black", "пластик чёрн",
                "пластик черн", "gasket", "уплотнит", "rubber", "резин" }, "plastic-black", false),
            (new[] { "plastic gray", "plastic - gray", "plastic grey", "plastic - grey",
                "пластик сер", "патрубок" }, "plastic-grey", false),
            (new[] { "clad - white", "clad white", "lacquer", "лак", "крашен", "painted" },
                "panel-white", true),
            (new[] { "plastic", "пластик", "полимер", "polymer", "acryl", "акрил", "пвх",
                "pvc" }, "plastic-white", true),

            // ---- mineral ----
            (new[] { "marble", "мрамор", "granite", "гранит" }, "marble-012", false),
            (new[] { "concrete", "бетон", "цемент", "cement" }, "concrete-034", false),
        };

        /// <summary>Finish for one IFC material/style name; <see cref="Match.None"/> when
        /// nothing in the table applies (keep the file's colour).</summary>
        public static Match Resolve(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return Match.None;
            string n = name.ToLowerInvariant();
            foreach (var (keys, id, tint) in Table)
                foreach (string key in keys)
                    if (n.IndexOf(key, StringComparison.Ordinal) >= 0)
                        return string.IsNullOrEmpty(id) ? Match.None : new Match(id, tint);
            return Match.None;
        }

        /// <summary>True when the name says «glass» — the element takes the see-through
        /// material even if the file forgot to set transparency (TV screens do).</summary>
        public static bool IsGlass(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            string n = name.ToLowerInvariant();
            return n.Contains("glass") || n.Contains("стекл");
        }
    }
}
