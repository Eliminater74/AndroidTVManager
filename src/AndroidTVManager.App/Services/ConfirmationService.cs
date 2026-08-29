using System.Windows;

namespace AndroidTVManager.App.Services;

public interface IConfirmationService
{
    bool Confirm(string title, string message);
}

public sealed class WpfConfirmationService : IConfirmationService
{
    public bool Confirm(string title, string message)
        => System.Windows.MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
}
