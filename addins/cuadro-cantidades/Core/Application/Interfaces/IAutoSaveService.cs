namespace CQEING_CuadroCantidades.Core.Application.Interfaces;

public interface IAutoSaveService
{
    void StartMonitoring(System.ComponentModel.INotifyPropertyChanged viewModel);
    void StopMonitoring();
    void RequestSave(bool immediate = false);
    void NotifyChange();
    void Suspend();
    void Resume();
    bool IsSuspended { get; }
}
