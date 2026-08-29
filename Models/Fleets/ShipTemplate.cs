namespace OnlyWar.Models.Fleets
{
    public class BoatTemplate
    {
        public int Id { get; }
        public string ClassName { get; }
        public ushort SoldierCapacity { get; }

        public BoatTemplate(int id, string className, ushort soldierCapacity)
        {
            Id = id;
            ClassName = className;
            SoldierCapacity = soldierCapacity;
        }
    }

    public class ShipTemplate : BoatTemplate
    {
        public ushort BoatCapacity { get; }
        public ushort LanderCapacity { get; }
        /// <summary>
        /// Authored precedence for deterministic flagship succession. Higher values win; this is
        /// deliberately separate from presentation names and soldier capacity.
        /// </summary>
        public int FlagshipPrecedence { get; }
        public int HullSize { get; }

        public ShipTemplate(int id, string className, ushort soldierCapacity, 
                            ushort boatCapacity, ushort landerCapacity,
                            int flagshipPrecedence = 0,
                            int hullSize = 0)
            : base(id, className, soldierCapacity)
        {
            BoatCapacity = boatCapacity;
            LanderCapacity = landerCapacity;
            FlagshipPrecedence = flagshipPrecedence;
            HullSize = hullSize;
        }
    }
}
