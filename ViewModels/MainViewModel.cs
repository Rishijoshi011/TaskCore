namespace TaskCore.ViewModels
{
    public class MainViewModel
    {
        public ProcessesViewModel ProcessesVM { get; set; }
        public GraphsViewModel GraphsVM { get; set; }
        public DetailsViewModel DetailsVM { get; set; }
        public SettingsViewModel SettingsVM { get; set; }

        public MainViewModel()
        {
            ProcessesVM = new ProcessesViewModel();
            GraphsVM = new GraphsViewModel();
            DetailsVM = new DetailsViewModel();
            SettingsVM = new SettingsViewModel();
        }
    }
}
