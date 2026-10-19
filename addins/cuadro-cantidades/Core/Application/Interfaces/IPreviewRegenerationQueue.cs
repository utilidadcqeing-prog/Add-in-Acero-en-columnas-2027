using System;
using System.Threading;
using System.Threading.Tasks;

namespace CQEING_CuadroCantidades.Core.Application.Interfaces;

public interface IPreviewRegenerationQueue
{
    void Enqueue(string activityCode, Func<CancellationToken, Task> taskFactory);
    void Cancel(string activityCode);
    void CancelAll();
}
