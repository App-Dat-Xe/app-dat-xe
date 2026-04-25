using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace RideHailingApi.Hubs
{
    [Authorize]
    public class TripHub : Hub
    {
        // Client → Server: tham gia phòng theo chuyến đi
        public async Task JoinTripGroup(string tripId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"Trip_{tripId}");
        }

        // Client → Server: tài xế gửi toạ độ GPS liên tục (3-5s/lần)
        public async Task UpdateDriverLocation(string tripId, double lat, double lng)
        {
            await Clients.OthersInGroup($"Trip_{tripId}").SendAsync("OnLocationUpdated", lat, lng);
        }

        // Client → Server: dọn dẹp khi kết thúc chuyến
        public async Task LeaveTripGroup(string tripId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"Trip_{tripId}");
        }

        // Client → Server: tài xế tham gia pool nhận cuốc theo khu vực
        public async Task JoinDriverPool(string region)
            => await Groups.AddToGroupAsync(Context.ConnectionId, $"DriverPool_{region}");

        // Client → Server: tài xế rời pool nhận cuốc
        public async Task LeaveDriverPool(string region)
            => await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"DriverPool_{region}");
    }
}
