namespace RideHailingApp.Services
{
    // Xác định Region (North/South) từ tọa độ GPS.
    // Quy ước đơn giản: Latitude > 16.0 → North (HN), ngược lại → South (HCM).
    // Vĩ tuyến 16 chia đôi VN tại Đà Nẵng (~16.07°N).
    public class GeoLocatorService
    {
        private const double NorthSouthBoundaryLat = 16.0;
        private const string DefaultRegion = "South";

        public async Task<string> GetRegionAsync()
        {
            try
            {
                var location = await Geolocation.Default.GetLastKnownLocationAsync()
                               ?? await Geolocation.Default.GetLocationAsync(
                                   new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(5)));

                if (location == null) return GetCachedOrDefault();

                string region = location.Latitude >= NorthSouthBoundaryLat ? "North" : "South";
                Preferences.Set("currentRegion", region);
                Preferences.Set("regionName", region == "North" ? "Miền Bắc (HN)" : "Miền Nam (HCM)");
                return region;
            }
            catch
            {
                return GetCachedOrDefault();
            }
        }

        // Lấy region đã cache (tránh gọi GPS mỗi request)
        public string GetCachedRegion() => Preferences.Get("currentRegion", DefaultRegion);

        // Cho phép user override thủ công (vd: đang ở miền Trung)
        public void SetRegionManually(string region)
        {
            if (region != "North" && region != "South") return;
            Preferences.Set("currentRegion", region);
            Preferences.Set("regionName", region == "North" ? "Miền Bắc (HN)" : "Miền Nam (HCM)");
        }

        private string GetCachedOrDefault() => Preferences.Get("currentRegion", DefaultRegion);
    }
}
