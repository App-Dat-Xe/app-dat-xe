namespace RideHailingApi.Services
{
    public class MaintenanceModeService
    {
        private volatile bool _isActive = false;
        private string _message = "Hệ thống đang bảo trì. Vui lòng thử lại sau.";
        private DateTime? _estimatedEndTime = null;

        public bool IsActive => _isActive;
        public string Message => _message;
        public DateTime? EstimatedEndTime => _estimatedEndTime;

        public void Activate(string? message = null, DateTime? estimatedEndTime = null)
        {
            if (!string.IsNullOrWhiteSpace(message)) _message = message;
            _estimatedEndTime = estimatedEndTime;
            _isActive = true;
        }

        public void Deactivate()
        {
            _isActive = false;
            _message           = "Hệ thống đang bảo trì. Vui lòng thử lại sau.";
            _estimatedEndTime  = null;
        }
    }
}
