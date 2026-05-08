using Go2HDR.Models;
using Go2HDR.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows.Controls;

namespace Go2HDR.Views.Pages;

public partial class SdrCurvePage : Page
{
    private SdrCurveViewModel Vm => (SdrCurveViewModel)DataContext;
    private double _editingOldValue;

    public SdrCurvePage()
    {
        InitializeComponent();
        DataContext = App.Services.GetRequiredService<SdrCurveViewModel>();
    }

    private void OnPreparingCellForEdit(object sender, DataGridPreparingCellForEditEventArgs e)
    {
        if (e.Column.Header?.ToString() == "SDR Level")
            _editingOldValue = ((CurvePoint)e.Row.Item).SdrValue;
    }

    private void OnCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.Column.Header?.ToString() != "SDR Level") return;

        var item = (CurvePoint)e.Row.Item;

        if (e.EditAction == DataGridEditAction.Cancel)
        {
            item.SdrValue = _editingOldValue;
            return;
        }

        if (e.EditAction != DataGridEditAction.Commit) return;

        // Reject values outside 0–100; revert to what was there before editing
        if (item.SdrValue < 0 || item.SdrValue > 100)
        {
            item.SdrValue = _editingOldValue;
            return;
        }

        item.SdrValue = Math.Round(item.SdrValue);
        Dispatcher.InvokeAsync(() => Vm.SaveCurveCommand.Execute(null));
    }
}
