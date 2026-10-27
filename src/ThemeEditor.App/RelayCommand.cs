// --------------------------------------------------
// Umami 3DS Theme Editor - RelayCommand.cs
// --------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;

namespace ThemeEditor.App
{
    /// <summary>
    ///     Minimal stand-in for WPF's CommandManager: commands subscribe to <see cref="RequerySuggested" />
    ///     and get re-queried whenever something that may influence their state changed.
    /// </summary>
    public static class CommandManager
    {
        private static int _pending;

        public static event EventHandler RequerySuggested;

        /// <summary>Called (on the UI thread) when an async command throws, instead of tearing the app down.</summary>
        public static Action<Exception> UnhandledCommandError { get; set; }

        /// <summary>
        ///     Every view model change asks for a requery, so a dragged color slider used to re-evaluate every command
        ///     dozens of times per frame. The requests are coalesced into one requery per dispatcher pass.
        /// </summary>
        public static void InvalidateRequerySuggested()
        {
            if (Interlocked.Exchange(ref _pending, 1) == 1)
                return;
            Dispatcher.UIThread.Post(RequeryNow, DispatcherPriority.Background);
        }

        /// <summary>Re-queries all commands immediately (tests and code that needs the new state right away).</summary>
        public static void RequeryNow()
        {
            Interlocked.Exchange(ref _pending, 0);
            RequerySuggested?.Invoke(null, EventArgs.Empty);
        }

        internal static void ReportError(Exception ex)
        {
            var handler = UnhandledCommandError;
            if (handler == null)
                throw ex;
            if (Dispatcher.UIThread.CheckAccess())
                handler(ex);
            else
                Dispatcher.UIThread.Post(() => handler(ex));
        }
    }

    public class RelayCommand<T> : ICommand
    {
        private readonly Predicate<T> _canExecute;
        private readonly Action<T> _execute;

        public RelayCommand(Action<T> execute) : this(execute, null) {}

        public RelayCommand(Action<T> execute, Predicate<T> canExecute)
        {
            if (execute == null)
                throw new ArgumentNullException(nameof(execute));
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter)
        {
            return _canExecute == null || _canExecute(parameter is T ? (T) parameter : default(T));
        }

        public event EventHandler CanExecuteChanged
        {
            add
            {
                if (_canExecute != null)
                    CommandManager.RequerySuggested += value;
            }
            remove
            {
                if (_canExecute != null)
                    CommandManager.RequerySuggested -= value;
            }
        }

        public void Execute(object parameter)
        {
            _execute(parameter is T ? (T) parameter : default(T));
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Func<bool> _canExecute;
        private readonly Action _execute;

        public RelayCommand(Action execute) : this(execute, null) {}

        public RelayCommand(Action execute, Func<bool> canExecute)
        {
            if (execute == null)
                throw new ArgumentNullException(nameof(execute));
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter)
        {
            return _canExecute == null || _canExecute();
        }

        public event EventHandler CanExecuteChanged
        {
            add
            {
                if (_canExecute != null)
                    CommandManager.RequerySuggested += value;
            }
            remove
            {
                if (_canExecute != null)
                    CommandManager.RequerySuggested -= value;
            }
        }

        public void Execute(object parameter)
        {
            _execute();
        }
    }

    public class RelayCommandAsync<TU> : ICommand
    {
        private readonly Func<bool> _canExecute;
        private readonly Func<Task<TU>> _execute;
        private readonly Action<TU> _postExecute;
        private readonly Action _preExecute;
        private long _isExecuting;

        public RelayCommandAsync(
            Func<Task<TU>> execute,
            Func<bool> canExecute = null,
            Action preExecute = null,
            Action<TU> postExecute = null)
        {
            _preExecute = preExecute;
            _postExecute = postExecute;
            _execute = execute;
            _canExecute = canExecute ?? (() => true);
        }

        public void RaiseCanExecuteChanged()
        {
            CommandManager.InvalidateRequerySuggested();
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameter)
        {
            if (Interlocked.Read(ref _isExecuting) != 0)
                return false;
            return _canExecute();
        }

        public async void Execute(object parameter)
        {
            Interlocked.Exchange(ref _isExecuting, 1);
            RaiseCanExecuteChanged();
            _preExecute?.Invoke();
            TU retVal;
            try
            {
                retVal = await _execute.Invoke();
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref _isExecuting, 0);
                RaiseCanExecuteChanged();
                CommandManager.ReportError(ex);
                return;
            }
            Interlocked.Exchange(ref _isExecuting, 0);
            RaiseCanExecuteChanged();
            try
            {
                _postExecute?.Invoke(retVal);
            }
            catch (Exception ex)
            {
                CommandManager.ReportError(ex);
            }
        }
    }

    public class RelayCommandAsync<T, TU> : ICommand
    {
        private readonly Func<T, bool> _canExecute;
        private readonly Func<T, Task<TU>> _execute;
        private readonly Action<TU> _postExecute;
        private readonly Action<T> _preExecute;
        private long _isExecuting;

        public RelayCommandAsync(
            Func<T, Task<TU>> execute,
            Func<T, bool> canExecute = null,
            Action<T> preExecute = null,
            Action<TU> postExecute = null)
        {
            _preExecute = preExecute;
            _postExecute = postExecute;
            _execute = execute;
            _canExecute = canExecute ?? (o => true);
        }

        public void RaiseCanExecuteChanged()
        {
            CommandManager.InvalidateRequerySuggested();
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameter)
        {
            if (Interlocked.Read(ref _isExecuting) != 0)
                return false;
            return _canExecute(parameter is T ? (T) parameter : default(T));
        }

        public async void Execute(object parameter)
        {
            var arg = parameter is T ? (T) parameter : default(T);
            Interlocked.Exchange(ref _isExecuting, 1);
            RaiseCanExecuteChanged();
            _preExecute?.Invoke(arg);
            TU retVal;
            try
            {
                retVal = await _execute(arg);
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref _isExecuting, 0);
                RaiseCanExecuteChanged();
                CommandManager.ReportError(ex);
                return;
            }
            Interlocked.Exchange(ref _isExecuting, 0);
            RaiseCanExecuteChanged();
            try
            {
                _postExecute?.Invoke(retVal);
            }
            catch (Exception ex)
            {
                CommandManager.ReportError(ex);
            }
        }
    }
}
