using System.Configuration;
using System.Data;
using System.Windows;
using NarraVoice.Core.Services;
using Application = System.Windows.Application;

namespace NarraVoice
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnExit(ExitEventArgs e)
        {
            QwenServerManager.Instance.Shutdown();
            base.OnExit(e);
        }
    }
}