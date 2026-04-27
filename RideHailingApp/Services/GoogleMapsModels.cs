using System.Text.Json.Serialization;

namespace RideHailingApp.Services
{
    // ─── Routes API v2 ───

    public class RouteRequest
    {
        [JsonPropertyName("origin")]
        public RouteWaypoint Origin { get; set; } = new();

        [JsonPropertyName("destination")]
        public RouteWaypoint Destination { get; set; } = new();

        [JsonPropertyName("travelMode")]
        public string TravelMode { get; set; } = "DRIVE";

        [JsonPropertyName("routingPreference")]
        public string RoutingPreference { get; set; } = "TRAFFIC_AWARE";

        [JsonPropertyName("computeAlternativeRoutes")]
        public bool ComputeAlternativeRoutes { get; set; } = false;

        [JsonPropertyName("languageCode")]
        public string LanguageCode { get; set; } = "vi";

        [JsonPropertyName("units")]
        public string Units { get; set; } = "METRIC";
    }

    public class RouteWaypoint
    {
        [JsonPropertyName("address")]
        public string? Address { get; set; }

        [JsonPropertyName("location")]
        public RouteLocation? Location { get; set; }
    }

    public class RouteLocation
    {
        [JsonPropertyName("latLng")]
        public LatLng LatLng { get; set; } = new();
    }

    public class LatLng
    {
        [JsonPropertyName("latitude")]
        public double Latitude { get; set; }

        [JsonPropertyName("longitude")]
        public double Longitude { get; set; }
    }

    public class RouteResponse
    {
        [JsonPropertyName("routes")]
        public List<RouteInfo> Routes { get; set; } = new();
    }

    public class RouteInfo
    {
        [JsonPropertyName("distanceMeters")]
        public int DistanceMeters { get; set; }

        [JsonPropertyName("duration")]
        public string Duration { get; set; } = "0s";  // e.g. "1200s"

        [JsonPropertyName("polyline")]
        public RoutePolyline? Polyline { get; set; }

        [JsonPropertyName("legs")]
        public List<RouteLeg> Legs { get; set; } = new();

        public double DistanceKm => DistanceMeters / 1000.0;

        public int DurationSeconds
        {
            get
            {
                if (string.IsNullOrEmpty(Duration)) return 0;
                var s = Duration.TrimEnd('s');
                return int.TryParse(s, out var sec) ? sec : 0;
            }
        }

        public int DurationMinutes => DurationSeconds / 60;
    }

    public class RoutePolyline
    {
        [JsonPropertyName("encodedPolyline")]
        public string EncodedPolyline { get; set; } = "";
    }

    public class RouteLeg
    {
        [JsonPropertyName("distanceMeters")]
        public int DistanceMeters { get; set; }

        [JsonPropertyName("duration")]
        public string Duration { get; set; } = "0s";
    }

    public class RouteResult
    {
        public bool IsSuccess { get; set; }
        public double DistanceKm { get; set; }
        public int DurationMinutes { get; set; }
        public string EncodedPolyline { get; set; } = "";
        public List<(double Lat, double Lon)> DecodedPoints { get; set; } = new();
        public string? ErrorMessage { get; set; }
    }

    // ─── Places Autocomplete API ───

    public class AutocompleteRequest
    {
        [JsonPropertyName("input")]
        public string Input { get; set; } = "";

        [JsonPropertyName("locationBias")]
        public LocationBias? LocationBias { get; set; }

        [JsonPropertyName("languageCode")]
        public string LanguageCode { get; set; } = "vi";

        [JsonPropertyName("regionCode")]
        public string RegionCode { get; set; } = "vn";
    }

    public class LocationBias
    {
        [JsonPropertyName("circle")]
        public LocationCircle Circle { get; set; } = new();
    }

    public class LocationCircle
    {
        [JsonPropertyName("center")]
        public LatLng Center { get; set; } = new();

        [JsonPropertyName("radius")]
        public double Radius { get; set; } = 50000;  // meters
    }

    public class AutocompleteResponse
    {
        [JsonPropertyName("suggestions")]
        public List<PlaceSuggestion> Suggestions { get; set; } = new();
    }

    public class PlaceSuggestion
    {
        [JsonPropertyName("placePrediction")]
        public PlacePrediction? PlacePrediction { get; set; }

        public string DisplayText => PlacePrediction?.Text?.Text ?? "";
        public string PlaceId => PlacePrediction?.PlaceId ?? "";
    }

    public class PlacePrediction
    {
        [JsonPropertyName("place")]
        public string Place { get; set; } = "";

        [JsonPropertyName("placeId")]
        public string PlaceId { get; set; } = "";

        [JsonPropertyName("text")]
        public PlaceText? Text { get; set; }

        [JsonPropertyName("structuredFormat")]
        public PlaceStructuredFormat? StructuredFormat { get; set; }
    }

    public class PlaceText
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = "";
    }

    public class PlaceStructuredFormat
    {
        [JsonPropertyName("mainText")]
        public PlaceText? MainText { get; set; }

        [JsonPropertyName("secondaryText")]
        public PlaceText? SecondaryText { get; set; }
    }

    // ─── Geocoding API ───

    public class GeocodingResponse
    {
        [JsonPropertyName("results")]
        public List<GeocodingResult> Results { get; set; } = new();

        [JsonPropertyName("status")]
        public string Status { get; set; } = "";
    }

    public class GeocodingResult
    {
        [JsonPropertyName("formatted_address")]
        public string FormattedAddress { get; set; } = "";

        [JsonPropertyName("geometry")]
        public GeocodingGeometry? Geometry { get; set; }
    }

    public class GeocodingGeometry
    {
        [JsonPropertyName("location")]
        public LatLng Location { get; set; } = new();
    }

    // ─── FCM / Backend models ───

    public class DeviceTokenRequest
    {
        public int UserId { get; set; }
        public string DeviceToken { get; set; } = "";
        public string Platform { get; set; } = "android";  // "android" | "ios"
    }

    public class MatchingJobInfo
    {
        public int TripId { get; set; }
        public string Status { get; set; } = "";  // "Queued" | "Matching" | "Matched" | "Failed"
        public int RetryCount { get; set; }
        public string? AssignedDriverId { get; set; }
        public DateTime EnqueuedAt { get; set; }
    }
}
