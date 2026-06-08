namespace BusJam
{
    /// <summary>Vehicle shapes. Capacity (seat count) AND grid footprint differ per type:
    /// a vehicle occupies <see cref="Vehicles.CellLength"/> cells in a line along its exit
    /// direction (Car 1 / Bus 2 / Limo 3). The solvable-by-construction grid handles this.</summary>
    public enum VehicleType { Car, Bus, Limo }

    /// <summary>How a level mixes vehicle types. Maps to a per-vehicle distribution
    /// in <see cref="LevelGenerator"/>.</summary>
    public enum VehicleMix { BusOnly, BusesVaried, CarsOnly, CarsAndBuses, WithLimo }

    public static class Vehicles
    {
        // Proposed seat counts.
        public const int CarSeats = 2;
        public const int BusSeats = 3;
        public const int LimoSeats = 5;

        public static int DefaultCapacity(VehicleType t)
        {
            switch (t)
            {
                case VehicleType.Car:  return CarSeats;
                case VehicleType.Limo: return LimoSeats;
                default:               return BusSeats;
            }
        }

        /// <summary>Grid cells a vehicle occupies along its exit direction.</summary>
        public static int CellLength(VehicleType t)
        {
            switch (t)
            {
                case VehicleType.Car:  return 1;
                case VehicleType.Limo: return 3;
                default:               return 2; // Bus
            }
        }
    }
}
