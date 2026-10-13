using System.Windows.Input;

namespace Orbit.Services;

/// <summary>Minimal ICommand for tray menu items.</summary>
public sealed class RelayCommand(Action execute) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => execute();
}
