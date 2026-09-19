using Microsoft.AspNetCore.SignalR;

namespace MinGo.MyBillBook.Hubs;

public class NotificationHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
    }

    public async Task NotifyBatchImported(int batchId, string fileName)
    {
        await Clients.All.SendAsync("BatchImported", batchId, fileName);
    }

    public async Task NotifyBatchProcessed(int batchId, int recordCount)
    {
        await Clients.All.SendAsync("BatchProcessed", batchId, recordCount);
    }

    public async Task NotifyDataSynced()
    {
        await Clients.All.SendAsync("DataSynced");
    }
}
