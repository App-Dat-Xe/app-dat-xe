namespace RideHailingApp
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();

            Routing.RegisterRoute(nameof(TripHistoryPage), typeof(TripHistoryPage));
        }
    }
}
