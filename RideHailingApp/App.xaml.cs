namespace RideHailingApp
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();

            // Khôi phục dark mode từ lần dùng trước
            bool isDarkMode = Preferences.Get("isDarkMode", false);
            UserAppTheme = isDarkMode ? AppTheme.Dark : AppTheme.Light;

            bool isLoggedIn = Preferences.Get("isLoggedIn", false);

            if (isLoggedIn)
            {
                bool isDriver = Preferences.Get("isDriver", false);
                MainPage = isDriver ? (Page)new DriverShell() : new AppShell();
            }
            else
                MainPage = new NavigationPage(new LoginPage());
        }
    }
}