using System.Text;

namespace RecipeOptimizer;

public partial class App : Application
{
    public App()
    {
        // Show something ASAP so we know if the ctor is reached
        MainPage = new ContentPage
        {
            Title = "Booting…",
            Content = new VerticalStackLayout
            {
                Padding = 20,
                Children =
                {
                    new Label { Text = "Booting… (App() reached)", FontSize = 18 },
                    new Label { Text = "Next: InitializeComponent()", FontSize = 14 }
                }
            }
        };

        AttachGlobalHandlers();

        string phase = "InitializeComponent";
        try
        {
            phase = "InitializeComponent";
            InitializeComponent(); // parse App.xaml (currently minimal)

            phase = "Create AppShell";
            MainPage = new AppShell(); // parse AppShell.xaml and create pages
        }
        catch (Exception ex)
        {
            var dump = Dump(phase, ex);
            System.Diagnostics.Debug.WriteLine(dump);
            MainPage = ErrorPage(dump);
        }
    }

    private static ContentPage ErrorPage(string text) =>
        new ContentPage
        {
            Title = "Startup Error",
            Content = new ScrollView { Content = new Label { Text = text, Margin = 16, FontSize = 13 } },
            BackgroundColor = Colors.White
        };

    private static void AttachGlobalHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            System.Diagnostics.Debug.WriteLine($"[UnhandledException] {e.ExceptionObject}");

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            System.Diagnostics.Debug.WriteLine($"[UnobservedTaskException] {e.Exception}");
            e.SetObserved();
        };
    }

    private static string Dump(string phase, Exception ex)
    {
        var sb = new StringBuilder();
        sb.AppendLine("===========================================");
        sb.AppendLine($" MAUI startup failure during: {phase}");
        sb.AppendLine("===========================================");
        int depth = 0;
        var cur = ex;
        while (cur != null)
        {
            sb.AppendLine($"[{depth}] {cur.GetType().FullName}: {cur.Message}");
            if (!string.IsNullOrWhiteSpace(cur.StackTrace))
                sb.AppendLine(cur.StackTrace);
            cur = cur.InnerException;
            depth++;
        }
        return sb.ToString();
    }
}
