using Go2HDR.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows.Controls;

namespace Go2HDR.Views.Pages;

public partial class SdrCurvePage : Page
{
    public SdrCurvePage()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<SdrCurveViewModel>();
    }
}
