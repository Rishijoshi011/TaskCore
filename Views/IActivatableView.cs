namespace TaskCore.Views
{
    /// <summary>
    /// Interface for views that need to know when they become active or inactive.
    /// Used to pause/resume heavy operations when tabs are switched.
    /// </summary>
    public interface IActivatableView
    {
        /// <summary>
        /// Called when the view becomes visible (tab is selected)
        /// </summary>
        void OnActivated();
        
        /// <summary>
        /// Called when the view is hidden (user switched to another tab)
        /// </summary>
        void OnDeactivated();
    }
}
