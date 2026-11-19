using System;
using System.Collections.Generic;

namespace GOA.Sim
{
    public sealed partial class World
    {
        public const long StartTime = 6 * SimTime.Hour;

        void Generate()
        {
            var rng = new SeqRandom(Seed);
            VillageLayout.BuildNav(Nav);

            foreach (var def in VillageLayout.Civic)
            {
                var b = AddBuilding(def.type, def.pos, def.size, Nav[def.node], def.name);
                b.flammable = def.flammable;
                if (!def.initial) { b.buildProgress = 0; b.destroyed = true; } // not yet founded
                b.rotation = FacingTo(def.pos, Nav.Node(Nav[def.node]).pos);
                if (def.type == BuildingType.Haystack || def.type == BuildingType.Well) b.rotation = rng.RangeF(0, 6.28f);
            }

            // houses on initial lots
            var initialHouses = new List<Building>();
            foreach (var lot in VillageLayout.InitialLots)
                initialHouses.Add(CreateHouseOnLot(lot, 0, rng.Range(0, 1000)));

            foreach (var lot in VillageLayout.ExpansionLots)
                if (LotIsClear(lot.pos)) FreeLots.Add(lot);

            // props
            for (int i = 0; i < VillageLayout.PebbleSpots.Length; i++)
                AddProp(PropType.Pebble, VillageLayout.PebbleSpots[i], true, "Pebble");
            AddProp(PropType.Lantern, VillageLayout.LanternPos, true, "Tavern lantern");
            AddProp(PropType.Cart, VillageLayout.CartStart, true, "Hay cart (wheel chock)");
            var nest = AddProp(PropType.CrowNest, VillageLayout.OakPos, false, "Crow's nest in the old oak");
            nest.hoard = new List<ItemType> { ItemType.Lens, ItemType.Coin, ItemType.Key, ItemType.Coin };
            rng.Shuffle(nest.hoard);
            if (Scenario.lensFirstInNest) { nest.hoard.Remove(ItemType.Lens); nest.hoard.Insert(0, ItemType.Lens); }

            GeneratePeople(rng, initialHouses);

            Food = Population * 9f;
            Wood = 40f;
            Energy = Scenario.startEnergy;
            Time = StartTime;
            RollWeather(0);

            foreach (var c in Citizens) Decide(c);
            Schedule(SimTime.Day, TaskKind.DailyTick);
            Schedule(Time + 30 * SimTime.Minute, TaskKind.NoticeScan);
        }

        static float FacingTo(Vec2 from, Vec2 to) => (to - from).Angle;

        public Building AddBuilding(BuildingType type, Vec2 pos, Vec2 size, int nodeId, string name)
        {
            var b = new Building { id = Buildings.Count, type = type, pos = pos, size = size, nodeId = nodeId, name = name };
            b.flammable = type != BuildingType.Well;
            Buildings.Add(b);
            return b;
        }

        Building CreateHouseOnLot(LotDef lot, int tier, int variant)
        {
            int link = Nav[lot.linkNode];
            Vec2 linkPos = Nav.Node(link).pos;
            Vec2 dir = (linkPos - lot.pos).Normalized;
            Vec2 doorPos = lot.pos + dir * 3.6f;
            int door = Nav.Add("Door_" + Buildings.Count, doorPos.x, doorPos.z, NodeKind.Door, 1.2f);
            Nav.Link(door, link);
            var b = AddBuilding(BuildingType.House, lot.pos, new Vec2(6, 5), door, "Cottage");
            b.rotation = dir.Angle;
            b.tier = tier;
            b.variant = variant;
            return b;
        }

        bool LotIsClear(Vec2 p)
        {
            foreach (var b in Buildings)
            {
                float r = Math.Max(b.size.x, b.size.z) * 0.5f + 4.5f;
                if (Vec2.Distance(b.pos, p) < r) return false;
            }
            foreach (var n in Nav.nodes)
                foreach (var l in n.links)
                {
                    if (l < n.id) continue;
                    if (Vec2.DistanceToSegment(p, n.pos, Nav.Node(l).pos, out _) < 4.2f) return false;
                }
            if (VillageLayout.DistanceToRiver(p) < VillageLayout.RiverHalfWidth + 6) return false;
            foreach (var f in VillageLayout.Fields)
                if (Math.Abs(p.x - f.center.x) < f.size.x * 0.5f + 4 && Math.Abs(p.z - f.center.z) < f.size.z * 0.5f + 4) return false;
            return true;
        }

        Prop AddProp(PropType type, Vec2 pos, bool nudgeable, string label)
        {
            var p = new Prop { id = Props.Count, type = type, pos = pos, originalPos = pos, nudgeable = nudgeable, label = label };
            if (type == PropType.Pebble) p.radius = 0.9f;
            if (type == PropType.GroundItem) p.radius = 1.3f;
            Props.Add(p);
            PropVersion[p.id] = 0;
            return p;
        }

        // ------------------------------------------------------------------ people

        Citizen NewCitizen(string first, string family, bool female, int ageYears, Occupation occ, Trait traits, int home, SeqRandom rng)
        {
            var c = new Citizen
            {
                id = Citizens.Count,
                firstName = first,
                familyName = family,
                female = female,
                birthTime = StartTime - (long)ageYears * SimTime.Year - rng.Range(0, SimTime.DaysPerYear) * SimTime.Day,
                occupation = occ,
                traits = traits,
                homeBuildingId = home,
                craft = rng.RangeF(5, 25),
                lore = rng.RangeF(3, 20),
                charm = rng.RangeF(10, 50),
                strength = rng.RangeF(20, 60),
                wealth = rng.Range(2, 15),
                skinTone = rng.Range(0, 6),
                hairColor = rng.Range(0, 7),
                outfitSeed = rng.Range(0, 100000),
            };
            if (c.Has(Trait.Charismatic)) c.charm += 25;
            if (c.Has(Trait.Curious)) c.lore += 8;
            if (ageYears < 14) { c.craft *= 0.3f; c.lore *= 0.4f; }
            Citizens.Add(c);
            return c;
        }

        static readonly Trait[] TraitPool =
        {
            Trait.Curious, Trait.Kind, Trait.Greedy, Trait.Brave, Trait.Timid, Trait.Ambitious, Trait.Romantic,
            Trait.Pious, Trait.Clumsy, Trait.Charismatic, Trait.Reckless, Trait.Diligent
        };

        public static Trait RollTraits(Func<int, double> roll, int count)
        {
            Trait t = Trait.None;
            int attempts = 0;
            while (CountBits((int)t) < count && attempts++ < 40)
            {
                var pick = TraitPool[(int)(roll(attempts) * TraitPool.Length) % TraitPool.Length];
                if ((pick == Trait.Brave && (t & Trait.Timid) != 0) || (pick == Trait.Timid && (t & Trait.Brave) != 0)) continue;
                if ((pick == Trait.Kind && (t & Trait.Greedy) != 0) || (pick == Trait.Greedy && (t & Trait.Kind) != 0)) continue;
                if ((pick == Trait.Curious && (t & Trait.Timid) != 0 && count < 3)) continue;
                t |= pick;
            }
            return t;
        }

        static int CountBits(int v) { int n = 0; while (v != 0) { n += v & 1; v >>= 1; } return n; }

        void Marry(Citizen a, Citizen b, int causeEvent)
        {
            a.spouseId = b.id; b.spouseId = a.id;
            var r1 = EnsureRelation(a, b); r1.kind = RelationKind.Spouse; r1.affinity = Math.Max(r1.affinity, 70); r1.causeEventId = causeEvent;
            var r2 = EnsureRelation(b, a); r2.kind = RelationKind.Spouse; r2.affinity = Math.Max(r2.affinity, 70); r2.causeEventId = causeEvent;
        }

        void Parent(Citizen child, Citizen mother, Citizen father)
        {
            if (mother != null) { child.motherId = mother.id; mother.children.Add(child.id); var r = EnsureRelation(child, mother); r.kind = RelationKind.Family; r.affinity = 70; var r2 = EnsureRelation(mother, child); r2.kind = RelationKind.Family; r2.affinity = 80; }
            if (father != null) { child.fatherId = father.id; father.children.Add(child.id); var r = EnsureRelation(child, father); r.kind = RelationKind.Family; r.affinity = 70; var r2 = EnsureRelation(father, child); r2.kind = RelationKind.Family; r2.affinity = 80; }
        }

        void GeneratePeople(SeqRandom rng, List<Building> houses)
        {
            int Node(string n) => Nav[n];

            // --- the founding cast of Thistlewick Vale (present in every world) ---
            var oswin = NewCitizen("Oswin", "Thornbury", false, 61, Occupation.Elder, Trait.Pious | Trait.Kind, houses[0].id, rng);
            var agnes = NewCitizen("Agnes", "Thornbury", true, 58, Occupation.Farmer, Trait.Diligent | Trait.Pious, houses[0].id, rng);
            Marry(oswin, agnes, -1);
            houses[0].name = "Thornbury Cottage";

            var ambrose = NewCitizen("Ambrose", "Pell", false, 68, Occupation.Tinkerer, Trait.Curious | Trait.Kind, houses[1].id, rng);
            ambrose.craft = 72; ambrose.lore = 38; ambrose.goal = "Build a wheel that turns by itself";
            ambrose.marks["lonely"] = -1;
            houses[1].name = "Pell House";

            var hester = NewCitizen("Hester", "Quill", true, 38, Occupation.Shopkeeper, Trait.Greedy | Trait.Charismatic | Trait.Ambitious, houses[2].id, rng);
            var silas = NewCitizen("Silas", "Quill", false, 41, Occupation.Farmer, Trait.Timid | Trait.Diligent, houses[2].id, rng);
            Marry(hester, silas, -1);
            var juniper = NewCitizen("Juniper", "Quill", true, 6, Occupation.Child, Trait.Charismatic, houses[2].id, rng);
            Parent(juniper, hester, silas);
            hester.wealth = 30; hester.goal = "Sit at the head of the Moot table";
            houses[2].name = "Quill House";

            var maud = NewCitizen("Maud", "Fennick", true, 34, Occupation.Farmer, Trait.Kind | Trait.Diligent, houses[3].id, rng);
            var edmund = NewCitizen("Edmund", "Fennick", false, 36, Occupation.Woodcutter, Trait.Brave, houses[3].id, rng);
            Marry(maud, edmund, -1);
            var tobias = NewCitizen("Tobias", "Fennick", false, 11, Occupation.Child, Trait.Reckless | Trait.Brave, houses[3].id, rng);
            Parent(tobias, maud, edmund);
            var wren = NewCitizen("Wren", "Ashdown", true, 9, Occupation.Child, Trait.Curious | Trait.Kind, houses[3].id, rng);
            wren.craft = 5; wren.lore = 6; wren.goal = "Find out how things work";
            wren.marks["orphan"] = -1;
            houses[3].name = "Fennick Farmhouse";

            var dunstan = NewCitizen("Dunstan", "Brack", false, 45, Occupation.Smith, Trait.Brave | Trait.Diligent, houses[4].id, rng);
            var elsbeth = NewCitizen("Elsbeth", "Brack", true, 42, Occupation.Innkeeper, Trait.Charismatic | Trait.Kind, houses[4].id, rng);
            Marry(dunstan, elsbeth, -1);
            var rafe = NewCitizen("Rafe", "Brack", false, 17, Occupation.Builder, Trait.Ambitious | Trait.Romantic, houses[4].id, rng);
            var orla = NewCitizen("Orla", "Brack", true, 12, Occupation.Child, Trait.Brave, houses[4].id, rng);
            Parent(rafe, elsbeth, dunstan); Parent(orla, elsbeth, dunstan);
            houses[4].name = "Brack House";

            var corvin = NewCitizen("Corvin", "Hale", false, 44, Occupation.Merchant, Trait.Clumsy | Trait.Charismatic, -1, rng);
            corvin.offMapResident = true; corvin.wealth = 60; corvin.goal = "Sell the vale everything it never knew it needed";

            // --- seeded households for the remaining cottages ---
            var needs = new List<Occupation> { Occupation.Priest, Occupation.Shepherd, Occupation.Fisher, Occupation.Farmer, Occupation.Builder,
                Occupation.Farmer, Occupation.Woodcutter, Occupation.Fisher, Occupation.Farmer, Occupation.Farmer };
            int needIdx = 0;
            var usedFamilies = new HashSet<string> { "Thornbury", "Pell", "Quill", "Fennick", "Ashdown", "Brack", "Hale" };
            for (int h = 5; h < houses.Count; h++)
            {
                string fam;
                do { fam = rng.Pick(NameBank.Family); } while (!usedFamilies.Add(fam) && usedFamilies.Count < NameBank.Family.Length);
                houses[h].name = fam + " Cottage";
                int adults = rng.Chance(0.82) ? 2 : 1;
                Citizen first = null, second = null;
                for (int a = 0; a < adults; a++)
                {
                    bool female = a == 0 ? rng.Chance(0.5) : !first.female;
                    string name = female ? rng.Pick(NameBank.Female) : rng.Pick(NameBank.Male);
                    if (name == "Wren" || name == "Ambrose") name = female ? "Edda" : "Ansel";
                    int age = rng.Range(22, 52);
                    var occ = needs[needIdx++ % needs.Count];
                    var traits = RollTraits(i => rng.NextDouble(), rng.Range(1, 3));
                    var c = NewCitizen(name, fam, female, age, occ, traits, houses[h].id, rng);
                    if (a == 0) first = c; else second = c;
                }
                if (second != null) Marry(first, second, -1);
                int kids = rng.Range(0, adults == 2 ? 4 : 2);
                for (int k = 0; k < kids; k++)
                {
                    bool female = rng.Chance(0.5);
                    string name = female ? rng.Pick(NameBank.Female) : rng.Pick(NameBank.Male);
                    if (name == "Wren" || name == "Ambrose") name = female ? "Ilse" : "Ivo";
                    int age = rng.Range(1, 13);
                    var traits = RollTraits(i => rng.NextDouble(), rng.Range(1, 3));
                    traits &= ~Trait.Curious; // the vale's curious children are rare: Wren is one of few
                    var c = NewCitizen(name, fam, female, age, age < 5 ? Occupation.Toddler : Occupation.Child, traits, houses[h].id, rng);
                    var mother = first.female ? first : second;
                    var father = first.female ? second : first;
                    Parent(c, mother, father);
                }
            }

            ElderId = oswin.id;
            foreach (var c in Citizens) AssignWorkNode(c);
        }

        void AssignWorkNode(Citizen c)
        {
            string n;
            switch (c.occupation)
            {
                case Occupation.Farmer:
                    n = (c.id % 3) switch { 0 => "FieldW", 1 => "FieldE", _ => "FieldS" };
                    break;
                case Occupation.Smith: n = "Forge"; break;
                case Occupation.Shopkeeper: n = "Market"; break;
                case Occupation.Merchant: n = "Market"; break;
                case Occupation.Innkeeper: n = "Tavern"; break;
                case Occupation.Priest: n = "Chapel"; break;
                case Occupation.Woodcutter: n = "ForestCamp"; break;
                case Occupation.Shepherd: n = "Pasture"; break;
                case Occupation.Fisher: n = "Riverbank"; break;
                case Occupation.Tinkerer: n = "Workshop"; break;
                case Occupation.Scholar: n = HasComplete(BuildingType.Library) ? "Library" : "Chapel"; break;
                case Occupation.Builder: n = "Market"; break;
                case Occupation.Elder: n = "ElderHall"; break;
                case Occupation.Miller: n = "MillSite"; break;
                default: n = "Green"; break;
            }
            c.workNodeId = Nav[n];
        }
    }
}
