namespace RideHailingApi.Models
{
    public class TripRequest
    {
        public int UserID { get; set; }
        // Thêm = string.Empty; vào sau mỗi biến string
        public string PickupLocation { get; set; } = string.Empty;
        public string DropoffLocation { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;
    }
}