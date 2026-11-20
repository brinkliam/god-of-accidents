using System;
using System.Collections.Generic;

namespace GOA.Sim
{
    public sealed partial class World
    {
        public float DaysOfFood => Food / Math.Max(1f, Consumption());
        int _shortfallDays;
        int _lastPopMilestoneBirth = -1;
        int _lastBirthEvent = -1;

        float Consumption()
        {
            float c = 0;
            foreach (var p in Citizens)
                if (p.IsActive && !p.offMapResident) c += p.AgeYears(Time) < 14 ? 0.6f : 1f;
            return c;
        }

        public int HouseTierForEra() => Era switch
        {
            Era.Hamlet => 0, Era.Village => 1, Era.Town => 2, Era.Renaissance => 3, _ => 4
        };

        void RollWeather(long day)
        {
            var season = (Season)((day % SimTime.DaysPerYear) / SimTime.DaysPerSeason);
            double r = Rand.U01(Seed, day, 0, 0, (int)Salt.Weather);
            Weather w;
            switch (season)
            {
                case Season.Spring: w = r < 0.35 ? Weather.Rain : r < 0.6 ? Weather.Cloudy : r < 0.67 ? Weather.Storm : Weather.Clear; break;
                case Season.Summer: w = r < 0.15 ? Weather.Rain : r < 0.27 ? Weather.Storm : r < 0.4 ? Weather.Cloudy : Weather.Clear; break;
                case Season.Autumn: w = r < 0.3 ? Weather.Rain : r < 0.6 ? Weather.Cloudy : r < 0.68 ? Weather.Storm : Weather.Clear; break;
                default: w = r < 0.45 ? Weather.Snow : r < 0.75 ? Weather.Cloudy : Weather.Clear; break;
            }
            if (day == 0) w = Weather.Clear; // every world begins on a fair morning
            Weather = w;
            float ang = (float)(Rand.U01(Seed, day, 1, 0, (int)Salt.Wind) * Math.PI * 2);
            WindDir = Vec2.FromAngle(ang);
            float baseWind = (float)Rand.U01(Seed, day, 2, 0, (int)Salt.Wind);
            WindStrength = w == Weather.Storm ? 0.8f + baseWind * 0.2f : 0.1f + baseWind * 0.55f + (baseWind > 0.93 ? 0.3f : 0);
        }

        void DailyTick()
        {
            Schedule(Time + SimTime.Day, TaskKind.DailyTick);
            ScheduleAmbientDay(Time, false);
            long day = SimTime.DayIndex(Time);
            RollWeather(day);
            if (WindStrength >= 0.75f)
                foreach (var hr in new[] { 10.0, 13.0, 16.0 }) Schedule(SimTime.At(Time, hr), TaskKind.WindCheck);
            if (Weather == Weather.Storm && Rand.Chance(0.35, Seed, day, 0, 0, (int)Salt.Lightning))
                Schedule(SimTime.At(Time, Rand.RangeF(13, 20, Seed, day, 0, 0, (int)Salt.LightningHour)), TaskKind.Lightning);

            // merchant's pack for the day
            foreach (var c in Citizens)
            {
                if (!c.offMapResident || !c.IsActive) continue;
                double r = Rand.U01(Seed, day, c.id, 0, (int)Salt.Cargo);
                if (r < 0.3) { c.cargoItem = ItemType.Gear; c.cargo = "box of brass gears"; }
                else if (r < 0.5) { c.cargoItem = ItemType.Lens; c.cargo = "case of glass lenses"; }
                else if (r < 0.7) { c.cargoItem = ItemType.Book; c.cargo = "bundle of old books"; }
                else { c.cargoItem = ItemType.None; c.cargo = "bolts of cloth"; }
            }

            // the innkeeper re-hangs the lantern each morning
            foreach (var p in Props)
                if (p.type == PropType.Lantern && p.state != 0 && HasComplete(BuildingType.Tavern))
                { p.state = 0; p.pos = p.originalPos; PropVersion[p.id]++; }

            Economy(day);
            People(day);
            Courtships();
            Births(day);
            Skills(day);
            Inventions(day);
            Housing();
            Politics(day);
            UpdateEra();
            Energy = Math.Min(Scenario.maxEnergy, Energy + Scenario.energyRegenPerDay);
        }

        // ------------------------------------------------------------------ economy

        void Economy(long day)
        {
            var season = SimTime.SeasonOf(Time);
            float seasonF = season switch { Season.Spring => 0.8f, Season.Summer => 1.25f, Season.Autumn => 2.0f, _ => 0.15f };
            float weatherF = Weather switch { Weather.Rain => season == Season.Winter ? 1f : 1.15f, Weather.Storm => 0.85f, _ => 1f };
            float mult = 1f;
            if (HasComplete(BuildingType.Mill)) mult += 0.45f;
            if (HasComplete(BuildingType.Windmill)) mult += 0.2f;
            if (Techs.Contains("starcharts")) mult += 0.15f;
            if (Policy == Policy.Commons) mult += 0.05f;

            float produced = 0;
            int households = 0;
            foreach (var b in Buildings) if (b.type == BuildingType.House && b.Complete) households++;
            foreach (var c in Citizens)
            {
                if (!c.IsActive || c.offMapResident || c.AgeYears(Time) < 14 || c.injuredUntilDay >= day) continue;
                switch (c.occupation)
                {
                    case Occupation.Farmer: produced += 3.4f * seasonF * weatherF * mult; break;
                    case Occupation.Miller: produced += HasComplete(BuildingType.Mill) ? 2.5f : 1.2f; break;
                    case Occupation.Fisher: produced += season == Season.Winter ? 0.6f : 2.2f; break;
                    case Occupation.Shepherd: produced += 0.6f; c.wealth += 1; break;
                }
            }
            produced += households * 0.4f;
            Food += produced;
            float need = Consumption();
            Food -= need;
            if (!HasComplete(BuildingType.Granary))
            {
                float cap = Population * 6f;
                if (Food > cap) Food = cap + (Food - cap) * 0.7f;
            }

            float shortfall = 0;
            if (Food < 0) { shortfall = -Food; Food = 0; }
            float days = DaysOfFood;
            FoodPrice = Math.Max(0.6f, Math.Min(6f, 8f / Math.Max(0.5f, days)));

            if (FoodPrice >= 2.2f && ScarcityEventId < 0)
            {
                var causes = new List<int>();
                if (LastFoodLossEventId >= 0 && Time - Ledger.Get(LastFoodLossEventId).time < 10 * SimTime.Day) causes.Add(LastFoodLossEventId);
                var e = Record(SimEventType.Scarcity, "Bread prices soar", $"Grain now costs {FoodPrice:0.0}× its usual price.", 2, causes.ToArray());
                ScarcityEventId = e.id;
            }
            else if (FoodPrice < 1.5f) ScarcityEventId = -1;

            if (shortfall > 0)
            {
                _shortfallDays++;
                if (_shortfallDays == 3)
                {
                    var e = Record(SimEventType.Famine, "Hunger in the vale", "The stores are empty and the people go hungry.", 3, C(ScarcityEventId), null, null, true);
                    FamineEventId = e.id;
                }
            }
            else { _shortfallDays = 0; if (days > 6) FamineEventId = -1; }

            // shopkeepers profit from dear bread
            foreach (var c in Citizens)
            {
                if (!c.IsActive || c.occupation != Occupation.Shopkeeper) continue;
                int profit = 2 + (int)(5 * Math.Max(0, FoodPrice - 1));
                if (Policy == Policy.Trade) profit = (int)(profit * 1.5f);
                if (!HasComplete(BuildingType.Market)) profit /= 2;
                int before = c.wealth;
                c.wealth += profit;
                foreach (int th in new[] { 80, 160, 320 })
                {
                    if (before < th && c.wealth >= th)
                    {
                        var causes = new List<int>();
                        if (ScarcityEventId >= 0 && profit > 6) causes.Add(ScarcityEventId);
                        var e = Record(SimEventType.MerchantFortune, $"{c.Name} grows rich",
                            causes.Count > 0 ? $"{c.Name} sells bread dear while the village goes short. Purse: {c.wealth} coins." : $"{c.Name}'s shop thrives. Purse: {c.wealth} coins.",
                            2, causes.ToArray(), C(c.id));
                        c.renown.Add(e.id);
                        Remember(c, "Made a fortune at the market", e.id);
                    }
                }
            }
            foreach (var c in Citizens)
                if (c.IsActive && c.offMapResident) c.wealth += 3;

            // prosperity
            float moodSum = 0, wealthSum = 0; int n = 0;
            foreach (var c in Citizens) if (c.IsActive && !c.offMapResident) { moodSum += c.mood; wealthSum += c.wealth; n++; }
            float avgMood = n > 0 ? moodSum / n : 0.5f;
            float avgWealth = n > 0 ? wealthSum / n : 0;
            float prev = Prosperity;
            Prosperity = 0.3f * Math.Min(days / 15f, 1f) + 0.45f * avgMood + 0.25f * Math.Min(avgWealth / 30f, 1f);
            if (prev < 0.78f && Prosperity >= 0.78f && ProsperityEventId < 0)
            {
                var causes = new List<int>();
                foreach (var kv in TechEvents) causes.Add(kv.Value);
                var e = Record(SimEventType.Prosperity, "A season of plenty", "Full granaries, full purses, full hearts.", 2, causes.ToArray());
                ProsperityEventId = e.id;
            }
            if (Prosperity < 0.6f) ProsperityEventId = -1;
        }

        // ------------------------------------------------------------------ life and death

        void People(long day)
        {
            bool hungry = _shortfallDays > 0;
            for (int i = 0; i < Citizens.Count; i++)
            {
                var c = Citizens[i];
                if (!c.IsActive) continue;
                int age = c.AgeYears(Time);

                if (hungry && !c.offMapResident) { c.hungryDays++; c.mood = Math.Max(0, c.mood - 0.08f); }
                else { c.hungryDays = 0; c.mood += (0.65f + (Prosperity - 0.5f) * 0.3f - c.mood) * 0.1f; }

                if (c.occupation == Occupation.Toddler && age >= 5) c.occupation = Occupation.Child;
                if (c.occupation == Occupation.Child && age >= 14) ComeOfAge(c);

                double p = age < 50 ? 0.00003 : 0.00045 * (age - 47);
                if (c.hungryDays >= 4 && (age < 5 || age > 60)) p += 0.03;
                if (c.offMapResident && age > 64) p = 0; // the merchant retires beyond the pass; never mind
                if (Rand.Chance(p, Seed, day, c.id, 0, (int)Salt.Death)) Die(c, c.hungryDays >= 4 ? FamineEventId : -1);
            }

            // whole households may leave a hungry, miserable vale
            if (_shortfallDays >= 2)
            {
                var seen = new HashSet<int>();
                foreach (var c in Citizens)
                {
                    if (!c.IsActive || c.offMapResident || c.homeBuildingId < 0 || !seen.Add(c.homeBuildingId)) continue;
                    if (c.mood > 0.2f || !(c.Has(Trait.Ambitious) || c.Has(Trait.Timid))) continue;
                    if (!Rand.Chance(0.04, Seed, day, c.homeBuildingId, 0, (int)Salt.Emigrate)) continue;
                    var leaving = new List<int>();
                    foreach (var o in Citizens) if (o.IsActive && o.homeBuildingId == c.homeBuildingId) leaving.Add(o.id);
                    var e = Record(SimEventType.Emigration, $"The {c.familyName}s leave the vale", $"{leaving.Count} people pack a cart and take the east road for good.", 2,
                        C(FamineEventId >= 0 ? FamineEventId : ScarcityEventId), leaving.ToArray());
                    foreach (var id in leaving) { var o = Citizens[id]; o.emigrated = true; o.activitySerial++; o.activity = null; }
                }
            }
        }

        void ComeOfAge(Citizen c)
        {
            Occupation occ;
            int cause = -1;
            if (c.marks.TryGetValue("spark:engineering", out int s1)) { occ = Occupation.Tinkerer; cause = s1; }
            else if (c.marks.TryGetValue("spark:optics", out int s2)) { occ = Occupation.Scholar; cause = s2; }
            else if (c.marks.TryGetValue("spark:letters", out int s3)) { occ = Occupation.Scholar; cause = s3; }
            else
            {
                Citizen mentor = null;
                foreach (var kv in c.relations) if (kv.Value.kind == RelationKind.Mentor) { mentor = GetCitizen(kv.Key); cause = kv.Value.causeEventId; }
                if (mentor != null) occ = mentor.occupation;
                else
                {
                    double r = Rand.U01(Seed, c.id, 0, 0, (int)Salt.Occupation);
                    bool needMiller = HasComplete(BuildingType.Mill) && FirstWith(Occupation.Miller) == null;
                    if (needMiller) occ = Occupation.Miller;
                    else if (DaysOfFood < 14 || r < 0.35) occ = Occupation.Farmer;
                    else if (Projects.Count > 1 || r < 0.5) occ = Occupation.Builder;
                    else if (r < 0.62) occ = Occupation.Fisher;
                    else if (r < 0.74) occ = Occupation.Woodcutter;
                    else if (r < 0.82) occ = Occupation.Shepherd;
                    else
                    {
                        var parent = GetCitizen(c.fatherId) ?? GetCitizen(c.motherId);
                        occ = parent != null && parent.occupation != Occupation.Elder ? parent.occupation : Occupation.Farmer;
                        if (occ == Occupation.Shopkeeper || occ == Occupation.Innkeeper || occ == Occupation.Priest) occ = Occupation.Farmer;
                    }
                }
            }
            c.occupation = occ;
            c.occupationCause = cause;
            AssignWorkNode(c);
            int id = RecordIfChained(SimEventType.ComingOfAge, $"{c.Name} becomes a {OccupationName(occ)}",
                $"At fourteen, {c.Name} takes up the trade of a {OccupationName(occ).ToLowerInvariant()}.", 2, C(cause), C(c.id));
            if (id >= 0) Remember(c, $"Became a {OccupationName(occ)}", id);
        }

        public static string OccupationName(Occupation o) => o switch
        {
            Occupation.Tinkerer => "Tinkerer", Occupation.Scholar => "Scholar", Occupation.Farmer => "Farmer", Occupation.Smith => "Smith",
            Occupation.Shopkeeper => "Shopkeeper", Occupation.Merchant => "Travelling Merchant", Occupation.Innkeeper => "Innkeeper",
            Occupation.Priest => "Priest", Occupation.Woodcutter => "Woodcutter", Occupation.Shepherd => "Shepherd", Occupation.Fisher => "Fisher",
            Occupation.Builder => "Builder", Occupation.Elder => "Elder", Occupation.Miller => "Miller", Occupation.Toddler => "Little One",
            _ => "Child"
        };

        void Die(Citizen c, int cause)
        {
            c.alive = false;
            c.deathTime = Time;
            bool notable = c.fame >= 3 || c.marks.Count > 1 || c.id == ElderId || c.renown.Count > 0;
            var e = Record(SimEventType.Death, $"{c.Name} dies", $"{c.Name} passes at {c.AgeYears(Time)}{(cause >= 0 ? ", weakened by hunger" : "")}.",
                notable ? 3 : 1, C(cause), C(c.id), PositionNow(c), notable);
            if (c.spouseId >= 0) { var s = GetCitizen(c.spouseId); if (s != null) s.spouseId = -1; }
            c.activitySerial++;
            c.activity = null;
            _directives.Remove(c.id);
            if (c.id == ElderId) HoldElection(e.id);
        }

        // ------------------------------------------------------------------ love, marriage, children

        void Courtships()
        {
            foreach (var a in Citizens)
            {
                if (!a.IsActive) continue;
                foreach (var kv in a.relations)
                {
                    var r = kv.Value;
                    if (r.kind != RelationKind.Courting || kv.Key < a.id) continue;
                    var b = GetCitizen(kv.Key);
                    if (b == null || !b.IsActive || a.spouseId >= 0 || b.spouseId >= 0) continue;
                    AdjustAffinity(a, b, 3f);
                    if (r.affinity >= 85)
                    {
                        var e = Record(SimEventType.Marriage, $"{a.Name} marries {b.Name}",
                            $"The chapel bell rings for {a.Name} and {b.Name}.", 2, C(r.causeEventId), C(a.id, b.id), Nav.Node(Nav["Chapel"]).pos);
                        Marry(a, b, e.id);
                        Remember(a, $"Married {b.Name}", e.id);
                        Remember(b, $"Married {a.Name}", e.id);
                        SettleCouple(a, b, e.id);
                        break; // relations dictionary changed kind; continue with next citizen
                    }
                }
            }
        }

        int Residents(int buildingId)
        {
            int n = 0;
            foreach (var c in Citizens) if (c.IsActive && c.homeBuildingId == buildingId) n++;
            return n;
        }

        void SettleCouple(Citizen a, Citizen b, int cause)
        {
            // one partner moves in with the other if there is room
            if (a.homeBuildingId >= 0 && Residents(a.homeBuildingId) <= 3) { b.homeBuildingId = a.homeBuildingId; return; }
            if (b.homeBuildingId >= 0 && Residents(b.homeBuildingId) <= 3) { a.homeBuildingId = b.homeBuildingId; return; }
            foreach (var h in Buildings)
                if (h.type == BuildingType.House && h.Complete && Residents(h.id) == 0) { a.homeBuildingId = h.id; b.homeBuildingId = h.id; return; }
            var house = NewHouse(cause);
            if (house != null) { house.ownerFamilyId = a.id; }
        }

        Building NewHouse(int cause)
        {
            if (FreeLots.Count == 0) return null;
            var lot = FreeLots[0];
            FreeLots.RemoveAt(0);
            var b = CreateHouseOnLot(lot, HouseTierForEra(), Rand.Range(0, 1000, Seed, Buildings.Count, 0, 0, (int)Salt.Build));
            b.name = "New Cottage";
            QueueConstruction(b, cause, false);
            return b;
        }

        void Housing()
        {
            foreach (var b in Buildings)
            {
                if (b.type != BuildingType.House || !b.Complete || b.ownerFamilyId < 0) continue;
                var a = GetCitizen(b.ownerFamilyId);
                b.ownerFamilyId = -1;
                if (a == null || !a.IsActive) continue;
                b.name = a.familyName + " Cottage";
                a.homeBuildingId = b.id;
                var s = GetCitizen(a.spouseId);
                if (s != null && s.IsActive) s.homeBuildingId = b.id;
                foreach (var ch in a.children) { var k = GetCitizen(ch); if (k != null && k.IsActive && k.AgeYears(Time) < 14) k.homeBuildingId = b.id; }
            }
            // gentle renovation toward the current era's architecture
            int targetTier = HouseTierForEra();
            foreach (var b in Buildings)
            {
                if (b.type == BuildingType.House && b.Complete && b.tier < targetTier && Rand.Chance(0.08, Seed, b.id, Time, 0, (int)Salt.Build))
                { b.tier++; break; }
            }
        }

        void Births(long day)
        {
            if (DaysOfFood < 2 && _shortfallDays > 0) return;
            int count = Citizens.Count;
            for (int i = 0; i < count; i++)
            {
                var m = Citizens[i];
                if (!m.IsActive || !m.female || m.spouseId < 0) continue;
                var f = GetCitizen(m.spouseId);
                if (f == null || !f.IsActive || f.female) continue;
                int age = m.AgeYears(Time);
                if (age < 18 || age > 42) continue;
                int kids = 0;
                foreach (var k in m.children) if (GetCitizen(k).IsActive) kids++;
                if (kids >= 5) continue;
                double p = 0.011 * (DaysOfFood > 5 ? 1 : 0.25) * (Policy == Policy.Commons ? 1.2 : 1);
                if (!Rand.Chance(p, Seed, day, m.id, 0, (int)Salt.Birth)) continue;

                var rel = m.Rel(f.id);
                int cause = rel != null ? rel.causeEventId : -1;
                bool girl = Rand.Chance(0.5, Seed, day, m.id, 1, (int)Salt.Birth);
                var names = girl ? NameBank.Female : NameBank.Male;
                string first = UniqueFirstName(names[Rand.Range(0, names.Length, Seed, day, m.id, 2, (int)Salt.Birth)], girl);
                Trait inherit = Trait.None;
                foreach (var parent in new[] { m, f })
                    foreach (Trait t in Enum.GetValues(typeof(Trait)))
                        if (t != Trait.None && parent.Has(t) && Rand.Chance(0.35, Seed, day, m.id, (int)t, (int)Salt.Traits)) inherit |= t;
                if (Rand.Chance(0.5, Seed, day, m.id, 3, (int)Salt.Traits))
                    inherit |= RollTraits(k => Rand.U01(Seed, day, m.id, k, (int)Salt.Traits + 1), 1);
                if ((inherit & Trait.Brave) != 0 && (inherit & Trait.Timid) != 0) inherit &= ~Trait.Timid;
                if ((inherit & Trait.Kind) != 0 && (inherit & Trait.Greedy) != 0) inherit &= ~Trait.Greedy;

                var child = new Citizen
                {
                    id = Citizens.Count, firstName = first, familyName = f.familyName, female = girl, birthTime = Time,
                    occupation = Occupation.Toddler, traits = inherit, homeBuildingId = m.homeBuildingId,
                    craft = 1, lore = 1, charm = Rand.RangeF(10, 50, Seed, day, m.id, 4, 0), strength = 10,
                    skinTone = Rand.Chance(0.5, Seed, day, m.id, 5, 0) ? m.skinTone : f.skinTone,
                    hairColor = Rand.Chance(0.5, Seed, day, m.id, 6, 0) ? m.hairColor : f.hairColor,
                    outfitSeed = Rand.Range(0, 100000, Seed, day, m.id, 7, 0),
                };
                Citizens.Add(child);
                Parent(child, m, f);
                child.birthCause = cause;
                var e = Record(SimEventType.Birth, $"{first} {f.familyName} is born", $"A child for {m.Name} and {f.Name}.", 2, C(cause), C(child.id, m.id, f.id),
                    m.activity != null ? PositionNow(m) : (Vec2?)null);
                if (e.roots.Length > 0) e.turningPoint = true; // a life that exists because of you
                child.birthCause = e.id;
                _lastBirthEvent = e.id;
                Remember(m, $"Gave birth to {first}", e.id);
                Decide(child);
                CheckPopulationMilestone();
                if (Residents(m.homeBuildingId) > 6) NewHouse(e.id);
            }
        }

        void CheckPopulationMilestone()
        {
            int pop = Population;
            foreach (int m in new[] { 40, 50, 60, 80, 100, 150 })
            {
                if (pop >= m && HighestPopulationMilestone < m)
                {
                    HighestPopulationMilestone = m;
                    Record(SimEventType.PopulationMilestone, $"The vale reaches {m} souls", $"There are now {pop} people in Thistlewick.", m >= 60 ? 3 : 2, C(_lastBirthEvent));
                }
            }
        }

        // ------------------------------------------------------------------ minds and inventions

        void Skills(long day)
        {
            float lm = (Policy == Policy.Learning ? 1.5f : 1f) * (Techs.Contains("printing") ? 1.5f : 1f);
            foreach (var c in Citizens)
            {
                if (!c.IsActive || c.offMapResident) continue;
                int age = c.AgeYears(Time);
                float dil = c.Has(Trait.Diligent) ? 1.3f : 1f;
                switch (c.occupation)
                {
                    case Occupation.Tinkerer: c.craft += 0.35f * dil; c.lore += 0.06f * lm; break;
                    case Occupation.Scholar: c.lore += 0.4f * dil * lm; c.craft += 0.03f; break;
                    case Occupation.Smith: c.craft += 0.1f * dil; break;
                    case Occupation.Builder: c.craft += 0.08f * dil; break;
                    case Occupation.Priest: c.lore += 0.08f * lm; break;
                }
                if (age < 14)
                {
                    if (c.HasMark("spark:engineering")) c.craft += 0.12f;
                    if (c.HasMark("spark:optics") || c.HasMark("spark:letters")) c.lore += 0.12f * lm;
                }
                foreach (var kv in c.relations)
                {
                    if (kv.Value.kind != RelationKind.Mentor) continue;
                    var m = GetCitizen(kv.Key);
                    if (m == null || !m.IsActive) continue;
                    if (m.occupation == Occupation.Tinkerer) { c.craft += 0.3f; c.lore += 0.04f; }
                    else c.lore += 0.3f * lm;
                }
                c.craft = Math.Min(100, c.craft); c.lore = Math.Min(100, c.lore);

                // rare natural sparks: the world has its own coincidences
                if (age >= 6 && age <= 16 && c.Has(Trait.Curious) && c.marks.Count < 3 && Rand.Chance(Scenario.naturalSparkRate, Seed, day, c.id, 0, (int)Salt.NaturalSpark))
                {
                    double r = Rand.U01(Seed, day, c.id, 1, (int)Salt.NaturalSpark);
                    if (r < 0.5) Spark(c, "engineering", -1, "watches sparks fly from the smith's anvil and wonders why the hammer bounces");
                    else if (r < 0.8) Spark(c, "optics", -1, "lies in the meadow counting stars until dawn");
                    else Spark(c, "letters", -1, "copies the chapel's inscriptions into the dirt with a stick");
                }
            }
        }

        void Inventions(long day)
        {
            for (int ti = 0; ti < TechTree.All.Length; ti++)
            {
                var tech = TechTree.All[ti];
                if (Techs.Contains(tech.id)) continue;
                bool ok = true;
                foreach (var r in tech.requires) if (!Techs.Contains(r)) ok = false;
                if (!ok) continue;
                foreach (var c in Citizens)
                {
                    if (!c.IsActive || c.offMapResident || c.AgeYears(Time) < 15) continue;
                    if (Array.IndexOf(tech.occupations, c.occupation) < 0) continue;
                    if (tech.spark != null && !c.HasMark(tech.spark)) continue;
                    if (c.craft < tech.minCraft || c.lore < tech.minLore) continue;
                    double p = tech.dailyChance * (Policy == Policy.Learning ? 1.5 : 1);
                    if (!Rand.Chance(p, Seed, day, c.id, ti, (int)Salt.Invention)) continue;

                    var causes = new List<int>();
                    if (tech.spark != null) causes.Add(c.marks[tech.spark]);
                    foreach (var kv in c.relations) if (kv.Value.kind == RelationKind.Mentor && kv.Value.causeEventId >= 0) causes.Add(kv.Value.causeEventId);
                    if (c.occupationCause >= 0) causes.Add(c.occupationCause);
                    foreach (var r in tech.requires) causes.Add(TechEvents[r]);
                    var e = Record(SimEventType.Invention, $"{c.Name} invents {tech.title}", tech.description, 5, causes.ToArray(), C(c.id), PositionNow(c), true);
                    Techs.Add(tech.id);
                    TechEvents[tech.id] = e.id;
                    c.fame += 5;
                    c.renown.Add(e.id);
                    Remember(c, $"Invented {tech.title}", e.id);
                    if (tech.unlocks.HasValue)
                    {
                        var b = FirstBuilding(tech.unlocks.Value);
                        if (b != null && !b.Complete && b.buildProgress <= 0f) QueueConstruction(b, e.id, false);
                    }
                    break;
                }
            }
        }

        // ------------------------------------------------------------------ politics and eras

        void Politics(long day)
        {
            int y = SimTime.Year1(Time);
            if (SimTime.DayOfYear(Time) == 0 && y >= 3 && (y - 3) % 4 == 0) HoldElection(-1);
        }

        void HoldElection(int triggerCause)
        {
            Citizen best = null; double bestScore = double.MinValue;
            foreach (var c in Citizens)
            {
                if (!c.IsActive || c.offMapResident || c.AgeYears(Time) < 25) continue;
                double s = c.charm * 0.5 + c.fame * 6 + Math.Min(c.wealth, 400) * 0.25 + (c.Has(Trait.Ambitious) ? 12 : 0) + (c.id == ElderId ? 10 : 0)
                           + Math.Min(c.AgeYears(Time), 60) * 0.2 + Rand.RangeF(-5, 5, Seed, c.id, Time, 0, (int)Salt.Merchant);
                if (s > bestScore) { bestScore = s; best = c; }
            }
            if (best == null) return;
            if (best.id == ElderId)
            {
                RecordIfChained(SimEventType.Election, $"{best.Name} keeps the Elder's chair", "The moot changes nothing.", 1, C(triggerCause), C(best.id));
                return;
            }
            var causes = new List<int>();
            if (triggerCause >= 0) causes.Add(triggerCause);
            for (int i = Math.Max(0, best.renown.Count - 3); i < best.renown.Count; i++) causes.Add(best.renown[i]);
            var e = Record(SimEventType.Election, $"{best.Name} is chosen Elder", $"The moot hall votes for {best.Name}.", 4, causes.ToArray(), C(best.id), Nav.Node(Nav["ElderHall"]).pos, true);
            ElderId = best.id;
            Remember(best, "Chosen as Elder of the vale", e.id);

            Policy p = Policy.Tradition;
            if (best.Has(Trait.Greedy) || best.occupation == Occupation.Shopkeeper) p = Policy.Trade;
            else if (best.Has(Trait.Curious) || best.occupation == Occupation.Tinkerer || best.occupation == Occupation.Scholar) p = Policy.Learning;
            else if (best.Has(Trait.Pious) || best.occupation == Occupation.Priest) p = Policy.Faith;
            else if (best.Has(Trait.Brave)) p = Policy.Watch;
            else if (best.Has(Trait.Kind)) p = Policy.Commons;
            if (p != Policy)
            {
                Policy = p;
                var pe = Record(SimEventType.PolicyChange, $"A new law: {PolicyName(p)}", PolicyBlurb(p), 3, C(e.id), C(best.id), null, true);
                PolicyEventId = pe.id;
                if (p == Policy.Learning)
                {
                    var lib = FirstBuilding(BuildingType.Library);
                    if (lib != null && !lib.Complete && lib.buildProgress <= 0) QueueConstruction(lib, pe.id, false);
                }
                if (p == Policy.Trade) { var m = FirstBuilding(BuildingType.Market); if (m != null) m.tier = Math.Max(m.tier, 1); }
                if (p == Policy.Faith) { var ch = FirstBuilding(BuildingType.Chapel); if (ch != null) ch.tier = Math.Max(ch.tier, 1); }
            }
        }

        public static string PolicyName(Policy p) => p switch
        {
            Policy.Trade => "The Trade Charter", Policy.Faith => "The Bell Covenant", Policy.Watch => "The Night Watch",
            Policy.Commons => "The Common Granary", Policy.Learning => "The Lamp of Learning", _ => "The Old Ways"
        };

        public static string PolicyBlurb(Policy p) => p switch
        {
            Policy.Trade => "Markets every day, even holy days. Coin flows; so does inequality.",
            Policy.Faith => "Every holy day a festival; the chapel grows grander.",
            Policy.Watch => "Watchmen walk the lanes. Fires are fought faster.",
            Policy.Commons => "Bread is shared. Fewer go hungry; more children are born.",
            Policy.Learning => "A House of Letters is founded. Minds grow faster.",
            _ => "Things go on as they always have."
        };

        void UpdateEra()
        {
            int pop = Population;
            int techs = Techs.Count;
            Era target = Era.Hamlet;
            if (pop >= 40 || techs >= 1) target = Era.Village;
            if ((techs >= 2 && pop >= 40) || (techs >= 1 && pop >= 55)) target = Era.Town;
            if (Techs.Contains("printing") && Techs.Contains("clockwork")) target = Era.Renaissance;
            if (Techs.Contains("steam")) target = Era.Industrial;
            if (target <= Era) return;
            var causes = new List<int>();
            foreach (var kv in TechEvents) causes.Add(kv.Value);
            if (HighestPopulationMilestone > 0 && _lastBirthEvent >= 0) causes.Add(_lastBirthEvent);
            Era = target;
            var e = Record(SimEventType.EraChange, $"The dawn of the {EraName(target)}", EraBlurb(target), 5, causes.ToArray(), null, null, true);
            EraEventId = e.id;
        }

        public static string EraName(Era e) => e switch
        {
            Era.Hamlet => "Hamlet Age", Era.Village => "Village Age", Era.Town => "Market Town", Era.Renaissance => "Age of Lamps", _ => "Age of Steam"
        };

        public static string EraBlurb(Era e) => e switch
        {
            Era.Village => "Thatch gives way to timber. Thistlewick is a proper village now.",
            Era.Town => "Stone walls, tiled roofs, a busy market. A town is born.",
            Era.Renaissance => "Clocks chime, presses clatter. The vale thinks faster than ever.",
            Era.Industrial => "Smoke and iron. The quiet vale will never be quiet again.",
            _ => "A handful of cottages by a river."
        };

        // ------------------------------------------------------------------ storms

        void LightningStrike()
        {
            long day = SimTime.DayIndex(Time);
            double r = Rand.U01(Seed, day, 1, 0, (int)Salt.Lightning);
            if (r < 0.4)
            {
                var nest = NestProp();
                var e = Record(SimEventType.LightningStrike, "Lightning splits the old oak", "The crow's nest tumbles from the shattered crown.", 2, null, null, VillageLayout.OakPos);
                if (nest != null && nest.hoard != null && nest.hoard.Count > 0)
                {
                    int k = 0;
                    foreach (var item in nest.hoard)
                    {
                        float ang = k++ * 2.1f + (float)r * 6;
                        SpawnGroundItem(item, VillageLayout.OakPos + Vec2.FromAngle(ang) * (3 + k * 1.5f), e.id);
                    }
                    nest.hoard.Clear();
                }
            }
            else if (r < 0.7)
            {
                var targets = new List<Building>();
                foreach (var b in Buildings) if (IsFlammable(b) && (b.type == BuildingType.Barn || b.type == BuildingType.House || b.type == BuildingType.Haystack || b.type == BuildingType.Mill)) targets.Add(b);
                if (targets.Count == 0) return;
                var t = targets[Rand.Range(0, targets.Count, Seed, day, 2, 0, (int)Salt.Lightning)];
                var e = Record(SimEventType.LightningStrike, $"Lightning strikes {t.name}", "A white flash, a crack like the sky breaking.", 2, null, null, t.pos);
                StartFire(t, C(e.id), $"The bolt sets {t.name} ablaze.");
            }
            else
            {
                Record(SimEventType.LightningStrike, "Lightning strikes the chapel spire", "The Quiet Bell rings once on its own. The pious call it an omen.", 1, null, null, Nav.Node(Nav["Chapel"]).pos);
                foreach (var c in Citizens) if (c.IsActive && c.Has(Trait.Pious)) c.mood = Math.Min(1, c.mood + 0.1f);
            }
        }

        public Prop NestProp()
        {
            foreach (var p in Props) if (p.type == PropType.CrowNest) return p;
            return null;
        }
    }
}
