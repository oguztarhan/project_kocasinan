namespace BusJam
{
    /// <summary>Vehicle shapes. Capacity (seat count) AND grid footprint differ per type:
    /// a vehicle occupies <see cref="Vehicles.CellLength"/> cells in a line along its exit
    /// direction (Car 1 / Minivan 1 / Bus 2). The solvable-by-construction grid handles this.
    /// NOTE: Minivan is appended LAST on purpose — enum values are serialized by index, so keeping
    /// Car=0, Bus=1 unchanged avoids corrupting any existing data; Minivan=2.</summary>
    public enum VehicleType { Car, Bus, Minivan }

    /// <summary>How a level mixes vehicle types. Maps to a per-vehicle distribution
    /// in <see cref="LevelGenerator"/>. New minivan-inclusive mixes are appended LAST to keep
    /// existing serialized LevelDefinition.vehicleMix values stable.</summary>
    public enum VehicleMix { BusOnly, BusesVaried, CarsOnly, CarsAndBuses, WithLimo, CarsAndMinivans, AllThree }

    public static class Vehicles
    {
        // Seat counts: sedan/car 4, minivan 6, regular bus 10.
        public const int CarSeats = 4;
        public const int MinivanSeats = 6;
        public const int BusSeats = 10;

        public static int DefaultCapacity(VehicleType t)
        {
            switch (t)
            {
                case VehicleType.Car:     return CarSeats;
                case VehicleType.Minivan: return MinivanSeats;
                default:                  return BusSeats;
            }
        }

        /// <summary>Grid cells a vehicle occupies along its exit direction. A Minivan is a compact
        /// van — same 1-cell footprint as a car (it just seats more); only the Bus spans 2 cells.</summary>
        public static int CellLength(VehicleType t)
        {
            switch (t)
            {
                case VehicleType.Car:     return 1;
                case VehicleType.Minivan: return 1;
                default:                  return 2; // Bus
            }
        }
    }
}
