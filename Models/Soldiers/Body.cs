using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Models.Soldiers
{
    public enum Stance
    {
        Standing,
        Kneeling, 
        Prone
    }

    public class Wounds
    {
        internal event System.Action Changed;

        public uint WoundTotal { get; private set; }
        public const byte WOUND_MAX = 5;
        public uint WeeksOfHealing { get; set; }
        
        public byte NegligibleWounds
        {
            get
            {
                return (byte)(WoundTotal & 0xf);
            }
        }

        public byte MinorWounds
        {
            get
            {
                return (byte)((WoundTotal / 0x10) & 0xf);
            }
        }

        public byte ModerateWounds
        {
            get
            {
                return (byte)((WoundTotal / 0x100) & 0xf);
            }
        }

        public byte MajorWounds
        {
            get
            {
                return (byte)((WoundTotal / 0x1000) & 0xf);
            }
        }

        public byte CriticalWounds
        {
            get
            {
                return (byte)((WoundTotal / 0x10000) & 0xf);
            }
        }

        public byte MassiveWounds
        {
            get
            {
                return (byte)((WoundTotal / 0x100000) & 0xf);
            }
        }

        public byte MortalWounds
        {
            get
            {
                return (byte)((WoundTotal / 0x1000000) & 0xf);
            }
        }

        public byte UnsurvivableWounds
        {
            get
            {
                return (byte)((WoundTotal / 0x10000000) & 0xf);
            }
        }

        public Wounds(uint woundTotal, uint weeksOfHealing)
        {
            WoundTotal = woundTotal;
            WeeksOfHealing = weeksOfHealing;
        }

        public void AddWound(WoundLevel wound)
        {
            WeeksOfHealing = 0;
            WoundTotal += (uint)wound;
            Normalize();
            Changed?.Invoke();
        }

        // Folds any band holding more than WOUND_MAX wounds up into the band above it, carrying the
        // remainder. Every severity comparison in the game -- cripple, sever, CanFight, the
        // Apothecarium's severity labels -- reads WoundTotal as a plain magnitude, and that is only
        // valid while the representation is normalized: six Major wounds (0x6000) compare as *less*
        // severe than the one Critical wound (0x10000) they are equivalent to.
        //
        // Both mutation paths must run this. AddWound always did; the healing step-down never did,
        // and it deposits a whole band's worth of wounds onto a band that may already be occupied.
        private void Normalize()
        {
            const int bandRatio = WOUND_MAX + 1;
            for (int shift = 0; shift < 28; shift += 4)
            {
                uint count = (WoundTotal >> shift) & 0xf;
                if (count <= WOUND_MAX)
                {
                    continue;
                }
                uint promoted = count / bandRatio;
                uint remainder = count % bandRatio;
                WoundTotal &= ~((uint)0xf << shift);
                WoundTotal += remainder << shift;
                WoundTotal += promoted << (shift + 4);
            }
        }

        /// <summary>
        /// Clears the Negligible band outright, leaving every other band and every band clock
        /// untouched (Design/Reference/CasualtyRealism.md §2.5). This is the Astartes daily pass:
        /// grazes and bruises close overnight.
        ///
        /// The boundary is load-bearing rather than cosmetic. Five Negligible wounds promote to a
        /// Minor one, so clearing them once a day means a *day's* worth of glancing hits no longer
        /// compounds into a real wound -- while a single battle's worth still does, because a
        /// battle resolves inside one day and this never runs during one. Bands below Moderate
        /// carry no healing clock, so there is nothing to reset.
        /// </summary>
        public void ClearNegligibleWounds()
        {
            uint before = WoundTotal;
            WoundTotal &= 0xfffffff0;
            if (WoundTotal != before)
            {
                Changed?.Invoke();
            }
        }

        /// <summary>
        /// The worst band this location carries that an Apothecary could usefully treat, and how
        /// many of that band's wounds a single treatment could actually step down
        /// (Design/Reference/CasualtyRealism.md §2.6). Pure -- nothing is mutated.
        ///
        /// Bands below Moderate are excluded because they carry no healing clock and are cleared
        /// outright by the next natural pass anyway: spending an Apothecary's day on a graze buys
        /// nothing. Count is capped by the room left in the band below, because a demotion that
        /// overfilled it would be folded straight back up by <see cref="Normalize"/> -- undoing the
        /// treatment. Returns <see cref="WoundLevel.None"/> with a zero count when there is nothing
        /// worth treating.
        /// </summary>
        public (WoundLevel Band, int Count) FindTreatableBand()
        {
            for (int shift = 28; shift >= 8; shift -= 4)
            {
                uint count = (WoundTotal >> shift) & 0xf;
                if (count == 0) continue;
                uint room = WOUND_MAX - ((WoundTotal >> (shift - 4)) & 0xf);
                uint movable = System.Math.Min(count, room);
                return movable == 0
                    ? ((WoundLevel)0, 0)
                    : ((WoundLevel)((uint)1 << shift), (int)movable);
            }
            return ((WoundLevel)0, 0);
        }

        /// <summary>
        /// An Apothecary's field treatment: a FORCED demotion of the worst treatable band, applied
        /// the moment it happens (Design/Reference/CasualtyRealism.md §2.6). Returns the band that was
        /// treated, or <see cref="WoundLevel.None"/> if there was nothing to treat.
        ///
        /// Expressed in the healing model's own vocabulary rather than as a separate "treatment
        /// credit" accumulator, which is the whole reason §2.4 calls the step-down structure a gift
        /// for the medical system: the effect is immediately visible to
        /// <see cref="RecoveryTimeLeft"/>, to the Apothecarium, and to the next day's battle.
        ///
        /// Clock handling mirrors <see cref="ApplyWeekOfHealing"/>'s demotions: the receiving band's
        /// clock is reset because the wounds arriving there have served none of its dwell time, and
        /// the vacated band's clock is reset only when the band actually emptied. Nothing else about
        /// WeeksOfHealing is touched -- treatment must never cost a man the convalescence he has
        /// already banked.
        /// </summary>
        public WoundLevel ApplyTreatmentDemotion()
        {
            (WoundLevel band, int count) = FindTreatableBand();
            if (band == 0 || count <= 0)
            {
                return 0;
            }

            int shift = 0;
            for (uint value = (uint)band; value > 1; value >>= 1) shift++;
            int lowerShift = shift - 4;

            uint remaining = (((WoundTotal >> shift) & 0xf) - (uint)count);
            WoundTotal &= ~((uint)0xf << shift);
            WoundTotal += remaining << shift;
            WoundTotal += (uint)count << lowerShift;
            if (remaining == 0)
            {
                WeeksOfHealing &= ~((uint)0xf << shift);
            }
            WeeksOfHealing &= ~((uint)0xf << lowerShift);
            // Cannot fire given FindTreatableBand's room cap, but the invariant is cheap to hold and
            // every severity comparison in the game depends on it.
            Normalize();
            Changed?.Invoke();
            return band;
        }

        public void ApplyWeekOfHealing()
        {
            // Every band that currently HOLDS wounds advances its own clock, independently and at
            // the same time. Wounds are discrete injuries, not one severity counter: a broken nose,
            // a swollen eye and a split lip all mend together, and the lip does not wait for the
            // nose. An EMPTY band's clock must not advance, which is where the original
            // `WeeksOfHealing += 0x11111100` went wrong -- it aged every band unconditionally, so a
            // wound stepping down found the band below had already served its dwell time and fell
            // straight through to Minor in a single pass, and over a long convalescence the unused
            // low nibbles overflowed and carried into the bands above them.
            AdvanceOccupiedBandClocks();
            // negligible and minor wounds heal automatically
            WoundTotal &= 0xffffff00;
            // Each demotion clears the clock for BOTH the band being vacated and the band being
            // entered. Clearing the receiving band is the load-bearing half: every band's nibble
            // advances each week whether or not a wound sits in it, so without this reset a wound
            // arriving at a lower band finds that band's dwell time already served and falls
            // straight through -- cascading all the way to Minor in a single pass, and healing an
            // Unsurvivable wound in 8 weeks instead of the advertised 28.
            if(UnsurvivableWounds > 0 && (WeeksOfHealing & 0xf0000000) > 0x60000000)
            {
                byte newMortalWounds = UnsurvivableWounds;
                WeeksOfHealing &= 0x00ffffff;
                WoundTotal &= 0x0fffffff;
                WoundTotal += (uint)(newMortalWounds * 0x01000000);
            }
            if (MortalWounds > 0 && (WeeksOfHealing & 0x0f000000) > 0x05000000)
            {
                byte newMassiveWounds = MortalWounds;
                WeeksOfHealing &= 0xf00fffff;
                WoundTotal &= 0xf0ffffff;
                WoundTotal += (uint)(newMassiveWounds * 0x00100000);
            }
            if (MassiveWounds > 0 && (WeeksOfHealing & 0x00f00000) > 0x00400000)
            {
                byte newCriticalWounds = MassiveWounds;
                WeeksOfHealing &= 0xff00ffff;
                WoundTotal &= 0xff0fffff;
                WoundTotal += (uint)(newCriticalWounds * 0x00010000);
            }
            if (CriticalWounds > 0 && (WeeksOfHealing & 0x000f0000) > 0x00030000)
            {
                byte newMajorWounds = CriticalWounds;
                WeeksOfHealing &= 0xfff00fff;
                WoundTotal &= 0xfff0ffff;
                WoundTotal += (uint)(newMajorWounds * 0x00001000);
            }
            if (MajorWounds > 0 && (WeeksOfHealing & 0x0000f000) > 0x00002000)
            {
                byte newModerateWounds = MajorWounds;
                WeeksOfHealing &= 0xffff00ff;
                WoundTotal &= 0xffff0fff;
                WoundTotal += (uint)(newModerateWounds * 0x00000100);
            }
            // A step-down can deposit its wounds onto a band that is already occupied -- three
            // Critical wounds falling onto three existing Major wounds leaves six, one band over
            // its maximum. Without this, that location reads as *less* severely wounded than it is
            // and the next hit resolves against a malformed WoundTotal.
            Normalize();
            if (ModerateWounds > 0 && (WeeksOfHealing & 0x00000f00) > 0x00000100)
            {
                byte newMinorWounds = ModerateWounds;
                WeeksOfHealing &= 0xfffff0ff;
                WoundTotal &= 0xfffff0ff;
                WoundTotal += (uint)(newMinorWounds * 0x00000010);
            }
            Changed?.Invoke();
        }

        // Adds one week to the clock of every band that currently holds at least one wound, and to
        // no others. Bands below Moderate are cleared outright each pass and carry no clock.
        private void AdvanceOccupiedBandClocks()
        {
            for (int shift = 8; shift <= 28; shift += 4)
            {
                if (((WoundTotal >> shift) & 0xf) == 0)
                {
                    continue;
                }
                // An occupied band always steps down well before its nibble could overflow, but
                // clamp rather than let a carry corrupt the band above it.
                if (((WeeksOfHealing >> shift) & 0xf) == 0xf)
                {
                    continue;
                }
                WeeksOfHealing += (uint)1 << shift;
            }
        }

        public byte RecoveryTimeLeft()
        {
            if (UnsurvivableWounds > 0)
            {
                return (byte)(28 - (WeeksOfHealing & 0xf0000000) / 0x10000000);
            }
            if (MortalWounds > 0)
            {
                return (byte)(21 - (WeeksOfHealing & 0x0f000000) / 0x01000000);
            }
            if (MassiveWounds > 0)
            {
                return (byte)(15 - (WeeksOfHealing & 0x00f00000) / 0x00100000);
            }
            if (CriticalWounds > 0)
            {
                return (byte)(10 - (WeeksOfHealing & 0x000f0000) / 0x00010000);
            }
            if (MajorWounds > 0)
            {
                return (byte)(6 - (WeeksOfHealing & 0x0000f000) / 0x00001000);
            }
            if (ModerateWounds > 0)
            {
                return (byte)(3 - (WeeksOfHealing & 0x00000f00) / 0x00000100);
            }
            if (MinorWounds > 0)
            {
                return 1;
            }
            return 0;
        }
    
        public void HealWounds()
        {
            WoundTotal = 0;
            Changed?.Invoke();
        }
    }

    public enum WoundLevel
    {
        None = 0,
        Negligible = 0x1,
        Minor = 0x10,
        Moderate = 0x100,
        Major = 0x1000,
        Critical = 0x10000,
        Massive = 0x100000,
        Mortal = 0x1000000,
        Unsurvivable = 0x10000000
    }

    public class HitLocationTemplate
    {
        public int Id;
        public string Name;
        public float NaturalArmor;
        public float WoundMultiplier;
        public uint CrippleWound;
        public uint SeverWound;
        public bool IsMotive;
        // Locations in the same hand group form one functional chain. For example,
        // a left arm and left hand share a group, so crippling either disables that
        // hand without counting injuries to both locations twice.
        public int? HandGroupId;
        public bool IsVital;
        // A location holding a progenoid gland destroys the soldier's geneseed when severed.
        public bool HoldsProgenoid;
        public int[] HitProbabilityMap;
    }

    public class HitLocation
    {
        private Wounds _wounds;
        internal event System.Action InjuryChanged;

        public Wounds Wounds
        {
            get => _wounds;
            set
            {
                if (_wounds != null)
                {
                    _wounds.Changed -= Wounds_Changed;
                }

                _wounds = value;
                if (_wounds != null)
                {
                    _wounds.Changed += Wounds_Changed;
                }
                if (_isCybernetic && IsSevered)
                {
                    _isCybernetic = false;
                }
                InjuryChanged?.Invoke();
            }
        }
        private bool _isCybernetic;

        // An augmetic is a property of the currently installed part. Once that part is severed,
        // the location follows the ordinary replacement flow again. The computed read also handles
        // a distal location covered by a severed cybernetic parent before the parent procedure
        // restores the group.
        public bool IsCybernetic
        {
            get => _isCybernetic && !IsSevered;
            set => _isCybernetic = value && !IsSevered;
        }
        public float Armor;
        
        public bool IsSevered
        {
            get
            {
                return Wounds.WoundTotal >= (uint)Template.SeverWound
                    || IsCoveredBySeveredParent;
            }
        }

        public bool IsCrippled
        {
            get
            {
                return Wounds.WoundTotal >= (uint)Template.CrippleWound;
            }
        }

        // A location qualifies for a cybernetic/vat-grown replacement when it matters for
        // function (a limb, weapon hand, or a vital location) and has reached its cripple or
        // sever threshold (PRD 4.8). This is the single source of truth shared by the
        // Apothecarium view and the weekly healing pass: an eligible location does not heal
        // naturally — it stays frozen until a replacement procedure restores it.
        public bool IsReplacementEligible
        {
            get
            {
                bool canMatterForFunction = Template.IsMotive
                    || Template.HandGroupId.HasValue
                    || Template.IsVital;
                return canMatterForFunction
                    && (IsSevered || IsCrippled)
                    && !IsCoveredBySeveredParent;
            }
        }

        // A severed proximal arm already takes its grouped hand with it. The arm replacement
        // restores the complete hand group, so the distal location must not create a second
        // procedure of its own or receive independent field/natural care.
        public bool IsCoveredBySeveredParent =>
            OwningBody?.GetReplacementParent(this)?.IsSevered == true;

        internal Body OwningBody { get; set; }

        public HitLocationTemplate Template { get; private set; }
        public HitLocation(HitLocationTemplate template)
        {
            Template = template;
            Wounds = new Wounds(0, 0);
            IsCybernetic = false;
            Armor = 0;
        }

        public HitLocation(HitLocationTemplate template, bool isCybernetic, float armor, 
            uint woundTotal, uint weeksOfHealing)
        {
            Template = template;
            Wounds = new Wounds(woundTotal, weeksOfHealing);
            IsCybernetic = isCybernetic;
            Armor = armor;
        }

        private void Wounds_Changed()
        {
            if (_isCybernetic && IsSevered)
            {
                _isCybernetic = false;
            }
            InjuryChanged?.Invoke();
        }

        public override string ToString()
        {
            if(IsSevered)
            {
                return Template.Name + ": <color=red>Severed</color>";
            }
            else if(IsCrippled)
            {
                return Template.Name + ": <color=red>Crippled</color>";
            }
            else if(Wounds.WoundTotal >= (uint)WoundLevel.Unsurvivable)
            {
                return Template.Name + ": <color=red>Unsurvivable</color>";
            }
            else if (Wounds.WoundTotal >= (uint)WoundLevel.Mortal)
            {
                return Template.Name + ":<color=red> Mortal</color>";
            }
            else if (Wounds.WoundTotal >= (uint)WoundLevel.Massive)
            {
                return Template.Name + ": <color=maroon>Massive</color>";
            }
            else if (Wounds.WoundTotal >= (uint)WoundLevel.Critical)
            {
                return Template.Name + ": <color=maroon>Critical</color>";
            }
            else if (Wounds.WoundTotal >= (uint)WoundLevel.Major)
            {
                return Template.Name + ": <color=orange>Major</color>";
            }
            else if (Wounds.WoundTotal >= (uint)WoundLevel.Moderate)
            {
                return Template.Name + ": <color=orange>Moderate</color>";
            }
            else if (Wounds.WoundTotal >= (uint)WoundLevel.Minor)
            {
                return Template.Name + ": <color=green>Minor</color>";
            }
            else if (Wounds.WoundTotal >= (uint)WoundLevel.Negligible)
            {
                return Template.Name + ": <color=green>Negligible</color>";
            }
            return Template.Name + ": No wounds";
        }
    }

    public class BodyTemplate
    {
        public HitLocationTemplate[] HitLocations;

        public BodyTemplate(IEnumerable<HitLocationTemplate> hitLocations)
        {
            HitLocations = hitLocations.ToArray();
        }
    }

    public class HumanBodyTemplate : BodyTemplate
    {
        private static HumanBodyTemplate _instance;

        public static HumanBodyTemplate Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new HumanBodyTemplate();
                }
                return _instance;
            }
        }

        static readonly List<HitLocationTemplate> list =
            [
                new HitLocationTemplate
                {
                    Id = 0,
                    Name = "Brain",
                    NaturalArmor = 2,
                    WoundMultiplier = 4,
                    HitProbabilityMap = new int[3] { 30, 30, 30 },
                    CrippleWound = (uint)WoundLevel.Critical,
                    SeverWound = (uint) WoundLevel.Massive,
                    IsMotive = false,
                    IsVital = true
                },

                new HitLocationTemplate
                {
                    Id = 1,
                    Name = "Eyes",
                    NaturalArmor = 0,
                    WoundMultiplier = 4,
                    HitProbabilityMap = new int[3] { 1, 1, 1 },
                    CrippleWound = (uint)WoundLevel.Moderate,
                    SeverWound = (uint)WoundLevel.Major,
                    IsMotive = false,
                    IsVital = false
                },

                new HitLocationTemplate
                {
                    Id = 2,
                    Name = "Face",
                    NaturalArmor = 0,
                    WoundMultiplier = 1,
                    HitProbabilityMap = new int[3] { 75, 75, 75 },
                    CrippleWound = (uint)WoundLevel.Critical,
                    SeverWound = (uint)WoundLevel.Massive,
                    IsMotive = false,
                    IsVital = true,
                    HoldsProgenoid = true
                },

                new HitLocationTemplate
                {
                    Id = 3,
                    Name = "Torso",
                    NaturalArmor = 0,
                    WoundMultiplier = 1,
                    HitProbabilityMap = new int[3] { 480, 480, 30 },
                    CrippleWound = (uint)WoundLevel.Massive,
                    SeverWound = (uint)WoundLevel.Unsurvivable,
                    IsMotive = false,
                    IsVital = true,
                    HoldsProgenoid = true
                },

                new HitLocationTemplate
                {
                    Id = 4,
                    Name = "Left Arm",
                    NaturalArmor = 0,
                    WoundMultiplier = 1,
                    HitProbabilityMap = new int[3] { 96, 96, 15 },
                    CrippleWound = 3 * (uint)WoundLevel.Major,
                    SeverWound = 3 * (uint)WoundLevel.Critical,
                    IsMotive = false,
                    HandGroupId = 0,
                    IsVital = false
                },

                new HitLocationTemplate
                {
                    Id = 5,
                    Name = "Right Arm",
                    NaturalArmor = 0,
                    WoundMultiplier = 1,
                    HitProbabilityMap = new int[3] { 96, 96, 15 },
                    CrippleWound = 3 * (uint)WoundLevel.Major,
                    SeverWound = 3 * (uint)WoundLevel.Critical,
                    IsMotive = false,
                    HandGroupId = 1,
                    IsVital = false
                },

                new HitLocationTemplate
                {
                    Id = 6,
                    Name = "Left Hand",
                    NaturalArmor = 0,
                    WoundMultiplier = 1,
                    HitProbabilityMap = new int[3] { 20, 20, 20 },
                    CrippleWound = (uint)WoundLevel.Major,
                    SeverWound = (uint)WoundLevel.Critical,
                    IsMotive = false,
                    HandGroupId = 0,
                    IsVital = false
                },

                new HitLocationTemplate
                {
                    Id = 7,
                    Name = "Right Hand",
                    NaturalArmor = 0,
                    WoundMultiplier = 1,
                    HitProbabilityMap = new int[3] { 20, 20, 20 },
                    CrippleWound = (uint)WoundLevel.Major,
                    SeverWound = (uint)WoundLevel.Critical,
                    IsMotive = false,
                    HandGroupId = 1,
                    IsVital = false
                },

                new HitLocationTemplate
                {
                    Id = 8,
                    Name = "Vitals",
                    NaturalArmor = 2,
                    WoundMultiplier = 1.5f,
                    HitProbabilityMap = new int[3] { 100, 100, 10 },
                    CrippleWound = (uint)WoundLevel.Critical,
                    SeverWound = (uint)WoundLevel.Massive,
                    IsMotive = false,
                    IsVital = true
                },

                new HitLocationTemplate
                {
                    Id = 9,
                    Name = "Left Leg",
                    NaturalArmor = 0,
                    WoundMultiplier = 1,
                   HitProbabilityMap = new int[3] { 160, 80, 1 },
                    // Cripple raised Critical -> Massive by
                    // Database/RulesMigration_LegCrippleThreshold.sql, and sever then raised
                    // Massive -> Mortal by Database/RulesMigration_LegSeverThreshold.sql
                    // (CasualtyRealism.md §2.1). The two must stay one band apart: with sever at
                    // Massive every leg wound that felled a marine also took the leg off, deleting
                    // "crippled but not severed" -- the state §2.3's Incapacitated outcome needs.
                    // Keep in step with the DB or this fallback silently amputates legs the loaded
                    // rules would only cripple.
                    CrippleWound = (uint)WoundLevel.Massive,
                    SeverWound = (uint)WoundLevel.Mortal,
                    IsMotive = true,
                    IsVital = false
                },

                new HitLocationTemplate
                {
                    Id = 10,
                    Name = "Right Leg",
                    NaturalArmor = 0,
                    WoundMultiplier = 1,
                    HitProbabilityMap = new int[3] { 160, 80, 1 },
                    // Cripple raised Critical -> Massive by
                    // Database/RulesMigration_LegCrippleThreshold.sql, sever raised Massive ->
                    // Mortal by Database/RulesMigration_LegSeverThreshold.sql
                    // (CasualtyRealism.md §2.1). The two must stay one band apart so that
                    // "crippled but not severed" stays reachable for legs. Keep in step with the DB.
                    CrippleWound = (uint)WoundLevel.Massive,
                    SeverWound = (uint)WoundLevel.Mortal,
                    IsMotive = true,
                    IsVital = false
                },

                new HitLocationTemplate
                {
                    Id = 11,
                    Name = "Left Foot",
                    NaturalArmor = 0,
                    WoundMultiplier = 1,
                    HitProbabilityMap = new int[3] { 15, 7, 0 },
                    CrippleWound = (uint)WoundLevel.Major,
                    SeverWound = (uint)WoundLevel.Critical,
                    IsMotive = true,
                    IsVital = false
                },

                new HitLocationTemplate
                {
                    Id = 12,
                    Name = "Right Foot",
                    NaturalArmor = 0,
                    WoundMultiplier = 1,
                    HitProbabilityMap = new int[3] { 15, 7, 0 },
                    CrippleWound = (uint)WoundLevel.Major,
                    SeverWound = (uint)WoundLevel.Critical,
                    IsMotive = true,
                    IsVital = false
                }
            ];

        private HumanBodyTemplate() : base(list) { }
    }

    public class TyranidWarriorBodyTemplate : BodyTemplate
    {
        private static TyranidWarriorBodyTemplate _instance;

        public static TyranidWarriorBodyTemplate Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new TyranidWarriorBodyTemplate();
                }
                return _instance;
            }
        }

        static readonly List<HitLocationTemplate> list =
            [
                new HitLocationTemplate
                {
                    Id = 0,
                    Name = "Brain",
                    NaturalArmor = 2,
                    WoundMultiplier = 4,
                    HitProbabilityMap = new int[3] { 30, 30, 30 },
                    CrippleWound = (uint)WoundLevel.Critical,
                    SeverWound = (uint) WoundLevel.Massive,
                    IsMotive = false,
                    IsVital = true
                },

                new HitLocationTemplate
                {
                    Id = 1,
                    Name = "Eyes",
                    NaturalArmor = 0,
                    WoundMultiplier = 4,
                    HitProbabilityMap = new int[3] { 1, 1, 1 },
                    CrippleWound = (uint)WoundLevel.Moderate,
                    SeverWound = (uint)WoundLevel.Major,
                    IsMotive = false,
                    IsVital = false
                },

                new HitLocationTemplate
                {
                    Id = 2,
                    Name = "Face",
                    NaturalArmor = 0,
                    WoundMultiplier = 1,
                    HitProbabilityMap = new int[3] { 75, 75, 75 },
                    CrippleWound = (uint)WoundLevel.Critical,
                    SeverWound = (uint)WoundLevel.Massive,
                    IsMotive = false,
                    IsVital = true,
                    HoldsProgenoid = true
                },

                new HitLocationTemplate
                {
                    Id = 3,
                    Name = "Torso",
                    NaturalArmor = 0,
                    WoundMultiplier = 1,
                    HitProbabilityMap = new int[3] { 480, 480, 30 },
                    CrippleWound = (uint)WoundLevel.Massive,
                    SeverWound = (uint)WoundLevel.Unsurvivable,
                    IsMotive = false,
                    IsVital = true,
                    HoldsProgenoid = true
                },

                new HitLocationTemplate
                {
                    Id = 4,
                    Name = "Left Arm",
                    NaturalArmor = 0,
                    WoundMultiplier = 1,
                    HitProbabilityMap = new int[3] { 96, 96, 15 },
                    CrippleWound = 3 * (uint)WoundLevel.Major,
                    SeverWound = 3 * (uint)WoundLevel.Critical,
                    IsMotive = false,
                    HandGroupId = 0,
                    IsVital = false
                },

                new HitLocationTemplate
                {
                    Id = 5,
                    Name = "Left Talon",
                    NaturalArmor = 0,
                    WoundMultiplier = 1,
                    HitProbabilityMap = new int[3] { 72, 72, 15 },
                    CrippleWound = 2 * (uint)WoundLevel.Major,
                    SeverWound = 2 * (uint)WoundLevel.Critical,
                    IsMotive = false,
                    IsVital = false
                },

                new HitLocationTemplate
                {
                    Id = 6,
                    Name = "Right Arm",
                    NaturalArmor = 0,
                    WoundMultiplier = 1,
                    HitProbabilityMap = new int[3] { 96, 96, 15 },
                    CrippleWound = 3 * (uint)WoundLevel.Major,
                    SeverWound = 3 * (uint)WoundLevel.Critical,
                    IsMotive = false,
                    HandGroupId = 1,
                    IsVital = false
                },

                new HitLocationTemplate
                {
                    Id = 7,
                    Name = "Right Talon",
                    NaturalArmor = 0,
                    WoundMultiplier = 1,
                    HitProbabilityMap = new int[3] { 72, 72, 15 },
                    CrippleWound = 2 * (uint)WoundLevel.Major,
                    SeverWound = 2 * (uint)WoundLevel.Critical,
                    IsMotive = false,
                    IsVital = false
                },

                new HitLocationTemplate
                {
                    Id = 8,
                    Name = "Left Hand",
                    NaturalArmor = 0,
                    WoundMultiplier = 1,
                    HitProbabilityMap = new int[3] { 20, 20, 20 },
                    CrippleWound = (uint)WoundLevel.Major,
                    SeverWound = (uint)WoundLevel.Critical,
                    IsMotive = false,
                    HandGroupId = 0,
                    IsVital = false
                },

                new HitLocationTemplate
                {
                    Id = 9,
                    Name = "Right Hand",
                    NaturalArmor = 0,
                    WoundMultiplier = 1,
                    HitProbabilityMap = new int[3] { 20, 20, 20 },
                    CrippleWound = (uint)WoundLevel.Major,
                    SeverWound = (uint)WoundLevel.Critical,
                    IsMotive = false,
                    HandGroupId = 1,
                    IsVital = false
                },

                new HitLocationTemplate
                {
                    Id = 10,
                    Name = "Vitals",
                    NaturalArmor = 2,
                    WoundMultiplier = 1.5f,
                    HitProbabilityMap = new int[3] { 100, 100, 10 },
                    CrippleWound = (uint)WoundLevel.Critical,
                    SeverWound = (uint)WoundLevel.Massive,
                    IsMotive = false,
                    IsVital = true
                },

                new HitLocationTemplate
                {
                    Id = 11,
                    Name = "Left Leg",
                    NaturalArmor = 0,
                    WoundMultiplier = 1,
                    HitProbabilityMap = new int[3] { 160, 80, 1 },
                    // Cripple raised Critical -> Massive by
                    // Database/RulesMigration_LegCrippleThreshold.sql, sever raised Massive ->
                    // Mortal by Database/RulesMigration_LegSeverThreshold.sql
                    // (CasualtyRealism.md §2.1). The two must stay one band apart so that
                    // "crippled but not severed" stays reachable for legs. Keep in step with the DB.
                    CrippleWound = (uint)WoundLevel.Massive,
                    SeverWound = (uint)WoundLevel.Mortal,
                    IsMotive = true,
                    IsVital = false
                },

                new HitLocationTemplate
                {
                    Id = 12,
                    Name = "Right Leg",
                    NaturalArmor = 0,
                    WoundMultiplier = 1,
                    HitProbabilityMap = new int[3] { 160, 80, 1 },
                    // Cripple raised Critical -> Massive by
                    // Database/RulesMigration_LegCrippleThreshold.sql, sever raised Massive ->
                    // Mortal by Database/RulesMigration_LegSeverThreshold.sql
                    // (CasualtyRealism.md §2.1). The two must stay one band apart so that
                    // "crippled but not severed" stays reachable for legs. Keep in step with the DB.
                    CrippleWound = (uint)WoundLevel.Massive,
                    SeverWound = (uint)WoundLevel.Mortal,
                    IsMotive = true,
                    IsVital = false
                },

                new HitLocationTemplate
                {
                    Id = 13,
                    Name = "Left Foot",
                    NaturalArmor = 0,
                    WoundMultiplier = 1,
                    HitProbabilityMap = new int[3] { 15, 7, 0 },
                    CrippleWound = (uint)WoundLevel.Major,
                    SeverWound = (uint)WoundLevel.Critical,
                    IsMotive = true,
                    IsVital = false
                },

                new HitLocationTemplate
                {
                    Id = 14,
                    Name = "Right Foot",
                    NaturalArmor = 0,
                    WoundMultiplier = 1,
                    HitProbabilityMap = new int[3] { 15, 7, 0 },
                    CrippleWound = (uint)WoundLevel.Major,
                    SeverWound = (uint)WoundLevel.Critical,
                    IsMotive = true,
                    IsVital = false
                }
            ];

        private TyranidWarriorBodyTemplate() : base(list) { }
    }

    public class Body
    {
        public HitLocation[] HitLocations { get; private set; }

        /// <summary>
        /// The stance's total hit-lottery weight, indexed by <c>(int)Stance</c> -- the denominator
        /// every per-location share is taken against. An array rather than a
        /// <c>Dictionary&lt;Stance, int&gt;</c> because it is read once per removal estimate on the
        /// battle planner's hot path, where the hash lookup and its comparer call were pure
        /// overhead against three contiguous ints. <see cref="HitLocationTemplate.HitProbabilityMap"/>,
        /// which it sums, was already indexed this way.
        /// </summary>
        public int[] TotalProbabilityMap { get; private set; }
        public int InjuryRevision { get; private set; }

        public Body(List<HitLocation> hitLocations)
        {
            HitLocations = hitLocations.ToArray();
            AttachLocationsToBody();
            SubscribeToInjuries();
            TotalProbabilityMap = BuildTotalProbabilityMap(HitLocations);
        }

        public Body(BodyTemplate template)
        {
            HitLocations = template.HitLocations.Select(hlt => new HitLocation(hlt)).ToArray();
            AttachLocationsToBody();
            SubscribeToInjuries();
            TotalProbabilityMap = BuildTotalProbabilityMap(HitLocations);
        }

        /// <summary>
        /// Returns the more proximal member of a hand group, such as the arm for a hand. The
        /// rules data expresses that relationship through the larger cripple threshold on the
        /// proximal location, avoiding name-based special cases for non-human species.
        /// </summary>
        public HitLocation GetReplacementParent(HitLocation location)
        {
            if (location?.Template?.HandGroupId == null)
            {
                return null;
            }

            return HitLocations
                .Where(candidate => candidate != location
                    && candidate.Template.HandGroupId == location.Template.HandGroupId
                    && candidate.Template.CrippleWound > location.Template.CrippleWound)
                .OrderByDescending(candidate => candidate.Template.CrippleWound)
                .ThenByDescending(candidate => candidate.Template.SeverWound)
                .FirstOrDefault();
        }

        private void AttachLocationsToBody()
        {
            foreach (HitLocation location in HitLocations)
            {
                location.OwningBody = this;
            }
        }

        private static int[] BuildTotalProbabilityMap(HitLocation[] hitLocations)
        {
            return
            [
                hitLocations.Sum(hl => hl.Template.HitProbabilityMap[(int)Stance.Standing]),
                hitLocations.Sum(hl => hl.Template.HitProbabilityMap[(int)Stance.Kneeling]),
                hitLocations.Sum(hl => hl.Template.HitProbabilityMap[(int)Stance.Prone])
            ];
        }

        private void SubscribeToInjuries()
        {
            foreach (HitLocation location in HitLocations)
            {
                location.InjuryChanged += OnInjuryChanged;
            }
        }

        private void OnInjuryChanged()
        {
            InjuryRevision++;
        }
    }
}
