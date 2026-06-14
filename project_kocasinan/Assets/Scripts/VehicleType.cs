namespace BusJam
{
    /// <summary>Vehicle shapes. Capacity (seat count) AND grid footprint differ per type:
    /// a vehicle occupies <see cref="Vehicles.CellLength"/> cells in a line along its exit
    /// direction (Car 1 / Bus 2). The solvable-by-construction grid handles this.</summary>
    public enum VehicleType { Car, Bus }

    /// <summary>How a level mixes vehicle types. Maps to a per-vehicle distribution
    /// in <see cref="LevelGenerator"/>.</summary>
    public enum VehicleMix { BusOnly, BusesVaried, CarsOnly, CarsAndBuses, WithLimo }

    public static class Vehicles
    {
        // Seat counts: sedan/car 4, regular bus 10.
        public const int CarSeats = 4;
        public const int BusSeats = 10;

        public static int DefaultCapacity(VehicleType t)
        {
            switch (t)
            {
                case VehicleType.Car:  return CarSeats;
                default:               return BusSeats;
            }
        }

        /// <summary>Grid cells a vehicle occupies along its exit direction.</summary>
        public static int CellLength(VehicleType t)
        {
            switch (t)
            {
                case VehicleType.Car:  return 1;
                default:               return 2; // Bus
            }
        }
    }
}
