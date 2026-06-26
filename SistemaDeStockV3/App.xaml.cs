using SistemaDeStockV3.Services;

namespace SistemaDeStockV3
{
    public partial class App : Application
    {
        public App(DataService dataService)
        {
            InitializeComponent();

           try
            {
                // Task.Run mueve la ejecución al thread pool, donde no hay
                // SynchronizationContext que cause deadlock. GetAwaiter().GetResult()
                // bloquea el hilo principal hasta que termine, garantizando que
                // la DB esté lista antes de mostrar cualquier página.
                Task.Run(async () => await dataService.InitializeAsync()).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] Fallo al inicializar la base de datos: {ex.Message}");
            }

            MainPage = new MainPage();
        }
    }
}
