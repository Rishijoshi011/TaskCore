using System;

namespace TaskCore.ViewModels
{
    /// <summary>
    /// Interface for ViewModels that support activation/deactivation lifecycle.
    /// This allows ViewModels to pause/resume work when views are shown/hidden.
    /// </summary>
    public interface IActivatableView
    {
        /// <summary>
        /// Called when the view is activated/shown.
        /// ViewModels should start timers, begin polling, and resume work here.
        /// </summary>
        void OnActivated();

        /// <summary>
        /// Called when the view is deactivated/hidden.
        /// ViewModels should stop timers, cancel tasks, and pause work here.
        /// </summary>
        void OnDeactivated();
    }
}
