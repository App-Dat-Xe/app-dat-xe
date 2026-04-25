namespace RideHailingApi.Models
{
    public class PendingTripItem
    {
        public int TripID { get; set; }
        public int UserID { get; set; }
        public string PickupLocation { get; set; } = "";
        public string DropoffLocation { get; set; } = "";
        public string Region { get; set; } = "";
        public DateTime? CreatedAt { get; set; }
    }

    public class TripStatusRequest
    {
        public string Status { get; set; } = string.Empty;   // Accepted | Arrived | Completed
        public string Message { get; set; } = string.Empty;
    }

    public class TripHistoryItem
    {
        public int TripID { get; set; }
        public int UserID { get; set; }
        public int? DriverID { get; set; }
        public string PickupLocation { get; set; } = string.Empty;
        public string DropoffLocation { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime? CreatedAt { get; set; }
    }
}
