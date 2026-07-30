using System;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TaskCore.ViewModels
{
    /// <summary>
    /// Base class for ViewModels with activation/deactivation lifecycle support.
    /// Provides common patterns for cancellation tokens, disposal, and lifecycle management.
    /// </summary>
    public abstract partial class ActivatableViewModelBase : ObservableObject, IActivatableView, IDisposable
    {
        private bool _disposed;
        private CancellationTokenSource? _lifecycleCts;

        /// <summary>
        /// Cancellation token for lifecycle-aware operations.
        /// Cancelled when the ViewModel is deactivated or disposed.
        /// </summary>
        protected CancellationToken LifecycleCancellationToken => _lifecycleCts?.Token ?? CancellationToken.None;

        /// <summary>
        /// Whether the ViewModel is currently active.
        /// </summary>
        protected bool IsActive { get; private set; }

        /// <summary>
        /// Called when the view is activated/shown.
        /// Override to start timers, begin polling, and resume work.
        /// </summary>
        public virtual void OnActivated()
        {
            if (_disposed) return;
            
            // Reset cancellation token for new activation
            _lifecycleCts?.Cancel();
            _lifecycleCts?.Dispose();
            _lifecycleCts = new CancellationTokenSource();
            
            IsActive = true;
            OnActivationCore(_lifecycleCts.Token);
        }

        /// <summary>
        /// Called when the view is deactivated/hidden.
        /// Override to stop timers, cancel tasks, and pause work.
        /// </summary>
        public virtual void OnDeactivated()
        {
            if (_disposed) return;
            
            IsActive = false;
            
            // Cancel all lifecycle operations
            _lifecycleCts?.Cancel();
            
            OnDeactivationCore();
        }

        /// <summary>
        /// Core activation logic. Override in derived classes to start work.
        /// </summary>
        /// <param name="token">Cancellation token that will be cancelled on deactivation.</param>
        protected virtual void OnActivationCore(CancellationToken token)
        {
        }

        /// <summary>
        /// Core deactivation logic. Override in derived classes to stop work.
        /// </summary>
        protected virtual void OnDeactivationCore()
        {
        }

        /// <summary>
        /// Disposes the ViewModel and releases all resources.
        /// </summary>
        public virtual void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Cancel all lifecycle operations
            _lifecycleCts?.Cancel();
            _lifecycleCts?.Dispose();
            _lifecycleCts = null;

            OnDisposeCore();
            
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Core disposal logic. Override in derived classes to release resources.
        /// </summary>
        protected virtual void OnDisposeCore()
        {
        }

        /// <summary>
        /// Safely executes an action with cancellation support.
        /// </summary>
        protected void SafeExecute(Action action)
        {
            try
            {
                if (!_disposed && action != null)
                {
                    action();
                }
            }
            catch (OperationCanceledException)
            {
                // Expected during cancellation
            }
            catch (ObjectDisposedException)
            {
                // Expected if already disposed
            }
            catch
            {
                // Suppress other errors to prevent crashes
            }
        }

        /// <summary>
        /// Safely executes a function with cancellation support.
        /// </summary>
        protected T SafeExecute<T>(Func<T> func, T defaultValue = default!)
        {
            try
            {
                if (!_disposed && func != null)
                {
                    return func();
                }
            }
            catch (OperationCanceledException)
            {
                // Expected during cancellation
            }
            catch (ObjectDisposedException)
            {
                // Expected if already disposed
            }
            catch
            {
                // Suppress other errors to prevent crashes
            }
            return defaultValue;
        }
    }
}
